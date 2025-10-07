using EasyDispatch;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EasyDispatch.IntegrationTests;

/// <summary>
/// Integration tests that verify the complete mediator pipeline end-to-end.
/// </summary>
public class IntegrationTests
{
    // Domain Messages
    private record GetUserQuery(int UserId) : IQuery<UserDto>;
    private record CreateUserCommand(string Name, string Email) : ICommand<int>;
    private record DeleteUserCommand(int UserId) : ICommand;
    private record UserCreatedNotification(int UserId, string Name) : INotification;

    private record UserDto(int Id, string Name, string Email);

    // Handlers
    private class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
    {
        public Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(new UserDto(query.UserId, "John Doe", "john@example.com"));
        }
    }

    private class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
    {
        public Task<int> Handle(CreateUserCommand command, CancellationToken cancellationToken)
        {
            // Simulate user creation returning new user ID
            return Task.FromResult(123);
        }
    }

    private class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand>
    {
        public static int DeletedUserId { get; set; }

        public Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
        {
            DeletedUserId = command.UserId;
            return Task.CompletedTask;
        }
    }

    private class EmailNotificationHandler : INotificationHandler<UserCreatedNotification>
    {
        public static List<string> SentEmails { get; } = [];

        public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
        {
            SentEmails.Add($"Welcome email sent to user {notification.UserId}");
            return Task.CompletedTask;
        }
    }

    private class AuditNotificationHandler : INotificationHandler<UserCreatedNotification>
    {
        public static List<string> AuditLog { get; } = [];

        public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
        {
            AuditLog.Add($"User created: {notification.Name} (ID: {notification.UserId})");
            return Task.CompletedTask;
        }
    }

    // Behaviors
    private class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        public static List<string> Logs { get; } = [];

        public async Task<TResponse> Handle(
            TMessage message,
            Func<Task<TResponse>> next,
            CancellationToken cancellationToken)
        {
            var messageName = typeof(TMessage).Name;
            Logs.Add($"[LOG] Executing {messageName}");
            
            var result = await next();
            
            Logs.Add($"[LOG] Executed {messageName}");
            return result;
        }
    }

    private class ValidationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        public static List<string> Validations { get; } = [];

        public async Task<TResponse> Handle(
            TMessage message,
            Func<Task<TResponse>> next,
            CancellationToken cancellationToken)
        {
            Validations.Add($"[VALIDATE] {typeof(TMessage).Name}");
            
            // Simple validation example
            if (message is CreateUserCommand cmd && string.IsNullOrEmpty(cmd.Name))
            {
                throw new InvalidOperationException("Name is required");
            }

            return await next();
        }
    }

    private class PerformanceBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        public static List<string> Metrics { get; } = [];

        public async Task<TResponse> Handle(
            TMessage message,
            Func<Task<TResponse>> next,
            CancellationToken cancellationToken)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var result = await next();
            stopwatch.Stop();
            
            Metrics.Add($"[PERF] {typeof(TMessage).Name} took {stopwatch.ElapsedMilliseconds}ms");
            return result;
        }
    }

    [Fact]
    public async Task CompleteFlow_Query_WithMultipleBehaviors()
    {
        // Arrange
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Clear();
        ValidationBehavior<GetUserQuery, UserDto>.Validations.Clear();
        PerformanceBehavior<GetUserQuery, UserDto>.Metrics.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly)
            .AddOpenBehavior(typeof(LoggingBehavior<,>))
            .AddOpenBehavior(typeof(ValidationBehavior<,>))
            .AddOpenBehavior(typeof(PerformanceBehavior<,>));

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new GetUserQuery(42);

        // Act
        var result = await mediator.SendAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("John Doe");

        // Verify behaviors executed
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Should().Contain(log => log.Contains("Executing GetUserQuery"));
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Should().Contain(log => log.Contains("Executed GetUserQuery"));
        ValidationBehavior<GetUserQuery, UserDto>.Validations.Should().Contain(val => val.Contains("GetUserQuery"));
        PerformanceBehavior<GetUserQuery, UserDto>.Metrics.Should().Contain(metric => metric.Contains("GetUserQuery"));
    }

    [Fact]
    public async Task CompleteFlow_CommandWithResponse_ReturnsValue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new CreateUserCommand("Jane Doe", "jane@example.com");

        // Act
        var userId = await mediator.SendAsync(command);

        // Assert
        userId.Should().Be(123);
    }

    [Fact]
    public async Task CompleteFlow_VoidCommand_ExecutesSuccessfully()
    {
        // Arrange
        DeleteUserCommandHandler.DeletedUserId = 0;

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new DeleteUserCommand(456);

        // Act
        await mediator.SendAsync(command);

        // Assert
        DeleteUserCommandHandler.DeletedUserId.Should().Be(456);
    }

    [Fact]
    public async Task CompleteFlow_Notification_ExecutesAllHandlers()
    {
        // Arrange
        EmailNotificationHandler.SentEmails.Clear();
        AuditNotificationHandler.AuditLog.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var notification = new UserCreatedNotification(789, "Alice");

        // Act
        await mediator.PublishAsync(notification);

        // Assert
        EmailNotificationHandler.SentEmails.Should().ContainSingle()
            .Which.Should().Contain("789");
        AuditNotificationHandler.AuditLog.Should().ContainSingle()
            .Which.Should().Contain("Alice");
    }

    [Fact]
    public async Task CompleteFlow_ValidationBehavior_CanPreventExecution()
    {
        // Arrange
        ValidationBehavior<CreateUserCommand, int>.Validations.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly)
            .AddOpenBehavior(typeof(ValidationBehavior<,>));

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new CreateUserCommand("", "invalid@example.com");

        // Act
        var act = async () => await mediator.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Name is required");
        
        ValidationBehavior<CreateUserCommand, int>.Validations.Should()
            .Contain(v => v.Contains("CreateUserCommand"));
    }

    [Fact]
    public async Task CompleteFlow_BehaviorsExecuteInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("First", executionOrder));
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("Second", executionOrder));
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("Third", executionOrder));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new GetUserQuery(1);

        // Act
        await mediator.SendAsync(query);

        // Assert
        executionOrder.Should().Equal(
            "First: Before",
            "Second: Before",
            "Third: Before",
            "Third: After",
            "Second: After",
            "First: After");
    }

    private class OrderTrackingBehavior : IPipelineBehavior<GetUserQuery, UserDto>
    {
        private readonly string _name;
        private readonly List<string> _log;

        public OrderTrackingBehavior(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public async Task<UserDto> Handle(
            GetUserQuery message,
            Func<Task<UserDto>> next,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}: Before");
            var result = await next();
            _log.Add($"{_name}: After");
            return result;
        }
    }

    [Fact]
    public async Task CompleteFlow_ScopedServices_IsolatedBetweenRequests()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        // Act & Assert - First scope
        using (var scope1 = provider.CreateScope())
        {
            var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
            var result1 = await mediator1.SendAsync(new GetUserQuery(1));
            result1.Should().NotBeNull();
        }

        // Act & Assert - Second scope (should be independent)
        using (var scope2 = provider.CreateScope())
        {
            var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
            var result2 = await mediator2.SendAsync(new GetUserQuery(2));
            result2.Should().NotBeNull();
            result2.Id.Should().Be(2);
        }
    }

    [Fact]
    public async Task CompleteFlow_CancellationToken_PropagatesToHandler()
    {
        // Arrange - Use manual registration to avoid conflicts with other handlers
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<CancellationTestQuery, UserDto>, CancellationAwareHandler>();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var query = new CancellationTestQuery(1);

        // Act
        var act = async () => await mediator.SendAsync(query, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // Separate query type to avoid conflicts with GetUserQuery
    private record CancellationTestQuery(int UserId) : IQuery<UserDto>;

    private class CancellationAwareHandler : IQueryHandler<CancellationTestQuery, UserDto>
    {
        public Task<UserDto> Handle(CancellationTestQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UserDto(query.UserId, "Test", "test@example.com"));
        }
    }
}