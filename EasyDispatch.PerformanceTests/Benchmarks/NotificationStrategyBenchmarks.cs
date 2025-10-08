using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for notification publishing with different strategies.
/// Compares sequential vs parallel execution with multiple handlers.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class NotificationStrategyBenchmarks
{
	private IMediator _mediator = null!;
	private IServiceProvider _serviceProvider = null!;
	private TestNotification _notification = null!;

	[Params(1, 3, 5, 10)]
	public int HandlerCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		var services = new ServiceCollection();

		// Register mediator
		services.AddMediator(options =>
		{
			options.Assemblies = new[] { typeof(NotificationStrategyBenchmarks).Assembly };
		});

		// Register multiple handlers dynamically
		for (int i = 0; i < HandlerCount; i++)
		{
			services.AddScoped<INotificationHandler<TestNotification>, TestNotificationHandler>();
		}

		_serviceProvider = services.BuildServiceProvider();
		_mediator = _serviceProvider.GetRequiredService<IMediator>();
		_notification = new TestNotification($"Message {HandlerCount}");
	}

	[Benchmark(Baseline = true)]
	public async Task Sequential_StopOnFirstException()
	{
		await _mediator.PublishAsync(
			_notification,
			NotificationPublishStrategy.StopOnFirstException,
			CancellationToken.None);
	}

	[Benchmark]
	public async Task Sequential_ContinueOnException()
	{
		await _mediator.PublishAsync(
			_notification,
			NotificationPublishStrategy.ContinueOnException,
			CancellationToken.None);
	}

	[Benchmark]
	public async Task Parallel_WhenAll()
	{
		await _mediator.PublishAsync(
			_notification,
			NotificationPublishStrategy.ParallelWhenAll,
			CancellationToken.None);
	}

	[Benchmark]
	public async Task Parallel_NoWait()
	{
		await _mediator.PublishAsync(
			_notification,
			NotificationPublishStrategy.ParallelNoWait,
			CancellationToken.None);

		// Small delay to allow fire-and-forget to complete
		await Task.Delay(1);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_serviceProvider as IDisposable)?.Dispose();
	}
}

// Test messages and handlers
public record TestNotification(string Message) : INotification;

public class TestNotificationHandler : INotificationHandler<TestNotification>
{
	public async Task Handle(TestNotification notification, CancellationToken cancellationToken)
	{
		// Simulate some work
		await Task.Delay(1, cancellationToken);
	}
}