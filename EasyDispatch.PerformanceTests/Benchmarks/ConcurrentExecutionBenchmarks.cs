using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for measuring concurrent execution performance.
/// Tests thread safety and scalability under load.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ConcurrentExecutionBenchmarks
{
	private IServiceProvider _serviceProvider = null!;
	private IMediator _mediator = null!;

	[Params(1, 10, 100, 1000)]
	public int ConcurrentRequests { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		var services = new ServiceCollection();
		services.AddMediator(typeof(ConcurrentExecutionBenchmarks).Assembly);

		_serviceProvider = services.BuildServiceProvider();
		_mediator = _serviceProvider.GetRequiredService<IMediator>();
	}

	[Benchmark]
	public async Task Queries_Sequential()
	{
		for (int i = 0; i < ConcurrentRequests; i++)
		{
			await _mediator.SendAsync(new ConcurrentQuery(i), CancellationToken.None);
		}
	}

	[Benchmark]
	public async Task Queries_Parallel()
	{
		var tasks = new Task<int>[ConcurrentRequests];

		for (int i = 0; i < ConcurrentRequests; i++)
		{
			tasks[i] = _mediator.SendAsync(new ConcurrentQuery(i), CancellationToken.None);
		}

		await Task.WhenAll(tasks);
	}

	[Benchmark]
	public async Task Commands_Sequential()
	{
		for (int i = 0; i < ConcurrentRequests; i++)
		{
			await _mediator.SendAsync(new ConcurrentCommand(i), CancellationToken.None);
		}
	}

	[Benchmark]
	public async Task Commands_Parallel()
	{
		var tasks = new Task[ConcurrentRequests];

		for (int i = 0; i < ConcurrentRequests; i++)
		{
			tasks[i] = _mediator.SendAsync(new ConcurrentCommand(i), CancellationToken.None);
		}

		await Task.WhenAll(tasks);
	}

	[Benchmark]
	public async Task Notifications_Parallel()
	{
		var tasks = new Task[ConcurrentRequests];

		for (int i = 0; i < ConcurrentRequests; i++)
		{
			tasks[i] = _mediator.PublishAsync(
				new ConcurrentNotification(i),
				NotificationPublishStrategy.ParallelWhenAll,
				CancellationToken.None);
		}

		await Task.WhenAll(tasks);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_serviceProvider as IDisposable)?.Dispose();
	}
}

// Test messages and handlers
public record ConcurrentQuery(int Id) : IQuery<int>;
public record ConcurrentCommand(int Id) : ICommand;
public record ConcurrentNotification(int Id) : INotification;

public class ConcurrentQueryHandler : IQueryHandler<ConcurrentQuery, int>
{
	public async Task<int> Handle(ConcurrentQuery query, CancellationToken cancellationToken)
	{
		// Simulate some async work
		await Task.Yield();
		return query.Id * 2;
	}
}

public class ConcurrentCommandHandler : ICommandHandler<ConcurrentCommand>
{
	public async Task Handle(ConcurrentCommand command, CancellationToken cancellationToken)
	{
		// Simulate some async work
		await Task.Yield();
	}
}

public class ConcurrentNotificationHandler1 : INotificationHandler<ConcurrentNotification>
{
	public async Task Handle(ConcurrentNotification notification, CancellationToken cancellationToken)
	{
		await Task.Yield();
	}
}

public class ConcurrentNotificationHandler2 : INotificationHandler<ConcurrentNotification>
{
	public async Task Handle(ConcurrentNotification notification, CancellationToken cancellationToken)
	{
		await Task.Yield();
	}
}