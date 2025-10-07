using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;

namespace EasyDispatch.UnitTests;

public class PipelineBehaviorTests
{
    private record TestQuery(int Value) : IQuery<int>;
    private record TestCommand(string Name) : ICommand;
    private record TestNotification(string Message) : INotification;

    private class TestQueryHandler : IQueryHandler<TestQuery, int>
    {
        public Task<int> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult(query.Value * 2);
        }
    }

    private class TestCommandHandler : ICommandHandler<TestCommand>
    {
        public List<string> ExecutionLog { get; } = [];

        public Task Handle(TestCommand command, CancellationToken cancellationToken)
        {
            ExecutionLog.Add($"Handler: {command.Name}");
            return Task.CompletedTask;
        }
    }

    private class TestNotificationHandler : INotificationHandler<TestNotification>
    {
        public List<string> ExecutionLog { get; } = [];

        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            ExecutionLog.Add($"Handler: {notification.Message}");
            return Task.CompletedTask;
        }
    }

    // Tracking behavior for testing execution order
    private class TrackingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TrackingBehavior(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public async Task<TResponse> Handle(
            TMessage message,
            Func<Task<TResponse>> next,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}: Before");
            var result = await next();
            _log.Add($"{_name}: After");
            return result;
        }
    }

    // Validation behavior for testing
    private class ValidationBehavior : IPipelineBehavior<TestQuery, int>
    {
        public async Task<int> Handle(
            TestQuery message,
            Func<Task<int>> next,
            CancellationToken cancellationToken)
        {
            if (message.Value < 0)
                throw new InvalidOperationException("Value cannot be negative");

            return await next();
        }
    }

    // Logging behavior for testing
    private class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        public List<string> Logs { get; } = [];

        public async Task<TResponse> Handle(
            TMessage message,
            Func<Task<TResponse>> next,
            CancellationToken cancellationToken)
        {
            Logs.Add($"Executing: {typeof(TMessage).Name}");
            var result = await next();
            Logs.Add($"Executed: {typeof(TMessage).Name}");
            return result;
        }
    }

    [Fact]
    public async Task SendAsync_Query_ExecutesBehaviorBeforeHandler()
    {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        
        services.AddScoped<IQueryHandler<TestQuery, int>, TestQueryHandler>();
        services.AddScoped<IPipelineBehavior<TestQuery, int>>(
            sp => new TrackingBehavior<TestQuery, int>("Behavior1", log));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(5);
        var result = await mediator.SendAsync(query);

        result.Should().Be(10);
        log.Should().Equal("Behavior1: Before", "Behavior1: After");
    }

    [Fact]
    public async Task SendAsync_Query_ExecutesMultipleBehaviorsInOrder()
    {
        // Arrange
        var log = new List<string>();
        var services = new ServiceCollection();
        
        services.AddScoped<IQueryHandler<TestQuery, int>, TestQueryHandler>();
        services.AddScoped<IPipelineBehavior<TestQuery, int>>(
            sp => new TrackingBehavior<TestQuery, int>("Behavior1", log));
        services.AddScoped<IPipelineBehavior<TestQuery, int>>(
            sp => new TrackingBehavior<TestQuery, int>("Behavior2", log));
        services.AddScoped<IPipelineBehavior<TestQuery, int>>(
            sp => new TrackingBehavior<TestQuery, int>("Behavior3", log));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(5);
        await mediator.SendAsync(query);

        // Behaviors should execute in FIFO order (first registered = outermost)
        log.Should().Equal(
            "Behavior1: Before",
            "Behavior2: Before",
            "Behavior3: Before",
            "Behavior3: After",
            "Behavior2: After",
            "Behavior1: After");
    }

    [Fact]
    public async Task SendAsync_Query_ValidationBehaviorCanShortCircuit()
    {
        // Arrange
        var services = new ServiceCollection();
        
        services.AddScoped<IQueryHandler<TestQuery, int>, TestQueryHandler>();
        services.AddScoped<IPipelineBehavior<TestQuery, int>, ValidationBehavior>();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(-5);
        var act = async () => await mediator.SendAsync(query);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Value cannot be negative");
    }

    [Fact]
    public async Task SendAsync_VoidCommand_ExecutesBehavior()
    {
        // Arrange
        var log = new List<string>();
        var handler = new TestCommandHandler();
        var services = new ServiceCollection();
        
        services.AddSingleton<ICommandHandler<TestCommand>>(handler);
        services.AddScoped<IPipelineBehavior<TestCommand, Unit>>(
            sp => new TrackingBehavior<TestCommand, Unit>("Behavior1", log));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var command = new TestCommand("Test");
        await mediator.SendAsync(command);

        handler.ExecutionLog.Should().ContainSingle().Which.Should().Be("Handler: Test");
        log.Should().Equal("Behavior1: Before", "Behavior1: After");
    }

    [Fact]
    public async Task PublishAsync_Notification_ExecutesBehaviorForEachHandler()
    {
        // Arrange
        var log = new List<string>();
        var handler1 = new TestNotificationHandler();
        var handler2 = new TestNotificationHandler();
        var services = new ServiceCollection();
        
        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        services.AddScoped<IPipelineBehavior<TestNotification, Unit>>(
            sp => new TrackingBehavior<TestNotification, Unit>("Behavior", log));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var notification = new TestNotification("Event");
        await mediator.PublishAsync(notification);

        handler1.ExecutionLog.Should().ContainSingle().Which.Should().Be("Handler: Event");
        handler2.ExecutionLog.Should().ContainSingle().Which.Should().Be("Handler: Event");
            
        // Behavior should execute twice (once per handler)
        log.Should().Equal(
            "Behavior: Before",
            "Behavior: After",
            "Behavior: Before",
            "Behavior: After");
    }

    [Fact]
    public async Task SendAsync_OpenGenericBehavior_ExecutesCorrectly()
    {
        // Arrange
        var loggingBehavior = new LoggingBehavior<TestQuery, int>();
        var services = new ServiceCollection();
        
        services.AddScoped<IQueryHandler<TestQuery, int>, TestQueryHandler>();
        services.AddSingleton<IPipelineBehavior<TestQuery, int>>(loggingBehavior);
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(5);
        var result = await mediator.SendAsync(query);

        result.Should().Be(10);
        loggingBehavior.Logs.Should().Equal(
            "Executing: TestQuery",
            "Executed: TestQuery");
    }

    [Fact]
    public async Task SendAsync_BehaviorReceivesCancellationToken()
    {
        // Arrange
        CancellationToken? receivedToken = null;
        var services = new ServiceCollection();
        
        services.AddScoped<IQueryHandler<TestQuery, int>, TestQueryHandler>();
        services.AddScoped<IPipelineBehavior<TestQuery, int>>(sp => 
            new DelegatingBehavior<TestQuery, int>((msg, next, ct) => 
            {
                receivedToken = ct;
                return next();
            }));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var cts = new CancellationTokenSource();

        // Act & Assert - Use explicit scope
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new TestQuery(5);
        await mediator.SendAsync(query, cts.Token);

        receivedToken.Should().NotBeNull();
        receivedToken.Value.Should().Be(cts.Token);
    }

    // Helper behavior for testing
    private class DelegatingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        private readonly Func<TMessage, Func<Task<TResponse>>, CancellationToken, Task<TResponse>> _handler;

        public DelegatingBehavior(Func<TMessage, Func<Task<TResponse>>, CancellationToken, Task<TResponse>> handler)
        {
            _handler = handler;
        }

        public Task<TResponse> Handle(TMessage message, Func<Task<TResponse>> next, CancellationToken cancellationToken)
        {
            return _handler(message, next, cancellationToken);
        }
    }
}