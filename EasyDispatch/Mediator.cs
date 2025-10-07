using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyDispatch;

/// <summary>
/// Mediator interface for sending queries, commands, and publishing notifications.
/// </summary>
public interface IMediator
{
	/// <summary>
	/// Sends a query and returns its response.
	/// </summary>
	/// <typeparam name="TResponse">The type of response expected</typeparam>
	/// <param name="query">The query to send</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The query response</returns>
	Task<TResponse> SendAsync<TResponse>(
		IQuery<TResponse> query,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command that does not return a value.
	/// </summary>
	/// <param name="command">The command to send</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task SendAsync(
		ICommand command,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a command and returns its response.
	/// </summary>
	/// <typeparam name="TResponse">The type of response expected</typeparam>
	/// <param name="command">The command to send</param>
	/// <param name="cancellationToken">Cancellation token</param>
	/// <returns>The command response</returns>
	Task<TResponse> SendAsync<TResponse>(
		ICommand<TResponse> command,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Publishes a notification to all registered handlers.
	/// </summary>
	/// <param name="notification">The notification to publish</param>
	/// <param name="cancellationToken">Cancellation token</param>
	Task PublishAsync(
		INotification notification,
		CancellationToken cancellationToken = default);
}


/// <summary>
/// Default implementation of IMediator that resolves handlers from the DI container
/// and executes pipeline behaviors with performance optimizations.
/// </summary>
public sealed class Mediator : IMediator
{
	private readonly IServiceProvider _serviceProvider;
	private readonly MediatorOptions _options;
	private readonly ILogger<Mediator>? _logger;

	// Performance: Cache reflection lookups to avoid repeated GetMethod calls
	private static readonly ConcurrentDictionary<Type, MethodInfo> _handleMethodCache = new();
	private static readonly ConcurrentDictionary<Type, Type> _handlerTypeCache = new();
	private static readonly ConcurrentDictionary<Type, Type> _behaviorTypeCache = new();

	public Mediator(
		IServiceProvider serviceProvider,
		ILogger<Mediator>? logger = null)
		: this(serviceProvider, null, logger)
	{
	}

	public Mediator(
		IServiceProvider serviceProvider,
		MediatorOptions? options,
		ILogger<Mediator>? logger = null)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_options = options ?? new MediatorOptions(); // Use defaults if not provided
		_logger = logger;
	}

	/// <summary>
	/// Sends a query and returns its response.
	/// </summary>
	public Task<TResponse> SendAsync<TResponse>(
		IQuery<TResponse> query,
		CancellationToken cancellationToken = default)
	{
		if (query == null)
			throw new ArgumentNullException(nameof(query));

		return SendQueryInternal(query, cancellationToken);
	}

	private async Task<TResponse> SendQueryInternal<TResponse>(
		IQuery<TResponse> query,
		CancellationToken cancellationToken)
	{
		var queryType = query.GetType();

		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			queryType.FullName ?? queryType.Name,
			ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", queryType.FullName);
		activity?.SetTag("easydispatch.response_type", typeof(TResponse).FullName);
		activity?.SetTag("easydispatch.operation", "Query");

		try
		{
			// Use cached handler type
			var handlerType = _handlerTypeCache.GetOrAdd(
				queryType,
				qt => typeof(IQueryHandler<,>).MakeGenericType(qt, typeof(TResponse)));

			var handler = _serviceProvider.GetService(handlerType);

			if (handler == null)
			{
				activity?.SetStatus(ActivityStatusCode.Error, "Handler not found");
				throw new InvalidOperationException(
					$"No handler registered for query '{queryType.Name}'.\n" +
					$"Expected a handler implementing IQueryHandler<{queryType.Name}, {typeof(TResponse).Name}>.\n" +
					$"Did you forget to call AddMediator() with the assembly containing your handlers?");
			}

			// Set handler type from actual instance
			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			// Get cached Handle method
			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			// Get behaviors
			var behaviorType = _behaviorTypeCache.GetOrAdd(
				queryType,
				qt => typeof(IPipelineBehavior<,>).MakeGenericType(qt, typeof(TResponse)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			// Build the handler execution
			Func<Task<TResponse>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task<TResponse>)handleMethod.Invoke(handler, new object[] { query, cancellationToken })!;
					return await task;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

			// Chain behaviors
			handlerFunc = ChainBehaviors(behaviors, query, handlerFunc, cancellationToken);

			var result = await handlerFunc();
			activity?.SetStatus(ActivityStatusCode.Ok);
			return result;
		}
		catch (Exception ex)
		{
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}
	}

	/// <summary>
	/// Sends a command that does not return a value.
	/// </summary>
	public Task SendAsync(
		ICommand command,
		CancellationToken cancellationToken = default)
	{
		if (command == null)
			throw new ArgumentNullException(nameof(command));

		return SendCommandInternal(command, cancellationToken);
	}

	private async Task SendCommandInternal(
		ICommand command,
		CancellationToken cancellationToken)
	{
		var commandType = command.GetType();

		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			commandType.FullName ?? commandType.Name,
			ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", commandType.FullName);
		activity?.SetTag("easydispatch.response_type", "void");
		activity?.SetTag("easydispatch.operation", "Command");

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(
				commandType,
				ct => typeof(ICommandHandler<>).MakeGenericType(ct));

			var handler = _serviceProvider.GetService(handlerType);

			if (handler == null)
			{
				activity?.SetStatus(ActivityStatusCode.Error, "Handler not found");
				throw new InvalidOperationException(
					$"No handler registered for command '{commandType.Name}'.\n" +
					$"Expected a handler implementing ICommandHandler<{commandType.Name}>.\n" +
					$"Did you forget to call AddMediator() with the assembly containing your handlers?");
			}

			// Set handler type from actual instance
			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			// Get behaviors - use Unit for void commands
			var behaviorType = _behaviorTypeCache.GetOrAdd(
				commandType,
				ct => typeof(IPipelineBehavior<,>).MakeGenericType(ct, typeof(Unit)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			// Build the handler execution
			Func<Task<Unit>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
					await task;
					return Unit.Value;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

			// Chain behaviors
			handlerFunc = ChainBehaviors(behaviors, command, handlerFunc, cancellationToken);

			await handlerFunc();
			activity?.SetStatus(ActivityStatusCode.Ok);
		}
		catch (Exception ex)
		{
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}
	}

	/// <summary>
	/// Sends a command and returns its response.
	/// </summary>
	public Task<TResponse> SendAsync<TResponse>(
		ICommand<TResponse> command,
		CancellationToken cancellationToken = default)
	{
		if (command == null)
			throw new ArgumentNullException(nameof(command));

		return SendCommandWithResponseInternal(command, cancellationToken);
	}

	private async Task<TResponse> SendCommandWithResponseInternal<TResponse>(
		ICommand<TResponse> command,
		CancellationToken cancellationToken)
	{
		var commandType = command.GetType();

		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			commandType.FullName ?? commandType.Name,
			ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", commandType.FullName);
		activity?.SetTag("easydispatch.response_type", typeof(TResponse).FullName);
		activity?.SetTag("easydispatch.operation", "Command");

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(
				commandType,
				ct => typeof(ICommandHandler<,>).MakeGenericType(ct, typeof(TResponse)));

			var handler = _serviceProvider.GetService(handlerType);

			if (handler == null)
			{
				activity?.SetStatus(ActivityStatusCode.Error, "Handler not found");
				throw new InvalidOperationException(
					$"No handler registered for command '{commandType.Name}'.\n" +
					$"Expected a handler implementing ICommandHandler<{commandType.Name}, {typeof(TResponse).Name}>.\n" +
					$"Did you forget to call AddMediator() with the assembly containing your handlers?");
			}

			// Set handler type from actual instance
			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			// Get behaviors
			var behaviorType = _behaviorTypeCache.GetOrAdd(
				commandType,
				ct => typeof(IPipelineBehavior<,>).MakeGenericType(ct, typeof(TResponse)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			// Build the handler execution
			Func<Task<TResponse>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task<TResponse>)handleMethod.Invoke(handler, new object[] { command, cancellationToken })!;
					return await task;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

			// Chain behaviors
			handlerFunc = ChainBehaviors(behaviors, command, handlerFunc, cancellationToken);

			var result = await handlerFunc();
			activity?.SetStatus(ActivityStatusCode.Ok);
			return result;
		}
		catch (Exception ex)
		{
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}
	}

	/// <summary>
	/// Publishes a notification to all registered handlers.
	/// Strategy determined by MediatorOptions.NotificationPublishStrategy.
	/// </summary>
	public async Task PublishAsync(
		INotification notification,
		CancellationToken cancellationToken = default)
	{
		if (notification == null)
			throw new ArgumentNullException(nameof(notification));

		var notificationType = notification.GetType();

		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			notificationType.FullName ?? notificationType.Name,
			ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", notificationType.FullName);
		activity?.SetTag("easydispatch.operation", "Notification");
		activity?.SetTag("easydispatch.publish_strategy", _options.NotificationPublishStrategy.ToString());

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(
				notificationType,
				nt => typeof(INotificationHandler<>).MakeGenericType(nt));

			// Get all handlers for this notification type
			var handlers = _serviceProvider.GetServices(handlerType).ToArray();

			// Set handler count tag before early return
			activity?.SetTag("easydispatch.handler_count", handlers.Length.ToString());

			if (handlers.Length == 0)
			{
				_logger?.LogDebug("No handlers registered for notification {NotificationType}", notificationType.Name);
				activity?.SetStatus(ActivityStatusCode.Ok);
				return;
			}

			_logger?.LogDebug("Publishing notification {NotificationType} to {HandlerCount} handlers",
				notificationType.Name, handlers.Length);

			// Resolve behaviors for notifications (Unit response type)
			var behaviorType = _behaviorTypeCache.GetOrAdd(
				notificationType,
				nt => typeof(IPipelineBehavior<,>).MakeGenericType(nt, typeof(Unit)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length);

			// Execute based on strategy
			await ExecuteNotificationHandlers(
				notification,
				notificationType,
				handlerType,
				handlers,
				behaviors,
				cancellationToken);

			activity?.SetStatus(ActivityStatusCode.Ok);
		}
		catch (Exception ex)
		{
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}
	}

	private async Task ExecuteNotificationHandlers(
		INotification notification,
		Type notificationType,
		Type handlerType,
		object[] handlers,
		object[] behaviors,
		CancellationToken cancellationToken)
	{
		var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
			ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

		switch (_options.NotificationPublishStrategy)
		{
			case NotificationPublishStrategy.StopOnFirstException:
				await PublishSequentialStopOnError(notification, handlers, behaviors, handleMethod, cancellationToken);
				break;

			case NotificationPublishStrategy.ContinueOnException:
				await PublishSequentialContinueOnError(notification, notificationType, handlers, behaviors, handleMethod, cancellationToken);
				break;

			case NotificationPublishStrategy.ParallelWhenAll:
				await PublishParallelWhenAll(notification, notificationType, handlers, behaviors, handleMethod, cancellationToken);
				break;

			case NotificationPublishStrategy.ParallelNoWait:
				_ = PublishParallelNoWait(notification, notificationType, handlers, behaviors, handleMethod, cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(_options.NotificationPublishStrategy));
		}
	}

	private async Task PublishSequentialStopOnError(
		INotification notification,
		object[] handlers,
		object[] behaviors,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		foreach (var handler in handlers)
		{
			var handlerFunc = CreateNotificationHandlerFunc(notification, handler, handleMethod, cancellationToken);
			handlerFunc = ChainBehaviors(behaviors, notification, handlerFunc, cancellationToken);
			await handlerFunc();
		}
	}

	private async Task PublishSequentialContinueOnError(
		INotification notification,
		Type notificationType,
		object[] handlers,
		object[] behaviors,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		var exceptions = new List<Exception>();

		foreach (var handler in handlers)
		{
			try
			{
				var handlerFunc = CreateNotificationHandlerFunc(notification, handler, handleMethod, cancellationToken);
				handlerFunc = ChainBehaviors(behaviors, notification, handlerFunc, cancellationToken);
				await handlerFunc();
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "Handler {HandlerType} failed for notification {NotificationType}",
					handler.GetType().Name, notificationType.Name);
				exceptions.Add(ex);
			}
		}

		if (exceptions.Count > 0)
		{
			throw new AggregateException(
				$"{exceptions.Count} handler(s) failed while publishing {notificationType.Name}",
				exceptions);
		}
	}

	private async Task PublishParallelWhenAll(
		INotification notification,
		Type notificationType,
		object[] handlers,
		object[] behaviors,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		var tasks = handlers.Select(handler =>
		{
			var handlerFunc = CreateNotificationHandlerFunc(notification, handler, handleMethod, cancellationToken);
			handlerFunc = ChainBehaviors(behaviors, notification, handlerFunc, cancellationToken);
			return handlerFunc();
		}).ToArray();

		try
		{
			await Task.WhenAll(tasks);
		}
		catch
		{
			var exceptions = tasks
				.Where(t => t.IsFaulted && t.Exception != null)
				.Select(t => t.Exception!.InnerException ?? t.Exception)
				.ToList();

			if (exceptions.Count > 0)
			{
				throw new AggregateException(
					$"{exceptions.Count} handler(s) failed while publishing {notificationType.Name} in parallel",
					exceptions);
			}
			throw;
		}
	}

	private Task PublishParallelNoWait(
		INotification notification,
		Type notificationType,
		object[] handlers,
		object[] behaviors,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		_ = Task.Run(async () =>
		{
			var tasks = handlers.Select(handler =>
			{
				var handlerFunc = CreateNotificationHandlerFunc(notification, handler, handleMethod, cancellationToken);
				handlerFunc = ChainBehaviors(behaviors, notification, handlerFunc, cancellationToken);
				return handlerFunc();
			}).ToArray();

			try
			{
				await Task.WhenAll(tasks);
			}
			catch (Exception ex)
			{
				_logger?.LogError(ex, "One or more handlers failed for notification {NotificationType}", notificationType.Name);
			}
		}, cancellationToken);

		return Task.CompletedTask;
	}

	private Func<Task<Unit>> CreateNotificationHandlerFunc(
		INotification notification,
		object handler,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		return async () =>
		{
			try
			{
				var task = (Task)handleMethod.Invoke(handler, new object[] { notification, cancellationToken })!;
				await task;
				return Unit.Value;
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				throw;
			}
		};
	}

	private Func<Task<TResponse>> ChainBehaviors<TMessage, TResponse>(
		object[] behaviors,
		TMessage message,
		Func<Task<TResponse>> handlerFunc,
		CancellationToken cancellationToken)
	{
		foreach (var behavior in behaviors)
		{
			var currentNext = handlerFunc;
			var currentBehavior = behavior;

			handlerFunc = async () =>
			{
				try
				{
					var behaviorHandleMethod = behavior.GetType().GetMethod("Handle")!;
					var task = (Task<TResponse>)behaviorHandleMethod.Invoke(
						currentBehavior,
						new object[] { message!, currentNext, cancellationToken })!;
					return await task;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};
		}

		return handlerFunc;
	}
}