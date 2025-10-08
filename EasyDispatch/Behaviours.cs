namespace EasyDispatch;

/// <summary>
/// Pipeline behavior (middleware) that wraps handler execution.
/// Behaviors execute in registration order, forming a chain around the handler.
/// </summary>
/// <typeparam name="TMessage">The message type (query, command, or notification)</typeparam>
/// <typeparam name="TResponse">The response type</typeparam>
public interface IPipelineBehavior<in TMessage, TResponse>
{
	/// <summary>
	/// Handles the message and invokes the next behavior in the pipeline.
	/// </summary>
	/// <param name="message">The message being handled</param>
	/// <param name="next">Delegate to invoke the next behavior or the handler</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The response from the handler</returns>
	Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken);
}

/// <summary>
/// Pipeline behavior (middleware) that wraps streaming query handler execution.
/// Stream behaviors can transform, filter, or observe the stream of results.
/// </summary>
/// <typeparam name="TQuery">The streaming query type</typeparam>
/// <typeparam name="TResult">The type of each result item in the stream</typeparam>
public interface IStreamPipelineBehavior<in TQuery, TResult>
{
	/// <summary>
	/// Handles the streaming query and invokes the next behavior in the pipeline.
	/// </summary>
	/// <param name="query">The streaming query being handled</param>
	/// <param name="next">Delegate to invoke the next behavior or the handler</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>An async enumerable stream of results</returns>
	IAsyncEnumerable<TResult> Handle(
		TQuery query,
		Func<IAsyncEnumerable<TResult>> next,
		CancellationToken cancellationToken);
}