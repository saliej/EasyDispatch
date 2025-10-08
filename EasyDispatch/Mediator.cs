using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyDispatch;

public interface IMediator
{
	Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default);
	Task SendAsync(ICommand command, CancellationToken cancellationToken = default);
	Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default);
	Task PublishAsync(INotification notification, CancellationToken cancellationToken = default);
	Task PublishAsync(INotification notification, NotificationPublishStrategy publishStrategy, CancellationToken cancellationToken = default);
	IAsyncEnumerable<TResult> StreamAsync<TResult>(IStreamQuery<TResult> query, CancellationToken cancellationToken = default);
}

public sealed class Mediator : IMediator
{
	private readonly IServiceProvider _serviceProvider;
	private readonly MediatorOptions _options;
	private readonly ILogger<Mediator>? _logger;

	private static readonly ConcurrentDictionary<Type, MethodInfo> _handleMethodCache = new();
	private static readonly ConcurrentDictionary<Type, Type> _handlerTypeCache = new();
	private static readonly ConcurrentDictionary<Type, Type> _behaviorTypeCache = new();

	public Mediator(IServiceProvider serviceProvider, ILogger<Mediator>? logger = null)
		: this(serviceProvider, null, logger) { }

	public Mediator(IServiceProvider serviceProvider, MediatorOptions? options, ILogger<Mediator>? logger = null)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
		_options = options ?? new MediatorOptions();
		_logger = logger;
	}

	public Task<TResponse> SendAsync<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return SendQueryInternal(query, cancellationToken);
	}

	public IAsyncEnumerable<TResult> StreamAsync<TResult>(IStreamQuery<TResult> query, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return StreamQueryInternal(query, cancellationToken);
	}

	private async IAsyncEnumerable<TResult> StreamQueryInternal<TResult>(
	IStreamQuery<TResult> query,
	[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
	{
		var queryType = query.GetType();

		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			queryType.FullName ?? queryType.Name,
			ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", queryType.FullName);
		activity?.SetTag("easydispatch.result_type", typeof(TResult).FullName);
		activity?.SetTag("easydispatch.operation", "StreamQuery");

		int itemCount = 0;
		Exception? streamException = null;

		// Use cached handler type
		var handlerType = _handlerTypeCache.GetOrAdd(
			queryType,
			qt => typeof(IStreamQueryHandler<,>).MakeGenericType(qt, typeof(TResult)));

		var handler = _serviceProvider.GetService(handlerType);

		if (handler == null)
		{
			activity?.SetStatus(ActivityStatusCode.Error, "Handler not found");
			throw new InvalidOperationException(
				$"No handler registered for streaming query '{queryType.Name}'.\n" +
				$"Expected a handler implementing IStreamQueryHandler<{queryType.Name}, {typeof(TResult).Name}>.\n" +
				$"Did you forget to call AddMediator() with the assembly containing your handlers?");
		}

		activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

		// Get cached Handle method
		var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
			ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

		// Get stream behaviors
		var behaviorType = _behaviorTypeCache.GetOrAdd(
			queryType,
			qt => typeof(IStreamPipelineBehavior<,>).MakeGenericType(qt, typeof(TResult)));

		var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
		activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

		// Build the handler execution
		Func<IAsyncEnumerable<TResult>> handlerFunc = () =>
		{
			try
			{
				var stream = (IAsyncEnumerable<TResult>)handleMethod.Invoke(handler, [query, cancellationToken])!;
				return stream;
			}
			catch (TargetInvocationException ex) when (ex.InnerException != null)
			{
				System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
				throw;
			}
		};

		// Chain behaviors
		handlerFunc = ChainStreamBehaviors(behaviors, query, handlerFunc, cancellationToken);

		// Execute with exception handling
		IAsyncEnumerable<TResult> resultStream;
		try
		{
			resultStream = handlerFunc();
		}
		catch (Exception ex)
		{
			streamException = ex;
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}

		// Stream enumeration without try-catch
		await foreach (var item in resultStream.WithCancellation(cancellationToken))
		{
			itemCount++;
			yield return item;
		}

		activity?.SetTag("easydispatch.stream_item_count", itemCount.ToString());
		activity?.SetStatus(ActivityStatusCode.Ok);
	}

	private async Task<TResponse> SendQueryInternal<TResponse>(IQuery<TResponse> query, CancellationToken cancellationToken)
	{
		var queryType = query.GetType();
		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			queryType.FullName ?? queryType.Name, ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", queryType.FullName);
		activity?.SetTag("easydispatch.response_type", typeof(TResponse).FullName);
		activity?.SetTag("easydispatch.operation", "Query");

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(queryType,
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

			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			var behaviorType = _behaviorTypeCache.GetOrAdd(queryType,
				qt => typeof(IPipelineBehavior<,>).MakeGenericType(qt, typeof(TResponse)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			Func<Task<TResponse>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task<TResponse>)handleMethod.Invoke(handler, [query, cancellationToken])!;
					return await task;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

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

	public Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		return SendCommandInternal(command, cancellationToken);
	}

	private async Task SendCommandInternal(ICommand command, CancellationToken cancellationToken)
	{
		var commandType = command.GetType();
		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			commandType.FullName ?? commandType.Name, ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", commandType.FullName);
		activity?.SetTag("easydispatch.response_type", "void");
		activity?.SetTag("easydispatch.operation", "Command");

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(commandType,
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

			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			var behaviorType = _behaviorTypeCache.GetOrAdd(commandType,
				ct => typeof(IPipelineBehavior<,>).MakeGenericType(ct, typeof(Unit)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			Func<Task<Unit>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task)handleMethod.Invoke(handler, [command, cancellationToken])!;
					await task;
					return Unit.Value;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

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

	public Task<TResponse> SendAsync<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);
		return SendCommandWithResponseInternal(command, cancellationToken);
	}

	private async Task<TResponse> SendCommandWithResponseInternal<TResponse>(ICommand<TResponse> command, CancellationToken cancellationToken)
	{
		var commandType = command.GetType();
		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			commandType.FullName ?? commandType.Name, ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", commandType.FullName);
		activity?.SetTag("easydispatch.response_type", typeof(TResponse).FullName);
		activity?.SetTag("easydispatch.operation", "Command");

		try
		{
			var handlerType = _handlerTypeCache.GetOrAdd(commandType,
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

			activity?.SetTag("easydispatch.handler_type", handler.GetType().FullName);

			var handleMethod = _handleMethodCache.GetOrAdd(handlerType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			var behaviorType = _behaviorTypeCache.GetOrAdd(commandType,
				ct => typeof(IPipelineBehavior<,>).MakeGenericType(ct, typeof(TResponse)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length.ToString());

			Func<Task<TResponse>> handlerFunc = async () =>
			{
				try
				{
					var task = (Task<TResponse>)handleMethod.Invoke(handler, [command, cancellationToken])!;
					return await task;
				}
				catch (TargetInvocationException ex) when (ex.InnerException != null)
				{
					System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
					throw;
				}
			};

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

	public async Task PublishAsync(INotification notification, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(notification);
		await PublishAsyncInternal(notification, _options.NotificationPublishStrategy, cancellationToken);
	}

	public async Task PublishAsync(INotification notification, NotificationPublishStrategy publishStrategy, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(notification);
		await PublishAsyncInternal(notification, publishStrategy, cancellationToken);
	}

	private async Task PublishAsyncInternal(INotification notification, NotificationPublishStrategy publishStrategy, CancellationToken cancellationToken)
	{
		var notificationType = notification.GetType();
		using var activity = EasyDispatchActivitySource.Source.StartActivity(
			notificationType.FullName ?? notificationType.Name, ActivityKind.Internal);

		activity?.SetTag("easydispatch.message_type", notificationType.FullName);
		activity?.SetTag("easydispatch.operation", "Notification");
		activity?.SetTag("easydispatch.publish_strategy", publishStrategy.ToString());

		try
		{
			// Get behaviors for the actual notification type being published
			var behaviorType = _behaviorTypeCache.GetOrAdd(notificationType,
				nt => typeof(IPipelineBehavior<,>).MakeGenericType(nt, typeof(Unit)));

			var behaviors = _serviceProvider.GetServices(behaviorType).Reverse().ToArray();
			activity?.SetTag("easydispatch.behavior_count", behaviors.Length);

			// Get all notification types (polymorphic dispatch: current type + base types + interfaces)
			var notificationTypes = GetNotificationTypes(notificationType);

			// Collect all handlers for all notification types
			var allHandlers = new List<(Type handlerInterfaceType, object handler)>();

			foreach (var type in notificationTypes)
			{
				var handlerType = _handlerTypeCache.GetOrAdd(type,
					nt => typeof(INotificationHandler<>).MakeGenericType(nt));

				var handlers = _serviceProvider.GetServices(handlerType);

				foreach (var handler in handlers)
				{
					allHandlers.Add((handlerType, handler));
				}
			}

			activity?.SetTag("easydispatch.handler_count", allHandlers.Count.ToString());

			if (allHandlers.Count == 0)
			{
				_logger?.LogDebug("No handlers registered for notification {NotificationType}", notificationType.Name);
				activity?.SetStatus(ActivityStatusCode.Ok);
				return;
			}

			_logger?.LogDebug("Publishing notification {NotificationType} to {HandlerCount} handlers",
				notificationType.Name, allHandlers.Count);

			// Execute based on strategy
			await ExecutePolymorphicNotificationHandlers(notification, notificationType, allHandlers, behaviors, publishStrategy, cancellationToken);

			activity?.SetStatus(ActivityStatusCode.Ok);
		}
		catch (Exception ex)
		{
			activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
			activity?.AddException(ex);
			throw;
		}
	}

	private static IEnumerable<Type> GetNotificationTypes(Type notificationType)
	{
		var types = new List<Type> { notificationType };

		// Add base types that implement INotification
		var currentType = notificationType.BaseType;
		while (currentType != null && currentType != typeof(object))
		{
			if (typeof(INotification).IsAssignableFrom(currentType))
			{
				types.Add(currentType);
			}
			currentType = currentType.BaseType;
		}

		// Add interfaces that implement INotification (excluding INotification itself)
		var interfaces = notificationType.GetInterfaces()
			.Where(i => typeof(INotification).IsAssignableFrom(i) && i != typeof(INotification));

		types.AddRange(interfaces);

		return types.Distinct();
	}

	private async Task ExecutePolymorphicNotificationHandlers(
		INotification notification,
		Type notificationType,
		List<(Type handlerInterfaceType, object handler)> handlers,
		object[] behaviors,
		NotificationPublishStrategy publishStrategy,
		CancellationToken cancellationToken)
	{
		switch (publishStrategy)
		{
			case NotificationPublishStrategy.StopOnFirstException:
				await PublishPolymorphicSequentialStopOnError(notification, handlers, behaviors, cancellationToken);
				break;

			case NotificationPublishStrategy.ContinueOnException:
				await PublishPolymorphicSequentialContinueOnError(notification, notificationType, handlers, behaviors, cancellationToken);
				break;

			case NotificationPublishStrategy.ParallelWhenAll:
				await PublishPolymorphicParallelWhenAll(notification, notificationType, handlers, behaviors, cancellationToken);
				break;

			case NotificationPublishStrategy.ParallelNoWait:
				_ = PublishPolymorphicParallelNoWait(notification, notificationType, handlers, behaviors, cancellationToken);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(publishStrategy));
		}
	}

	private static async Task PublishPolymorphicSequentialStopOnError(
		INotification notification,
		List<(Type handlerInterfaceType, object handler)> handlers,
		object[] behaviors,
		CancellationToken cancellationToken)
	{
		foreach (var (handlerInterfaceType, handler) in handlers)
		{
			var handleMethod = _handleMethodCache.GetOrAdd(handlerInterfaceType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

			var handlerFunc = CreateNotificationHandlerFunc(notification, handler, handleMethod, cancellationToken);
			handlerFunc = ChainBehaviors(behaviors, notification, handlerFunc, cancellationToken);
			await handlerFunc();
		}
	}

	private async Task PublishPolymorphicSequentialContinueOnError(
		INotification notification,
		Type notificationType,
		List<(Type handlerInterfaceType, object handler)> handlers,
		object[] behaviors,
		CancellationToken cancellationToken)
	{
		var exceptions = new List<Exception>();

		foreach (var (handlerInterfaceType, handler) in handlers)
		{
			try
			{
				var handleMethod = _handleMethodCache.GetOrAdd(handlerInterfaceType, ht =>
					ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

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

	private static async Task PublishPolymorphicParallelWhenAll(
		INotification notification,
		Type notificationType,
		List<(Type handlerInterfaceType, object handler)> handlers,
		object[] behaviors,
		CancellationToken cancellationToken)
	{
		var tasks = handlers.Select(tuple =>
		{
			var (handlerInterfaceType, handler) = tuple;
			var handleMethod = _handleMethodCache.GetOrAdd(handlerInterfaceType, ht =>
				ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

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

	private Task PublishPolymorphicParallelNoWait(
		INotification notification,
		Type notificationType,
		List<(Type handlerInterfaceType, object handler)> handlers,
		object[] behaviors,
		CancellationToken cancellationToken)
	{
		_ = Task.Run(async () =>
		{
			var tasks = handlers.Select(tuple =>
			{
				var (handlerInterfaceType, handler) = tuple;
				var handleMethod = _handleMethodCache.GetOrAdd(handlerInterfaceType, ht =>
					ht.GetMethod("Handle") ?? throw new InvalidOperationException($"Handle method not found on {ht.Name}"));

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

	private static Func<Task<Unit>> CreateNotificationHandlerFunc(
		INotification notification,
		object handler,
		MethodInfo handleMethod,
		CancellationToken cancellationToken)
	{
		return async () =>
		{
			try
			{
				var task = (Task)handleMethod.Invoke(handler, [notification, cancellationToken])!;
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

	private static Func<Task<TResponse>> ChainBehaviors<TMessage, TResponse>(
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
						[message!, currentNext, cancellationToken])!;
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

	private static Func<IAsyncEnumerable<TResult>> ChainStreamBehaviors<TQuery, TResult>(
		object[] behaviors,
		TQuery query,
		Func<IAsyncEnumerable<TResult>> handlerFunc,
		CancellationToken cancellationToken)
	{
		foreach (var behavior in behaviors)
		{
			var currentNext = handlerFunc;
			var currentBehavior = behavior;

			handlerFunc = () =>
			{
				try
				{
					var behaviorHandleMethod = behavior.GetType().GetMethod("Handle")!;
					var stream = (IAsyncEnumerable<TResult>)behaviorHandleMethod.Invoke(
						currentBehavior,
						[query!, currentNext, cancellationToken])!;
					return stream;
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