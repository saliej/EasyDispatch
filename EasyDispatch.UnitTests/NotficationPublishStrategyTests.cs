using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

public class NotificationPublishStrategyTests
{
    private record TestNotification(string Message) : INotification;

    private class SuccessfulHandler : INotificationHandler<TestNotification>
    {
        public List<string> ExecutionLog { get; } = new();

        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            ExecutionLog.Add($"Success: {notification.Message}");
            return Task.CompletedTask;
        }
    }

    private class FailingHandler : INotificationHandler<TestNotification>
    {
        public bool WasExecuted { get; private set; }

        public Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            WasExecuted = true;
            throw new InvalidOperationException("Handler intentionally failed");
        }
    }

    [Fact]
    public async Task StopOnFirstException_StopsOnFirstFailure()
    {
        // Arrange
        var handler1 = new SuccessfulHandler();
        var handler2 = new FailingHandler();
        var handler3 = new SuccessfulHandler();

        var services = new ServiceCollection();
        
        // Manual registration - don't use AddMediator to avoid assembly scanning
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();
        
        // Manually register handlers in specific order
        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler3);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");

            var act = async () => await mediator.PublishAsync(notification);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Handler intentionally failed");

            // First handler executed
            handler1.ExecutionLog.Should().ContainSingle();
            
            // Second handler executed and failed
            handler2.WasExecuted.Should().BeTrue();
            
            // Third handler should NOT execute (stopped on failure)
            handler3.ExecutionLog.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task ContinueOnException_ExecutesAllHandlers()
    {
        // Arrange
        var handler1 = new SuccessfulHandler();
        var handler2 = new FailingHandler();
        var handler3 = new SuccessfulHandler();

        var services = new ServiceCollection();
        
        // Manual registration
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();

        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler3);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");

            var act = async () => await mediator.PublishAsync(notification);

            var exception = await act.Should().ThrowAsync<AggregateException>();
            exception.Which.InnerExceptions.Should().ContainSingle()
                .Which.Message.Should().Contain("Handler intentionally failed");

            // All handlers should execute
            handler1.ExecutionLog.Should().ContainSingle();
            handler2.WasExecuted.Should().BeTrue();
            handler3.ExecutionLog.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ContinueOnException_NoExceptionWhenAllSucceed()
    {
        // Arrange
        var handler1 = new SuccessfulHandler();
        var handler2 = new SuccessfulHandler();

        var services = new ServiceCollection();
        
        // Manual registration
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();

        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");

            await mediator.PublishAsync(notification);

            handler1.ExecutionLog.Should().ContainSingle();
            handler2.ExecutionLog.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ParallelWhenAll_ExecutesInParallel()
    {
        // Arrange
        var executionTimes = new List<DateTime>();
        var services = new ServiceCollection();
        
        // Manual registration
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();

        services.AddSingleton<INotificationHandler<TestNotification>>(
            new DelayHandler(executionTimes, TimeSpan.FromMilliseconds(50)));
        services.AddSingleton<INotificationHandler<TestNotification>>(
            new DelayHandler(executionTimes, TimeSpan.FromMilliseconds(50)));
        services.AddSingleton<INotificationHandler<TestNotification>>(
            new DelayHandler(executionTimes, TimeSpan.FromMilliseconds(50)));

        var provider = services.BuildServiceProvider();

        // Act
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");
            
            var startTime = DateTime.UtcNow;
            await mediator.PublishAsync(notification);
            var totalTime = DateTime.UtcNow - startTime;

            // Assert - Should complete in ~50ms, not 150ms
            // (parallel execution, not sequential)
            totalTime.Should().BeLessThan(TimeSpan.FromMilliseconds(120));
            executionTimes.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task ParallelWhenAll_ThrowsAggregateException()
    {
        // Arrange
        var handler1 = new FailingHandler();
        var handler2 = new FailingHandler();

        var services = new ServiceCollection();
        
        // Manual registration
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();

        services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
        services.AddSingleton<INotificationHandler<TestNotification>>(handler2);

        var provider = services.BuildServiceProvider();

        // Act & Assert
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");

            var act = async () => await mediator.PublishAsync(notification);

            var exception = await act.Should().ThrowAsync<AggregateException>();
            exception.Which.InnerExceptions.Should().HaveCount(2);
            
            handler1.WasExecuted.Should().BeTrue();
            handler2.WasExecuted.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ParallelNoWait_ReturnsImmediately()
    {
        // Arrange
        var handler1Started = new TaskCompletionSource<bool>();
        var handler1CanComplete = new TaskCompletionSource<bool>();

        var services = new ServiceCollection();
        
        // Manual registration
        var options = new MediatorOptions
        {
            Assemblies = new[] { typeof(NotificationPublishStrategyTests).Assembly },
            NotificationPublishStrategy = NotificationPublishStrategy.ParallelNoWait
        };
        services.AddSingleton(options);
        services.AddScoped<IMediator, Mediator>();

        services.AddSingleton<INotificationHandler<TestNotification>>(
            new LongRunningHandler(handler1Started, handler1CanComplete));

        var provider = services.BuildServiceProvider();

        // Act
        using (var scope = provider.CreateScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var notification = new TestNotification("Test");

            var publishTask = mediator.PublishAsync(notification);
            
            // Should return immediately
            publishTask.IsCompleted.Should().BeTrue();
            await publishTask; // Should complete instantly

            // But handler might still be running
            await Task.Delay(50); // Give it time to start
            
            // Signal handler can complete
            handler1CanComplete.SetResult(true);
            
            // Wait for handler to actually start
            await handler1Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    // Helper handlers
    private class DelayHandler : INotificationHandler<TestNotification>
    {
        private readonly List<DateTime> _executionTimes;
        private readonly TimeSpan _delay;

        public DelayHandler(List<DateTime> executionTimes, TimeSpan delay)
        {
            _executionTimes = executionTimes;
            _delay = delay;
        }

        public async Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            lock (_executionTimes)
            {
                _executionTimes.Add(DateTime.UtcNow);
            }
            await Task.Delay(_delay, cancellationToken);
        }
    }

    private class LongRunningHandler : INotificationHandler<TestNotification>
    {
        private readonly TaskCompletionSource<bool> _started;
        private readonly TaskCompletionSource<bool> _canComplete;

        public LongRunningHandler(
            TaskCompletionSource<bool> started,
            TaskCompletionSource<bool> canComplete)
        {
            _started = started;
            _canComplete = canComplete;
        }

        public async Task Handle(TestNotification notification, CancellationToken cancellationToken)
        {
            _started.SetResult(true);
            await _canComplete.Task;
        }
    }
}