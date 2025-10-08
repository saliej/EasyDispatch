using System.Reflection;
using EasyDispatch;

#pragma warning disable IDE0130
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>
/// Extension methods for registering the Mediator and its handlers with dependency injection.
/// </summary>
public static class ServiceCollectionExtensions
{
	/// <summary>
	/// Registers the Mediator with custom configuration.
	/// </summary>
	/// <param name="services">The service collection</param>
	/// <param name="configure">Configuration action for MediatorOptions</param>
	/// <returns>Builder for configuring pipeline behaviors</returns>
	public static IMediatorBuilder AddMediator(
		this IServiceCollection services,
		Action<MediatorOptions> configure)
	{
		if (services == null)
			throw new ArgumentNullException(nameof(services));

		if (configure == null)
			throw new ArgumentNullException(nameof(configure));

		var options = new MediatorOptions();
		configure(options);

		if (options.Assemblies == null || options.Assemblies.Length == 0)
			throw new ArgumentException("At least one assembly must be provided in MediatorOptions", nameof(configure));

		// Register options as singleton
		services.AddSingleton(options);

		// Register the mediator itself as scoped
		services.AddScoped<IMediator, Mediator>();

		// Scan and register all handlers from the specified assemblies
		RegisterHandlers(services, options.Assemblies, options.HandlerLifetime);

		// Perform startup validation if configured
		StartupValidator.ValidateHandlers(services, options);

		return new MediatorBuilder(services);
	}

	/// <summary>
	/// Registers the Mediator, scans assemblies for handlers, and configures pipeline behaviors.
	/// </summary>
	/// <param name="services">The service collection</param>
	/// <param name="assemblies">Assemblies to scan for handlers</param>
	/// <returns>Builder for configuring pipeline behaviors</returns>
	public static IMediatorBuilder AddMediator(
		this IServiceCollection services,
		params Assembly[] assemblies)
	{
		if (services == null)
			throw new ArgumentNullException(nameof(services));

		if (assemblies == null || assemblies.Length == 0)
			throw new ArgumentException("At least one assembly must be provided", nameof(assemblies));

		// Use default options
		var options = new MediatorOptions { Assemblies = assemblies };
		services.AddSingleton(options);

		// Register the mediator itself as scoped
		services.AddScoped<IMediator, Mediator>();

		// Scan and register all handlers from the specified assemblies
		RegisterHandlers(services, assemblies, options.HandlerLifetime);

		// Perform startup validation if configured
		StartupValidator.ValidateHandlers(services, options);

		return new MediatorBuilder(services);
	}

	/// <summary>
	/// Registers the Mediator and scans the calling assembly for handlers.
	/// </summary>
	public static IMediatorBuilder AddMediator(this IServiceCollection services)
	{
		var callingAssembly = Assembly.GetCallingAssembly();
		return AddMediator(services, callingAssembly);
	}

	private static void RegisterHandlers(
		IServiceCollection services,
		Assembly[] assemblies,
		ServiceLifetime lifetime)
	{
		var handlerTypes = assemblies
			.SelectMany(a => a.GetTypes())
			.Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
			.ToList();

		// Register Query Handlers: IQueryHandler<TQuery, TResponse>
		RegisterHandlersOfType(
			services,
			handlerTypes,
			typeof(IQueryHandler<,>),
			lifetime);

		// Register Stream Query Handlers: IStreamQueryHandler<TQuery, TResult>
		RegisterHandlersOfType(
			services,
			handlerTypes,
			typeof(IStreamQueryHandler<,>),
			lifetime);

		// Register Command Handlers (void): ICommandHandler<TCommand>
		RegisterHandlersOfType(
			services,
			handlerTypes,
			typeof(ICommandHandler<>),
			lifetime);

		// Register Command Handlers (with response): ICommandHandler<TCommand, TResponse>
		RegisterHandlersOfType(
			services,
			handlerTypes,
			typeof(ICommandHandler<,>),
			lifetime);

		// Register Notification Handlers: INotificationHandler<TNotification>
		// Note: Multiple handlers can exist for the same notification
		RegisterHandlersOfType(
			services,
			handlerTypes,
			typeof(INotificationHandler<>),
			lifetime);
	}

	private static void RegisterHandlersOfType(
		IServiceCollection services,
		List<Type> candidateTypes,
		Type handlerInterfaceType,
		ServiceLifetime lifetime)
	{
		foreach (var type in candidateTypes)
		{
			var interfaces = type.GetInterfaces()
				.Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterfaceType)
				.ToList();

			foreach (var @interface in interfaces)
			{
				services.Add(new ServiceDescriptor(@interface, type, lifetime));
			}
		}
	}
}

/// <summary>
/// Builder for configuring Mediator pipeline behaviors.
/// </summary>
public interface IMediatorBuilder
{
	/// <summary>
	/// The service collection being configured.
	/// </summary>
	IServiceCollection Services { get; }

