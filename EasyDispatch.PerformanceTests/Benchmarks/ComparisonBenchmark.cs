using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Comprehensive comparison benchmark across all major features.
/// Useful for comparing different versions or configurations.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
[MarkdownExporter]
[HtmlExporter]
[CsvExporter]
[JsonExporter]
public class ComparisonBenchmark
{
	private IServiceProvider _serviceProvider = null!;
	private IMediator _mediator = null!;

	[GlobalSetup]
	public void Setup()
	{
		var services = new ServiceCollection();
		services.AddMediator(typeof(ComparisonBenchmark).Assembly)
			.AddOpenBehavior(typeof(SampleBehavior<,>));

		_serviceProvider = services.BuildServiceProvider();
		_mediator = _serviceProvider.GetRequiredService<IMediator>();
	}

	// === QUERIES ===

	[Benchmark]
	[BenchmarkCategory("Queries")]
	public async Task<int> Query_Simple()
	{
		return await _mediator.SendAsync(new CompQuery(42), CancellationToken.None);
	}

	[Benchmark]
	[BenchmarkCategory("Queries")]
	public async Task<ComplexResult> Query_Complex()
	{
		return await _mediator.SendAsync(new ComplexQuery(1, "test", DateTime.Now), CancellationToken.None);
	}

	// === COMMANDS ===

	[Benchmark]
	[BenchmarkCategory("Commands")]
	public async Task Command_Void()
	{
		await _mediator.SendAsync(new CompVoidCommand("data"), CancellationToken.None);
	}

	[Benchmark]
	[BenchmarkCategory("Commands")]
	public async Task<int> Command_WithResponse()
	{
		return await _mediator.SendAsync(new CompCommandWithResponse(100), CancellationToken.None);
	}

	// === NOTIFICATIONS ===

	[Benchmark]
	[BenchmarkCategory("Notifications")]
	public async Task Notification_SingleHandler()
	{
		await _mediator.PublishAsync(new CompNotification("message"), CancellationToken.None);
	}

	[Benchmark]
	[BenchmarkCategory("Notifications")]
	public async Task Notification_MultipleHandlers_Sequential()
	{
		await _mediator.PublishAsync(
			new CompMultiNotification("message"),
			NotificationPublishStrategy.StopOnFirstException,
			CancellationToken.None);
	}

	[Benchmark]
	[BenchmarkCategory("Notifications")]
	public async Task Notification_MultipleHandlers_Parallel()
	{
		await _mediator.PublishAsync(
			new CompMultiNotification("message"),
			NotificationPublishStrategy.ParallelWhenAll,
			CancellationToken.None);
	}

	// === STREAMING ===

	[Benchmark]
	[BenchmarkCategory("Streaming")]
	public async Task<int> StreamQuery_100Items()
	{
		int count = 0;
		await foreach (var item in _mediator.StreamAsync(new CompStreamQuery(100), CancellationToken.None))
		{
			count++;
		}
		return count;
	}

	[Benchmark]
	[BenchmarkCategory("Streaming")]
	public async Task<int> StreamQuery_1000Items()
	{
		int count = 0;
		await foreach (var item in _mediator.StreamAsync(new CompStreamQuery(1000), CancellationToken.None))
		{
			count++;
		}
		return count;
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_serviceProvider as IDisposable)?.Dispose();
	}
}

// Test messages
public record CompQuery(int Value) : IQuery<int>;
public record ComplexQuery(int Id, string Name, DateTime Date) : IQuery<ComplexResult>;
public record ComplexResult(int Id, string Name, DateTime Date, string ComputedValue);
public record CompVoidCommand(string Data) : ICommand;
public record CompCommandWithResponse(int Value) : ICommand<int>;
public record CompNotification(string Message) : INotification;
public record CompMultiNotification(string Message) : INotification;
public record CompStreamQuery(int Count) : IStreamQuery<int>;

// Handlers
public class CompQueryHandler : IQueryHandler<CompQuery, int>
{
	public Task<int> Handle(CompQuery query, CancellationToken ct)
	{
		return Task.FromResult(query.Value * 2);
	}
}

public class ComplexQueryHandler : IQueryHandler<ComplexQuery, ComplexResult>
{
	public Task<ComplexResult> Handle(ComplexQuery query, CancellationToken ct)
	{
		return Task.FromResult(new ComplexResult(
			query.Id,
			query.Name,
			query.Date,
			$"{query.Name}-{query.Date:yyyyMMdd}"));
	}
}

public class CompVoidCommandHandler : ICommandHandler<CompVoidCommand>
{
	public Task Handle(CompVoidCommand command, CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}

public class CompCommandWithResponseHandler : ICommandHandler<CompCommandWithResponse, int>
{
	public Task<int> Handle(CompCommandWithResponse command, CancellationToken ct)
	{
		return Task.FromResult(command.Value * 3);
	}
}

public class CompNotificationHandler : INotificationHandler<CompNotification>
{
	public Task Handle(CompNotification notification, CancellationToken ct)
	{
		return Task.CompletedTask;
	}
}

public class CompMultiNotificationHandler1 : INotificationHandler<CompMultiNotification>
{
	public async Task Handle(CompMultiNotification notification, CancellationToken ct)
	{
		await Task.Delay(1, ct);
	}
}

public class CompMultiNotificationHandler2 : INotificationHandler<CompMultiNotification>
{
	public async Task Handle(CompMultiNotification notification, CancellationToken ct)
	{
		await Task.Delay(1, ct);
	}
}

public class CompMultiNotificationHandler3 : INotificationHandler<CompMultiNotification>
{
	public async Task Handle(CompMultiNotification notification, CancellationToken ct)
	{
		await Task.Delay(1, ct);
	}
}

public class CompStreamQueryHandler : IStreamQueryHandler<CompStreamQuery, int>
{
	public async IAsyncEnumerable<int> Handle(
		CompStreamQuery query,
		[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
	{
		for (int i = 0; i < query.Count; i++)
		{
			await Task.Yield();
			yield return i;
		}
	}
}

// Sample behavior for testing
public class SampleBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken ct)
	{
		return await next();
	}
}