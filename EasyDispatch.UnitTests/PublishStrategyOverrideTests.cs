using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for per-call notification publish strategy override.
/// </summary>
public class PublishStrategyOverrideTests
{
	private record TestNotification(string Message) : INotification;

	private class SuccessfulHandler : INotificationHandler<TestNotification>
	{
		public List<string> ExecutionLog { get; } = [];

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
	public async Task PublishAsync_WithStrategyOverride_UsesOverrideInsteadOfConfigured()
	{
		// Arrange - Configure with StopOnFirstException
		var handler1 = new SuccessfulHandler();
		var handler2 = new FailingHandler();
		var handler3 = new SuccessfulHandler();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler3);

		var provider = services.BuildServiceProvider();

		// Act - Override with ContinueOnException
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var notification = new TestNotification("Test");

		var act = async () => await mediator.PublishAsync(
			notification,
			NotificationPublishStrategy.ContinueOnException);

		// Assert - Should continue despite failure (override behavior)
		var exception = await act.Should().ThrowAsync<AggregateException>();
		exception.Which.InnerExceptions.Should().ContainSingle();

		// All handlers should execute (ContinueOnException behavior)
		handler1.ExecutionLog.Should().ContainSingle();
		handler2.WasExecuted.Should().BeTrue();
		handler3.ExecutionLog.Should().ContainSingle();
	}

	[Fact]
	public async Task PublishAsync_WithoutStrategyOverride_UsesConfiguredStrategy()
	{
		// Arrange - Configure with StopOnFirstException
		var handler1 = new SuccessfulHandler();
		var handler2 = new FailingHandler();
		var handler3 = new SuccessfulHandler();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler3);

		var provider = services.BuildServiceProvider();

		// Act - Use default (no override)
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var notification = new TestNotification("Test");

		var act = async () => await mediator.PublishAsync(notification);

		// Assert - Should stop on first exception (configured behavior)
		await act.Should().ThrowAsync<InvalidOperationException>();

		// Only first two handlers should execute (StopOnFirstException behavior)
		handler1.ExecutionLog.Should().ContainSingle();
		handler2.WasExecuted.Should().BeTrue();
		handler3.ExecutionLog.Should().BeEmpty(); // Should NOT execute
	}

	[Fact]
	public async Task PublishAsync_OverrideToStopOnFirstException_StopsImmediately()
	{
		// Arrange - Configure with ContinueOnException
		var handler1 = new SuccessfulHandler();
		var handler2 = new FailingHandler();
		var handler3 = new SuccessfulHandler();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler2);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler3);

		var provider = services.BuildServiceProvider();

		// Act - Override with StopOnFirstException
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var notification = new TestNotification("Test");

		var act = async () => await mediator.PublishAsync(
			notification,
			NotificationPublishStrategy.StopOnFirstException);

		// Assert - Should stop immediately (override behavior)
		await act.Should().ThrowAsync<InvalidOperationException>();

		handler1.ExecutionLog.Should().ContainSingle();
		handler2.WasExecuted.Should().BeTrue();
		handler3.ExecutionLog.Should().BeEmpty(); // Should NOT execute
	}

	[Fact]
	public async Task PublishAsync_OverrideToParallelWhenAll_ExecutesInParallel()
	{
		// Arrange - Configure with StopOnFirstException (sequential)
		var executionTimes = new List<DateTime>();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
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

		// Act - Override with ParallelWhenAll
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var notification = new TestNotification("Test");

		var startTime = DateTime.UtcNow;
		await mediator.PublishAsync(notification, NotificationPublishStrategy.ParallelWhenAll);
		var totalTime = DateTime.UtcNow - startTime;

		// Assert - Should complete in ~50ms (parallel), not 150ms (sequential)
		totalTime.Should().BeLessThan(TimeSpan.FromMilliseconds(120));
		executionTimes.Should().HaveCount(3);
	}

	[Fact]
	public async Task PublishAsync_OverrideToParallelNoWait_ReturnsImmediately()
	{
		// Arrange - Configure with StopOnFirstException (waits for completion)
		var handler1Started = new TaskCompletionSource<bool>();
		var handler1CanComplete = new TaskCompletionSource<bool>();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		services.AddSingleton<INotificationHandler<TestNotification>>(
			new LongRunningHandler(handler1Started, handler1CanComplete));

		var provider = services.BuildServiceProvider();

		// Act - Override with ParallelNoWait
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
		var notification = new TestNotification("Test");

		var publishTask = mediator.PublishAsync(
			notification,
			NotificationPublishStrategy.ParallelNoWait);

		// Assert - Should return immediately
		publishTask.IsCompleted.Should().BeTrue();
		await publishTask;

		// Give handler time to start
		await Task.Delay(50);

		// Signal handler can complete
		handler1CanComplete.SetResult(true);

		// Wait for handler to start
		await handler1Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public async Task PublishAsync_MultipleCallsWithDifferentStrategies_EachUsesCorrectStrategy()
	{
		// Arrange
		var handler1 = new SuccessfulHandler();
		var handler2 = new FailingHandler();

		var services = new ServiceCollection();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PublishStrategyOverrideTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		services.AddSingleton<INotificationHandler<TestNotification>>(handler1);
		services.AddSingleton<INotificationHandler<TestNotification>>(handler2);

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act & Assert - First call with StopOnFirstException (should throw immediately)
		var notification1 = new TestNotification("Test1");
		var act1 = async () => await mediator.PublishAsync(
			notification1,
			NotificationPublishStrategy.StopOnFirstException);
		await act1.Should().ThrowAsync<InvalidOperationException>();

		// Reset
		handler1.ExecutionLog.Clear();
		handler2 = new FailingHandler();
		services.AddSingleton<INotificationHandler<TestNotification>>(handler2);

		// Act & Assert - Second call with ContinueOnException (should collect exceptions)
		var notification2 = new TestNotification("Test2");
		var act2 = async () => await mediator.PublishAsync(
			notification2,
			NotificationPublishStrategy.ContinueOnException);
		await act2.Should().ThrowAsync<AggregateException>();
	}

	[Fact]
	public async Task PublishAsync_WithNullNotification_ThrowsArgumentNullException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(PublishStrategyOverrideTests).Assembly);

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act & Assert
		var act = async () => await mediator.PublishAsync(
			null!,
			NotificationPublishStrategy.StopOnFirstException);

		await act.Should().ThrowAsync<ArgumentNullException>()
			.WithParameterName("notification");
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