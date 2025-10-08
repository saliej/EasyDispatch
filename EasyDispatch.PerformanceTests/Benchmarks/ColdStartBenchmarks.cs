using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for measuring cold start and registration performance.
/// Tests handler registration, first execution (with reflection caching), and warm execution.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class ColdStartBenchmarks
{
	[Params(10, 50, 100)]
	public int HandlerCount { get; set; }

	[Benchmark]
	public void Registration_WithAssemblyScanning()
	{
		var services = new ServiceCollection();
		services.AddMediator(typeof(ColdStartBenchmarks).Assembly);
		var provider = services.BuildServiceProvider();
		(provider as IDisposable)?.Dispose();
	}

	[Benchmark]
	public void Registration_WithStartupValidation()
	{
		var services = new ServiceCollection();
		services.AddMediator(options =>
		{
			options.Assemblies = new[] { typeof(ColdStartBenchmarks).Assembly };
			options.StartupValidation = StartupValidation.Warn;
		});
		var provider = services.BuildServiceProvider();
		(provider as IDisposable)?.Dispose();
	}

	[Benchmark]
	public async Task<int> FirstExecution_ColdCache()
	{
		// Fresh service provider = cold reflection cache
		var services = new ServiceCollection();
		services.AddMediator(typeof(ColdStartBenchmarks).Assembly);
		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var result = await mediator.SendAsync(new ColdStartQuery(42), CancellationToken.None);

		(provider as IDisposable)?.Dispose();
		return result;
	}

	[Benchmark]
	public async Task<int> SecondExecution_WarmCache()
	{
		// Reuse service provider = warm reflection cache
		var services = new ServiceCollection();
		services.AddMediator(typeof(ColdStartBenchmarks).Assembly);
		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// First call warms cache
		await mediator.SendAsync(new ColdStartQuery(42), CancellationToken.None);

		// Second call uses cache
		var result = await mediator.SendAsync(new ColdStartQuery(42), CancellationToken.None);

		(provider as IDisposable)?.Dispose();
		return result;
	}
}

// Test messages and handlers
public record ColdStartQuery(int Value) : IQuery<int>;

public class ColdStartQueryHandler : IQueryHandler<ColdStartQuery, int>
{
	public Task<int> Handle(ColdStartQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(query.Value * 2);
	}
}