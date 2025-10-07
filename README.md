# EasyDispatch

A Mediator implementation for .NET that implements the CQRS and Mediator patterns that aims for ease-of-use, and a relatively simple migration path from Mediatr.

[![.NET 9.0](https://img.shields.io/badge/.NET-9.0-blue.svg)](https://dotnet.microsoft.com/download)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

## Features

- **CQRS Support**: Separate interfaces for Commands, Queries, and Notifications
- **Pipeline Behaviors**: Extensible middleware pipeline for cross-cutting concerns
- **Flexible Notification Strategies**: Four built-in strategies for handling multiple notification handlers
- **Dependency Injection**: First-class support for Microsoft.Extensions.DependencyInjection
- **Minimal Dependencies**: Only depends on Microsoft.Extensions.DependencyInjection.Abstractions

## Installation

```bash
# Via NuGet (when published)
dotnet add package EasyDispatch

# Or add to your .csproj
<PackageReference Include="EasyDispatch" Version="1.0.x" />
```

## Quick Start

### 1. Define Your Messages

```csharp
using EasyDispatch.Contracts;

// Query - retrieves data
public record GetUserQuery(int UserId) : IQuery<UserDto>;

// Command - performs action, no return value
public record DeleteUserCommand(int UserId) : ICommand;

// Command - performs action, returns value
public record CreateUserCommand(string Name, string Email) : ICommand<int>;

// Notification - pub/sub event
public record UserCreatedNotification(int UserId, string Name) : INotification;
```

### 2. Implement Handlers

```csharp
using EasyDispatch.Handlers;

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
    private readonly IUserRepository _repository;

    public GetUserQueryHandler(IUserRepository repository)
    {
        _repository = repository;
    }

    public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
    {
        var user = await _repository.GetByIdAsync(query.UserId, cancellationToken);
        return new UserDto(user.Id, user.Name, user.Email);
    }
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
{
    private readonly IUserRepository _repository;

    public async Task<int> Handle(CreateUserCommand command, CancellationToken cancellationToken)
    {
        var user = new User { Name = command.Name, Email = command.Email };
        await _repository.AddAsync(user, cancellationToken);
        return user.Id;
    }
}

// Notification handlers - multiple handlers can exist
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        // Send welcome email
    }
}

public class AuditLogHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
    {
        // Log to audit system
    }
}
```

### 3. Register with DI

```csharp
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Simple registration - scans assembly for handlers
services.AddMediator(typeof(Program).Assembly);

// With configuration
services.AddMediator(options =>
{
    options.Assemblies = new[] { typeof(Program).Assembly };
    options.HandlerLifetime = ServiceLifetime.Scoped;
    options.NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException;
})
.AddOpenBehavior(typeof(LoggingBehavior<,>))
.AddOpenBehavior(typeof(ValidationBehavior<,>));
```

### 4. Use the Mediator

```csharp
public class UserController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _mediator.SendAsync(new GetUserQuery(id));
        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        var userId = await _mediator.SendAsync(
            new CreateUserCommand(request.Name, request.Email));
        
        await _mediator.PublishAsync(new UserCreatedNotification(userId, request.Name));
        
        return CreatedAtAction(nameof(GetUser), new { id = userId }, userId);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _mediator.SendAsync(new DeleteUserCommand(id));
        return NoContent();
    }
}
```

## Configuration Options

### MediatorOptions

```csharp
services.AddMediator(options =>
{
    // Required: Assemblies to scan for handlers
    options.Assemblies = new[] 
    { 
        typeof(Program).Assembly,
        typeof(HandlersAssembly).Assembly 
    };

    // Optional: Handler lifetime (default: Scoped)
    options.HandlerLifetime = ServiceLifetime.Scoped;

    // Optional: Notification publishing strategy (default: StopOnFirstException)
    options.NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll;

    // Optional: Validate handlers at startup (default: false)
    options.ValidateHandlersAtStartup = false;
});
```

## Notification Publishing Strategies

EasyDispatch provides four strategies for handling multiple notification handlers:

### StopOnFirstException (Default)
```csharp
options.NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException;
```
- Executes handlers sequentially
- Stops on first exception
- Maintains ordering guarantees
- **Use for**: Critical operations where any failure should halt execution

### ContinueOnException
```csharp
options.NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException;
```
- Executes handlers sequentially
- Continues on exceptions
- Collects all exceptions as AggregateException
- **Use for**: Logging, auditing where all handlers should attempt execution

### ParallelWhenAll
```csharp
options.NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll;
```
- Executes handlers in parallel
- Waits for all handlers to complete
- Collects exceptions as AggregateException
- **Use for**: Independent handlers that can run concurrently

### ParallelNoWait
```csharp
options.NotificationPublishStrategy = NotificationPublishStrategy.ParallelNoWait;
```
- Fire-and-forget parallel execution
- Returns immediately without waiting
- Logs errors but doesn't throw to caller
- **Use for**: Non-critical notifications, fire-and-forget events

## Pipeline Behaviors

Pipeline behaviors are middleware that wrap handler execution, perfect for cross-cutting concerns.

### Creating a Behavior

```csharp
using EasyDispatch.Behaviors;

public class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
    private readonly ILogger<LoggingBehavior<TMessage, TResponse>> _logger;

    public LoggingBehavior(ILogger<LoggingBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
    }

    public async Task<TResponse> Handle(
        TMessage message,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var messageName = typeof(TMessage).Name;
        
        _logger.LogInformation("Executing {MessageName}", messageName);
        
        try
        {
            var response = await next();
            _logger.LogInformation("Executed {MessageName} successfully", messageName);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {MessageName}", messageName);
            throw;
        }
    }
}
```

### Registering Behaviors

```csharp
// Open generic - applies to all messages
services.AddMediator(typeof(Program).Assembly)
    .AddOpenBehavior(typeof(LoggingBehavior<,>))
    .AddOpenBehavior(typeof(ValidationBehavior<,>))
    .AddOpenBehavior(typeof(PerformanceBehavior<,>));

// Specific message type
services.AddMediator(typeof(Program).Assembly)
    .AddBehavior<CreateUserCommand, int, TransactionBehavior>();
```

### Common Behavior Examples

#### Validation Behavior (FluentValidation)
```csharp
public class ValidationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
    private readonly IEnumerable<IValidator<TMessage>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TMessage>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TMessage message,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any())
            return await next();

        var context = new ValidationContext<TMessage>(message);
        var validationResults = await Task.WhenAll(
            _validators.Select(v => v.ValidateAsync(context, cancellationToken)));

        var failures = validationResults
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();

        if (failures.Count != 0)
            throw new ValidationException(failures);

        return await next();
    }
}
```

#### Performance Monitoring
```csharp
public class PerformanceBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
    private readonly ILogger<PerformanceBehavior<TMessage, TResponse>> _logger;
    private readonly Stopwatch _timer;

    public PerformanceBehavior(ILogger<PerformanceBehavior<TMessage, TResponse>> logger)
    {
        _logger = logger;
        _timer = new Stopwatch();
    }

    public async Task<TResponse> Handle(
        TMessage message,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        _timer.Start();
        var response = await next();
        _timer.Stop();

        var elapsedMilliseconds = _timer.ElapsedMilliseconds;
        if (elapsedMilliseconds > 500)
        {
            _logger.LogWarning(
                "Long Running Request: {MessageName} ({ElapsedMilliseconds} ms)",
                typeof(TMessage).Name,
                elapsedMilliseconds);
        }

        return response;
    }
}
```

#### Authorization
```csharp
public class AuthorizationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
    private readonly ICurrentUserService _currentUserService;
    private readonly IAuthorizationService _authorizationService;

    public async Task<TResponse> Handle(
        TMessage message,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        var authorizeAttributes = typeof(TMessage)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>()
            .ToList();

        if (!authorizeAttributes.Any())
            return await next();

        var currentUser = _currentUserService.GetCurrentUser();
        if (currentUser == null)
            throw new UnauthorizedException();

        foreach (var attribute in authorizeAttributes)
        {
            var authorized = await _authorizationService.AuthorizeAsync(
                currentUser, 
                attribute.Policy);
            
            if (!authorized)
                throw new ForbiddenException($"Access denied: {attribute.Policy}");
        }

        return await next();
    }
}
```

## Unit Type for Void Operations

EasyDispatch uses the `Unit` type internally to represent void operations in pipeline behaviors:

```csharp
// Void commands use Unit in behaviors
public class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
    public async Task<TResponse> Handle(
        TMessage message,
        Func<Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        // Works for both void and non-void commands
        return await next();
    }
}
```

This allows a single behavior implementation to work across all message types.

## Architecture

### Message Types

```
IQuery<TResponse>          - Retrieves data, no side effects
ICommand                   - Performs action, void return
ICommand<TResponse>        - Performs action, returns value
INotification              - Pub/sub event, multiple handlers
```

### Handler Types

```
IQueryHandler<TQuery, TResponse>              - Handles queries
ICommandHandler<TCommand>                     - Handles void commands
ICommandHandler<TCommand, TResponse>          - Handles commands with response
INotificationHandler<TNotification>           - Handles notifications
```

### Pipeline

```
Request → [Behavior 1] → [Behavior 2] → [Behavior N] → Handler → Response
```

## Error Handling

EasyDispatch provides clear, actionable error messages:

### Missing Handler

```
No handler registered for query 'GetUserQuery'.
Expected a handler implementing IQueryHandler<GetUserQuery, UserDto>.
Did you forget to call AddMediator() with the assembly containing your handlers?
```

### Notification Failures

Depending on your strategy:
- **StopOnFirstException**: Throws first exception immediately
- **ContinueOnException**: Collects all exceptions in `AggregateException`
- **ParallelWhenAll**: Collects all parallel exceptions in `AggregateException`
- **ParallelNoWait**: Logs errors, doesn't throw

### Migration from MediatR

EasyDispatch uses a very similar API to MediatR, making migration straightforward:

```csharp
// MediatR
public record GetUserQuery(int Id) : IRequest<UserDto>;
public class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDto> { }

// EasyDispatch
public record GetUserQuery(int Id) : IQuery<UserDto>;
public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto> { }
```

Main differences:
- `IRequest<T>` → `IQuery<T>` or `ICommand<T>`
- `IRequestHandler<,>` → `IQueryHandler<,>` or `ICommandHandler<,>`
- Additional notification strategies


### Development Setup

```bash
# Clone the repository
git clone https://github.com/yourusername/easydispatch.git

# Restore dependencies
dotnet restore

# Build
dotnet build

# Run tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Resources

- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Mediator Pattern](https://refactoring.guru/design-patterns/mediator)
- [Pipeline Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/pipes-and-filters)

## Support

- Issues: [GitHub Issues](https://github.com/saliej/easydispatch/issues)
- Discussions: [GitHub Discussions](https://github.com/saliej/easydispatch/discussions)
