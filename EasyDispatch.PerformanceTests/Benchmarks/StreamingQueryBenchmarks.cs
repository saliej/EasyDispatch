using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for streaming query performance.
/// Compares streaming vs loading all data at once.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class StreamingQueryBenchmarks
{
	private IServiceProvider _serviceProvider = null!;
	private IMediator _mediator = null!;

	[Params(100, 1000, 10000)]
	public int ItemCount { get; set; }

	[GlobalSetup]
	public void Setup()
	{
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingQueryBenchmarks).Assembly);

		_serviceProvider = services.BuildServiceProvider();
		_mediator = _serviceProvider.GetRequiredService<IMediator>();
	}

	[Benchmark(Baseline = true)]
	public async Task<int> Query_LoadAllIntoList()
	{
		var query = new GetAllItemsQuery(ItemCount);
		var items = await _mediator.SendAsync(query, CancellationToken.None);
		return items.Count;
	}

	[Benchmark]
	public async Task<int> StreamQuery_ConsumeAll()
	{
		var query = new GetItemsStreamQuery(ItemCount);
		int count = 0;

		await foreach (var item in _mediator.StreamAsync(query, CancellationToken.None))
		{
			count++;
		}

		return count;
	}

	[Benchmark]
	public async Task<int> StreamQuery_TakeFirst100()
	{
		var query = new GetItemsStreamQuery(ItemCount);
		int count = 0;

		await foreach (var item in _mediator.StreamAsync(query, CancellationToken.None))
		{
			count++;
			if (count >= 100) break;
		}

		return count;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_serviceProvider as IDisposable)?.Dispose();
	}
}

// Test messages and handlers
public record DataItem(int Id, string Name);

public record GetAllItemsQuery(int Count) : IQuery<List<DataItem>>;

public class GetAllItemsQueryHandler : IQueryHandler<GetAllItemsQuery, List<DataItem>>
{
	public Task<List<DataItem>> Handle(GetAllItemsQuery query, CancellationToken cancellationToken)
	{
		var items = new List<DataItem>(query.Count);
		for (int i = 0; i < query.Count; i++)
		{
			items.Add(new DataItem(i, $"Item {i}"));
		}
		return Task.FromResult(items);
	}
}

public record GetItemsStreamQuery(int Count) : IStreamQuery<DataItem>;

public class GetItemsStreamQueryHandler : IStreamQueryHandler<GetItemsStreamQuery, DataItem>
{
	public async IAsyncEnumerable<DataItem> Handle(
		GetItemsStreamQuery query,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		for (int i = 0; i < query.Count; i++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await Task.Yield(); // Simulate async work
			yield return new DataItem(i, $"Item {i}");
		}
	}
}