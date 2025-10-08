using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for measuring pipeline behavior overhead.
/// Compares handler execution with 0, 1, 3, and 5 behaviors.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class BehaviorOverheadBenchmarks
{
	private IServiceProvider _noBehaviors = null!;
	private IServiceProvider _oneBehavior = null!;
	private IServiceProvider _threeBehaviors = null!;
	private IServiceProvider _fiveBehaviors = null!;

	private IMediator _mediatorNoBehaviors = null!;
	private IMediator _mediatorOneBehavior = null!;
	private IMediator _mediatorThreeBehaviors = null!;
	private IMediator _mediatorFiveBehaviors = null!;

	private BehaviorTestQuery _query = null!;

	[GlobalSetup]
	public void Setup()
	{
		_query = new BehaviorTestQuery(100);

		// No behaviors
		var servicesNone = new ServiceCollection();
		servicesNone.AddMediator(typeof(BehaviorOverheadBenchmarks).Assembly);
		_noBehaviors = servicesNone.BuildServiceProvider();
		_mediatorNoBehaviors = _noBehaviors.GetRequiredService<IMediator>();

		// One behavior
		var servicesOne = new ServiceCollection();
		servicesOne.AddMediator(typeof(BehaviorOverheadBenchmarks).Assembly)
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior1>();
		_oneBehavior = servicesOne.BuildServiceProvider();
		_mediatorOneBehavior = _oneBehavior.GetRequiredService<IMediator>();

		// Three behaviors
		var servicesThree = new ServiceCollection();
		servicesThree.AddMediator(typeof(BehaviorOverheadBenchmarks).Assembly)
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior1>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior2>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior3>();
		_threeBehaviors = servicesThree.BuildServiceProvider();
		_mediatorThreeBehaviors = _threeBehaviors.GetRequiredService<IMediator>();

		// Five behaviors
		var servicesFive = new ServiceCollection();
		servicesFive.AddMediator(typeof(BehaviorOverheadBenchmarks).Assembly)
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior1>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior2>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior3>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior4>()
			.AddBehavior<BehaviorTestQuery, int, EmptyBehavior5>();
		_fiveBehaviors = servicesFive.BuildServiceProvider();
		_mediatorFiveBehaviors = _fiveBehaviors.GetRequiredService<IMediator>();
	}

	[Benchmark(Baseline = true)]
	public async Task<int> NoBehaviors()
	{
		return await _mediatorNoBehaviors.SendAsync(_query, CancellationToken.None);
	}

	[Benchmark]
	public async Task<int> OneBehavior()
	{
		return await _mediatorOneBehavior.SendAsync(_query, CancellationToken.None);
	}

	[Benchmark]
	public async Task<int> ThreeBehaviors()
	{
		return await _mediatorThreeBehaviors.SendAsync(_query, CancellationToken.None);
	}

	[Benchmark]
	public async Task<int> FiveBehaviors()
	{
		return await _mediatorFiveBehaviors.SendAsync(_query, CancellationToken.None);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_noBehaviors as IDisposable)?.Dispose();
		(_oneBehavior as IDisposable)?.Dispose();
		(_threeBehaviors as IDisposable)?.Dispose();
		(_fiveBehaviors as IDisposable)?.Dispose();
	}
}

// Test messages and handlers
public record BehaviorTestQuery(int Value) : IQuery<int>;

public class BehaviorTestQueryHandler : IQueryHandler<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(query.Value * 2);
	}
}

// Empty behaviors (minimal overhead)
public class EmptyBehavior1 : IPipelineBehavior<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery message, Func<Task<int>> next, CancellationToken cancellationToken)
	{
		return next();
	}
}

public class EmptyBehavior2 : IPipelineBehavior<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery message, Func<Task<int>> next, CancellationToken cancellationToken)
	{
		return next();
	}
}

public class EmptyBehavior3 : IPipelineBehavior<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery message, Func<Task<int>> next, CancellationToken cancellationToken)
	{
		return next();
	}
}

public class EmptyBehavior4 : IPipelineBehavior<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery message, Func<Task<int>> next, CancellationToken cancellationToken)
	{
		return next();
	}
}

public class EmptyBehavior5 : IPipelineBehavior<BehaviorTestQuery, int>
{
	public Task<int> Handle(BehaviorTestQuery message, Func<Task<int>> next, CancellationToken cancellationToken)
	{
		return next();
	}
}