	/// <summary>
	/// Adds a pipeline behavior for a specific message and response type.
	/// </summary>
	IMediatorBuilder AddBehavior<TMessage, TResponse, TBehavior>()
		where TBehavior : class, IPipelineBehavior<TMessage, TResponse>;

	/// <summary>
	/// Adds an open generic pipeline behavior that will be applied to all matching messages.
	/// </summary>
	IMediatorBuilder AddOpenBehavior(Type openBehaviorType);

	/// <summary>
	/// Adds a pipeline behavior instance for a specific message and response type.
	/// </summary>
	IMediatorBuilder AddBehavior<TMessage, TResponse>(
		IPipelineBehavior<TMessage, TResponse> behaviorInstance);

	/// <summary>
	/// Adds a stream pipeline behavior for a specific streaming query and result type.
	/// </summary>
	IMediatorBuilder AddStreamBehavior<TQuery, TResult, TBehavior>()
		where TBehavior : class, IStreamPipelineBehavior<TQuery, TResult>;

	/// <summary>
	/// Adds an open generic stream pipeline behavior that will be applied to all matching streaming queries.
	/// </summary>
	IMediatorBuilder AddOpenStreamBehavior(Type openBehaviorType);

	/// <summary>
	/// Adds a stream pipeline behavior instance for a specific streaming query and result type.
	/// </summary>
	IMediatorBuilder AddStreamBehavior<TQuery, TResult>(
		IStreamPipelineBehavior<TQuery, TResult> behaviorInstance);
}

internal sealed class MediatorBuilder : IMediatorBuilder
{
	public IServiceCollection Services { get; }

	public MediatorBuilder(IServiceCollection services)
	{
		Services = services ?? throw new ArgumentNullException(nameof(services));
	}

	public IMediatorBuilder AddBehavior<TMessage, TResponse, TBehavior>()
		where TBehavior : class, IPipelineBehavior<TMessage, TResponse>
	{
		Services.AddScoped<IPipelineBehavior<TMessage, TResponse>, TBehavior>();
		return this;
	}

	public IMediatorBuilder AddOpenBehavior(Type openBehaviorType)
	{
		if (openBehaviorType == null)
			throw new ArgumentNullException(nameof(openBehaviorType));

		if (!openBehaviorType.IsGenericTypeDefinition)
			throw new ArgumentException("Type must be an open generic type", nameof(openBehaviorType));

		// Verify it implements IPipelineBehavior<,>
		var implementsInterface = openBehaviorType
			.GetInterfaces()
			.Any(i => i.IsGenericType &&
					 i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

		if (!implementsInterface)
			throw new ArgumentException(
				$"Type {openBehaviorType.Name} must implement IPipelineBehavior<,>",
				nameof(openBehaviorType));

		Services.AddScoped(typeof(IPipelineBehavior<,>), openBehaviorType);
		return this;
	}

	public IMediatorBuilder AddBehavior<TMessage, TResponse>(
		IPipelineBehavior<TMessage, TResponse> behaviorInstance)
	{
		if (behaviorInstance == null)
			throw new ArgumentNullException(nameof(behaviorInstance));

		Services.AddSingleton(behaviorInstance);
		return this;
	}

	public IMediatorBuilder AddStreamBehavior<TQuery, TResult, TBehavior>()
		where TBehavior : class, IStreamPipelineBehavior<TQuery, TResult>
	{
		Services.AddScoped<IStreamPipelineBehavior<TQuery, TResult>, TBehavior>();
		return this;
	}

	public IMediatorBuilder AddOpenStreamBehavior(Type openBehaviorType)
	{
		if (openBehaviorType == null)
			throw new ArgumentNullException(nameof(openBehaviorType));

		if (!openBehaviorType.IsGenericTypeDefinition)
			throw new ArgumentException("Type must be an open generic type", nameof(openBehaviorType));

		// Verify it implements IStreamPipelineBehavior<,>
		var implementsInterface = openBehaviorType
			.GetInterfaces()
			.Any(i => i.IsGenericType &&
					 i.GetGenericTypeDefinition() == typeof(IStreamPipelineBehavior<,>));

		if (!implementsInterface)
			throw new ArgumentException(
				$"Type {openBehaviorType.Name} must implement IStreamPipelineBehavior<,>",
				nameof(openBehaviorType));

		Services.AddScoped(typeof(IStreamPipelineBehavior<,>), openBehaviorType);
		return this;
	}

	public IMediatorBuilder AddStreamBehavior<TQuery, TResult>(
		IStreamPipelineBehavior<TQuery, TResult> behaviorInstance)
	{
		if (behaviorInstance == null)
			throw new ArgumentNullException(nameof(behaviorInstance));

		Services.AddSingleton(behaviorInstance);
		return this;
	}
}