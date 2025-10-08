using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.PerformanceTests;

/// <summary>
/// Benchmarks for basic handler execution performance.
/// Tests query, command, and direct handler invocation.
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class HandlerExecutionBenchmarks
{
	private IServiceProvider _serviceProvider = null!;
	private IMediator _mediator = null!;
	private SimpleQueryHandler _directHandler = null!;
	private SimpleQuery _query = null!;
	private SimpleCommand _command = null!;
	private SimpleCommandWithResponse _commandWithResponse = null!;

	[GlobalSetup]
	public void Setup()
	{
		var services = new ServiceCollection();
		services.AddMediator(typeof(HandlerExecutionBenchmarks).Assembly);

		_serviceProvider = services.BuildServiceProvider();
		_mediator = _serviceProvider.GetRequiredService<IMediator>();
		_directHandler = new SimpleQueryHandler();

		_query = new SimpleQuery(42);
		_command = new SimpleCommand("test");
		_commandWithResponse = new SimpleCommandWithResponse(123);
	}

	[Benchmark(Baseline = true)]
	public async Task<int> DirectHandlerInvocation()
	{
		return await _directHandler.Handle(_query, CancellationToken.None);
	}

	[Benchmark]
	public async Task<int> Query_ThroughMediator()
	{
		return await _mediator.SendAsync(_query, CancellationToken.None);
	}

	[Benchmark]
	public async Task VoidCommand_ThroughMediator()
	{
		await _mediator.SendAsync(_command, CancellationToken.None);
	}

	[Benchmark]
	public async Task<int> CommandWithResponse_ThroughMediator()
	{
		return await _mediator.SendAsync(_commandWithResponse, CancellationToken.None);
	}

	[GlobalCleanup]
	public void Cleanup()
	{
		(_serviceProvider as IDisposable)?.Dispose();
	}
}

// Test messages and handlers
public record SimpleQuery(int Value) : IQuery<int>;
public record SimpleCommand(string Data) : ICommand;
public record SimpleCommandWithResponse(int Value) : ICommand<int>;

public class SimpleQueryHandler : IQueryHandler<SimpleQuery, int>
{
	public Task<int> Handle(SimpleQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(query.Value * 2);
	}
}

public class SimpleCommandHandler : ICommandHandler<SimpleCommand>
{
	public Task Handle(SimpleCommand command, CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}
}

public class SimpleCommandWithResponseHandler : ICommandHandler<SimpleCommandWithResponse, int>
{
	public Task<int> Handle(SimpleCommandWithResponse command, CancellationToken cancellationToken)
	{
		return Task.FromResult(command.Value * 3);
	}
}