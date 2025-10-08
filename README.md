# EasyDispatch

A lightweight, simple Mediator library for .NET focused on ease-of-use and implementing the CQRS pattern.

[![NuGet](https://img.shields.io/nuget/v/EasyDispatch.svg)](https://www.nuget.org/packages/EasyDispatch/)
[![Downloads](https://img.shields.io/nuget/dt/EasyDispatch.svg)](https://www.nuget.org/packages/EasyDispatch/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

## Features

**Simple API** - Minimal boilerplate, intuitive API  
**CQRS Support** - Clean separation of queries, commands, and notifications  
**Streaming Queries** - Handle large datasets efficiently with `IAsyncEnumerable`  
**Pipeline Behaviors** - Add cross-cutting concerns like logging and validation  
**Polymorphic Dispatch** - Notifications handled by base class and interface handlers  
**Startup Validation** - Catch missing handlers at application startup  
**OpenTelemetry Support** - Built-in Activity tracing for observability  

## Installation

```bash
dotnet add package EasyDispatch
```

## Quick Start

### 1. Register the Mediator

```csharp
var builder = WebApplication.CreateBuilder(args);

// Register mediator and scan assembly for handlers
builder.Services.AddMediator(typeof(Program).Assembly);

var app = builder.Build();
```

### 2. Define a Query and Handler

```csharp
// Query
public record GetUserQuery(int UserId) : IQuery<UserDto>;

// Handler
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
```

### 3. Use the Mediator

```csharp
public class UserController : ControllerBase
{
    private readonly IMediator _mediator;

    public UserController(IMediator mediator) => _mediator = mediator;

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var query = new GetUserQuery(id);
        var user = await _mediator.SendAsync(query);
        return Ok(user);
    }
}
```

## Message Types

**Queries** - Read operations that return data
```csharp
public record GetUserQuery(int UserId) : IQuery<UserDto>;
```

**Commands** - Write operations that modify state
```csharp
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record DeleteUserCommand(int UserId) : ICommand;
```

**Notifications** - Events with multiple handlers (pub/sub)
```csharp
public record UserCreatedNotification(int UserId, string Name) : INotification;
```

**Streaming Queries** - Large datasets returned incrementally
```csharp
public record GetOrdersStreamQuery(int PageSize) : IStreamQuery<OrderDto>;
```

## Pipeline Behaviors

Add cross-cutting concerns like logging, validation, and caching:

```csharp
builder.Services.AddMediator(typeof(Program).Assembly)
    .AddOpenBehavior(typeof(LoggingBehavior<,>))
    .AddOpenBehavior(typeof(ValidationBehavior<,>))
    .AddOpenBehavior(typeof(PerformanceBehavior<,>));
```

## Configuration

```csharp
builder.Services.AddMediator(options =>
{
    // Assemblies to scan
    options.Assemblies = new[] { typeof(Program).Assembly };
    
    // Handler lifetime (default: Scoped)
    options.HandlerLifetime = ServiceLifetime.Scoped;
    
    // Notification strategy (default: StopOnFirstException)
    options.NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll;
    
    // Startup validation (default: None)
    options.StartupValidation = StartupValidation.FailFast;
});
```

## Documentation

**[View Full Documentation](../../wiki)** - Complete guide with examples

## Examples

### Command with Response

```csharp
public record CreateOrderCommand(
    int CustomerId,
    List<OrderItem> Items
) : ICommand<int>;

public class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, int>
{
    public async Task<int> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = new Order { CustomerId = command.CustomerId, Items = command.Items };
        await _repository.AddAsync(order, ct);
        return order.Id;
    }
}

// Usage
var command = new CreateOrderCommand(customerId: 123, items);
int orderId = await mediator.SendAsync(command);
```

### Notifications with Multiple Handlers

```csharp
public record UserCreatedNotification(int UserId, string Email) : INotification;

// Handler 1: Send welcome email
public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken ct)
    {
        await _emailService.SendWelcomeEmailAsync(notification.Email, ct);
    }
}

// Handler 2: Update analytics
public class UpdateAnalyticsHandler : INotificationHandler<UserCreatedNotification>
{
    public async Task Handle(UserCreatedNotification notification, CancellationToken ct)
    {
        await _analyticsService.TrackUserCreatedAsync(notification.UserId, ct);
    }
}

// Usage - both handlers execute
await mediator.PublishAsync(new UserCreatedNotification(userId, email));
```

### Streaming Large Datasets

```csharp
public record GetOrdersStreamQuery() : IStreamQuery<OrderDto>;

public class GetOrdersStreamQueryHandler : IStreamQueryHandler<GetOrdersStreamQuery, OrderDto>
{
    public async IAsyncEnumerable<OrderDto> Handle(
        GetOrdersStreamQuery query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var order in _repository.StreamAllOrdersAsync(ct))
        {
            yield return new OrderDto(order.Id, order.Total);
        }
    }
}

// Usage - process items as they arrive
await foreach (var order in mediator.StreamAsync(new GetOrdersStreamQuery()))
{
    Console.WriteLine($"Order {order.Id}: ${order.Total}");
}
```

## Requirements

- .NET 9.0 or later
- Microsoft.Extensions.DependencyInjection 9.0+

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
