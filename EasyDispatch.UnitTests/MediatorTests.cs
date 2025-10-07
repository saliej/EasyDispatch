using System;
using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using NSubstitute;

namespace EasyDispatch.UnitTests;

public class MediatorTests
{
    // Test Messages
    public record TestQuery(int Id) : IQuery<string>;
    private record TestVoidCommand(string Name) : ICommand;
    private record TestCommandWithResponse(int Value) : ICommand<int>;
    private record TestNotification(string Message) : INotification;

    // Test Handlers
    private class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Query result for {query.Id}");
        }
    }

    private class TestVoidCommandHandler : ICommandHandler<TestVoidCommand>
    {
        public int CallCount { get; private set; }

        public Task Handle(TestVoidCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private class TestCommandWithResponseHandler : ICommandHandler<TestCommandWithResponse, int>
    {
        public Task<int> Handle(TestCommandWithResponse command, CancellationToken cancellationToken)
        {
            return Task.FromResult(command.Value * 2);
        }
    }

    private class TestNotificationHandler1 : INotificationHandler<TestNotification>
    {
        public List<string> ReceivedMessages { get; } = [];

        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            ReceivedMessages.Add($"Handler1: {notification.Message}");
            return Task.CompletedTask;
        }
    }

    private class TestNotificationHandler2 : INotificationHandler<TestNotification>
    {
        public List<string> ReceivedMessages { get; } = [];

        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            ReceivedMessages.Add($"Handler2: {notification.Message}");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SendAsync_Query_ReturnsExpectedResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<TestQuery, string>, TestQueryHandler>();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new TestQuery(42);

        // Act
        var result = await mediator.SendAsync(query);

        // Assert
        result.Should().Be("Query result for 42");
    }

    [Fact]
    public async Task SendAsync_VoidCommand_ExecutesHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler = new TestVoidCommandHandler();
        services.AddSingleton<ICommandHandler<TestVoidCommand>>(handler);
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new TestVoidCommand("Test");

        // Act
        await mediator.SendAsync(command);

        // Assert
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SendAsync_CommandWithResponse_ReturnsExpectedResult()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<ICommandHandler<TestCommandWithResponse, int>, TestCommandWithResponseHandler>();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new TestCommandWithResponse(10);

        // Act
        var result = await mediator.SendAsync(command);

        // Assert
        result.Should().Be(20);
    }

    [Fact]
    public async Task PublishAsync_Notification_ExecutesAllHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var handler1 = new TestNotificationHandler1();
        var handler2 = new TestNotificationHandler2();
        
        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var notification = new TestNotification("Hello");

        // Act
        await mediator.PublishAsync(notification);

        // Assert
        handler1.ReceivedMessages.Should().ContainSingle()
            .Which.Should().Be("Handler1: Hello");
        handler2.ReceivedMessages.Should().ContainSingle()
            .Which.Should().Be("Handler2: Hello");
    }

    [Fact]
    public async Task SendAsync_Query_ThrowsWhenHandlerNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new TestQuery(42);

        // Act
        var act = async () => await mediator.SendAsync(query);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    [Fact]
    public async Task SendAsync_Query_ThrowsWhenQueryIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = default(IQuery<string>);

        // Act
        var act = async () => await mediator.SendAsync(query!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("query");
    }

    [Fact]
    public async Task SendAsync_VoidCommand_ThrowsWhenCommandIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var act = async () => await mediator.SendAsync((ICommand)null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task SendAsync_CommandWithResponse_ThrowsWhenCommandIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = default(ICommand<int>);

        // Act
        var act = async () => await mediator.SendAsync(command!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("command");
    }

    [Fact]
    public async Task PublishAsync_ThrowsWhenNotificationIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Act
        var act = async () => await mediator.PublishAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("notification");
    }

    [Fact]
    public async Task PublishAsync_NoHandlers_DoesNotThrow()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var notification = new TestNotification("Test");

        // Act
        var act = async () => await mediator.PublishAsync(notification);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_CancellationToken_PassedToHandler()
    {
        // Arrange
        var handler = Substitute.For<IQueryHandler<TestQuery, string>>();
        handler.Handle(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns("result");

        var services = new ServiceCollection();
        services.AddSingleton(handler);
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        var query = new TestQuery(42);

        // Act
        await mediator.SendAsync(query, cts.Token);

        // Assert
        await handler.Received(1).Handle(
            Arg.Is<TestQuery>(q => q.Id == 42),
            Arg.Is<CancellationToken>(ct => ct == cts.Token));
    }

    [Fact]
    public async Task SendAsync_VoidCommand_ThrowsWhenHandlerNotRegistered()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new UnregisteredVoidCommand("test");

        // Act
        var act = async () => await mediator.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No handler registered*");
    }

    private record UnregisteredVoidCommand(string Name) : ICommand;
}