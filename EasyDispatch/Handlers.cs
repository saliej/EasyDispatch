using EasyDispatch;

namespace EasyDispatch;

/// <summary>
/// Handler for queries that return a response.
/// Only one handler should exist per query type.
/// </summary>
/// <typeparam name="TQuery">The query type to handle</typeparam>
/// <typeparam name="TResponse">The response type returned by the query</typeparam>
public interface IQueryHandler<in TQuery, TResponse>
	where TQuery : IQuery<TResponse>
{
	/// <summary>
	/// Handles the query and returns a response.
	/// </summary>
	/// <param name="query">The query to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The query response</returns>
	Task<TResponse> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Handler for streaming queries that return results incrementally.
/// Only one handler should exist per streaming query type.
/// </summary>
/// <typeparam name="TQuery">The streaming query type to handle</typeparam>
/// <typeparam name="TResult">The type of each result item in the stream</typeparam>
public interface IStreamQueryHandler<in TQuery, TResult>
	where TQuery : IStreamQuery<TResult>
{
	/// <summary>
	/// Handles the streaming query and returns results incrementally.
	/// </summary>
	/// <param name="query">The streaming query to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>An async enumerable stream of results</returns>
	IAsyncEnumerable<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Handler for commands that do not return a value.
/// Only one handler should exist per command type.
/// </summary>
/// <typeparam name="TCommand">The command type to handle</typeparam>
public interface ICommandHandler<in TCommand>
	where TCommand : ICommand
{
	/// <summary>
	/// Handles the command.
	/// </summary>
	/// <param name="command">The command to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handler for commands that return a response.
/// Only one handler should exist per command type.
/// </summary>
/// <typeparam name="TCommand">The command type to handle</typeparam>
/// <typeparam name="TResponse">The response type returned by the command</typeparam>
public interface ICommandHandler<in TCommand, TResponse>
	where TCommand : ICommand<TResponse>
{
	/// <summary>
	/// Handles the command and returns a response.
	/// </summary>
	/// <param name="command">The command to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The command response</returns>
	Task<TResponse> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handler for notifications (events).
/// Multiple handlers can exist for the same notification type.
/// </summary>
/// <typeparam name="TNotification">The notification type to handle</typeparam>
public interface INotificationHandler<in TNotification>
	where TNotification : INotification
{
	/// <summary>
	/// Handles the notification.
	/// </summary>
	/// <param name="notification">The notification to handle</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task Handle(TNotification notification, CancellationToken cancellationToken);
}