using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyDispatch.IntegrationTests;

public class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public static List<string> Logs { get; } = [];

	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		var messageName = typeof(TMessage).Name;
		Logs.Add($"[LOG] Executing {messageName}");

		var result = await next();

		Logs.Add($"[LOG] Executed {messageName}");
		return result;
	}
}

public class ValidationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public static List<string> Validations { get; } = [];

	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		Validations.Add($"[VALIDATE] {typeof(TMessage).Name}");

		// Simple validation example
		if (message is CreateUserCommand cmd && string.IsNullOrEmpty(cmd.Name))
		{
			throw new InvalidOperationException("Name is required");
		}

		return await next();
	}
}

public class PerformanceBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public static List<string> Metrics { get; } = [];

	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		var result = await next();
		stopwatch.Stop();

		Metrics.Add($"[PERF] {typeof(TMessage).Name} took {stopwatch.ElapsedMilliseconds}ms");
		return result;
	}
}