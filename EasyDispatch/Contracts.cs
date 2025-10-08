using System;

namespace EasyDispatch;

/// <summary>
/// Marker interface for queries that return a response.
/// Queries should be side-effect free and only fetch data.
/// </summary>
/// <typeparam name="TResponse">The type of response the query will return</typeparam>
public interface IQuery<out TResponse>
{
}

/// <summary>
/// Marker interface for streaming queries that return results incrementally.
/// Streaming queries are ideal for large datasets that should not be loaded into memory at once.
/// </summary>
/// <typeparam name="TResult">The type of each result item in the stream</typeparam>
public interface IStreamQuery<out TResult>
{
}

/// <summary>
/// Marker interface for commands that do not return a value (void).
/// Commands represent operations that cause side effects or state changes.
/// </summary>
public interface ICommand
{
}

/// <summary>
/// Marker interface for commands that return a response.
/// Commands represent operations that cause side effects and return a result.
/// </summary>
/// <typeparam name="TResponse">The type of response the command will return</typeparam>
public interface ICommand<out TResponse>
{
}

/// <summary>
/// Marker interface for notifications (events) that can be handled by multiple handlers.
/// Notifications are fire-and-forget messages for pub/sub scenarios.
/// </summary>
public interface INotification
{
}