using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EasyDispatch;

/// <summary>
/// Validates that message types have corresponding handlers registered.
/// </summary>
internal static class StartupValidator
{
	/// <summary>
	/// Validates handler registration for all message types found in the specified assemblies.
	/// </summary>
	public static void ValidateHandlers(
		IServiceCollection services,
		MediatorOptions options,
		ILogger? logger = null)
	{
		if (options.StartupValidation == StartupValidation.None)
			return;

		var validationResults = PerformValidation(services, options.Assemblies);

		if (!validationResults.Any())
			return;

		switch (options.StartupValidation)
		{
			case StartupValidation.Warn:
				LogWarnings(validationResults, logger);
				break;

			case StartupValidation.FailFast:
				ThrowValidationException(validationResults);
				break;
		}
	}

	private static List<ValidationResult> PerformValidation(
		IServiceCollection services,
		Assembly[] assemblies)
	{
		var results = new List<ValidationResult>();

		var messageTypes = assemblies
			.SelectMany(a => a.GetTypes())
			.Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition)
			.ToList();

		ValidateQueries(services, results, messageTypes);
		ValidateVoidCommands(services, results, messageTypes);
		ValidateCommandsWithResponse(services, results, messageTypes);

		// Note: Notifications are NOT validated as they can have zero or multiple handlers
		// This is by design for the pub/sub pattern

		return results;
	}

	private static void ValidateCommandsWithResponse(IServiceCollection services, List<ValidationResult> results, List<Type> messageTypes)
	{
		var commandsWithResponse = messageTypes
			.Where(t => t.GetInterfaces()
				.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
			.ToList();

		foreach (var command in commandsWithResponse)
		{
			var commandInterface = command.GetInterfaces()
				.First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

			var responseType = commandInterface.GetGenericArguments()[0];
			var handlerType = typeof(ICommandHandler<,>).MakeGenericType(command, responseType);

			if (!IsHandlerRegistered(services, handlerType))
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command"));
			}
		}
	}

	private static void ValidateVoidCommands(IServiceCollection services, List<ValidationResult> results, List<Type> messageTypes)
	{
		var voidCommands = messageTypes
			.Where(t => t.GetInterfaces().Contains(typeof(ICommand)))
			.ToList();

		foreach (var command in voidCommands)
		{
			var handlerType = typeof(ICommandHandler<>).MakeGenericType(command);

			if (!IsHandlerRegistered(services, handlerType))
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command (void)"));
			}
		}
	}

	private static void ValidateQueries(IServiceCollection services, List<ValidationResult> results, List<Type> messageTypes)
	{
		var queries = messageTypes
			.Where(t => t.GetInterfaces()
				.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)))
			.ToList();

		foreach (var query in queries)
		{
			var queryInterface = query.GetInterfaces()
				.First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

			var responseType = queryInterface.GetGenericArguments()[0];
			var handlerType = typeof(IQueryHandler<,>).MakeGenericType(query, responseType);

			if (!IsHandlerRegistered(services, handlerType))
			{
				results.Add(new ValidationResult(
					query,
					handlerType,
					"Query"));
			}
		}
	}

	private static bool IsHandlerRegistered(IServiceCollection services, Type handlerType)
	{
		return services.Any(d => d.ServiceType == handlerType);
	}

	private static void LogWarnings(List<ValidationResult> results, ILogger? logger)
	{
		logger?.LogWarning(
			"EasyDispatch startup validation found {Count} message(s) without handlers:",
			results.Count);

		foreach (var result in results)
		{
			logger?.LogWarning(
				"  {MessageType} '{MessageName}' has no registered handler ({HandlerType})",
				result.MessageCategory,
				result.MessageType.Name,
				result.ExpectedHandlerType.Name);
		}

		logger?.LogWarning(
			"These messages will fail at runtime if dispatched. " +
			"Register handlers or set StartupValidation to None to suppress this warning.");
	}

	private static void ThrowValidationException(List<ValidationResult> results)
	{
		var errorMessages = results.Select(r =>
			$"  - {r.MessageCategory} '{r.MessageType.FullName}' has no registered handler " +
			$"(expected {r.ExpectedHandlerType.FullName})"
		).ToList();

		var message =
			$"EasyDispatch startup validation failed. {results.Count} message(s) have no registered handlers:\n" +
			string.Join("\n", errorMessages) + "\n\n" +
			"To fix this issue:\n" +
			"  1. Register handlers for all messages in the assemblies provided to AddMediator()\n" +
			"  2. Remove unused message types from the scanned assemblies\n" +
			"  3. Set StartupValidation to None or Warn to allow startup without handlers";

		throw new InvalidOperationException(message);
	}

	private record ValidationResult(
		Type MessageType,
		Type ExpectedHandlerType,
		string MessageCategory);
}