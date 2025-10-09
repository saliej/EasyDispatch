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

		if (options.Assemblies == null || options.Assemblies.Length == 0)
			throw new InvalidOperationException("No assemblies specified for scanning. " +
				"Set MediatorOptions.Assemblies to the assemblies containing your message types.");

		var validationResults = PerformValidation(services, options.Assemblies);

		if (validationResults.Count == 0)
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

		PerformMultipleInterfaceValidation(messageTypes, results);

		PerformQueryValidation(services, messageTypes, results);

		PerformStreamingQueryValidation(services, messageTypes, results);

		PerformVoidCommandValidation(services, results, messageTypes);

		PerformResponseCommandValidation(services, messageTypes, results);

		// Note: Notifications are NOT validated as they can have zero or multiple handlers
		// This is by design for the pub/sub pattern

		return results;
	}

	private static void PerformResponseCommandValidation(IServiceCollection services, List<Type> messageTypes, List<ValidationResult> results)
	{
		// Validate commands with response
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

			var handlerCount = CountHandlers(services, handlerType);

			if (handlerCount == 0)
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command",
					ValidationIssue.MissingHandler));
			}
			else if (handlerCount > 1)
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command",
					ValidationIssue.MultipleHandlers,
					handlerCount));
			}
		}
	}

	private static void PerformVoidCommandValidation(IServiceCollection services, List<ValidationResult> results, List<Type> messageTypes)
	{
		var voidCommands = messageTypes
			.Where(t => t.GetInterfaces().Contains(typeof(ICommand)))
			.ToList();

		foreach (var command in voidCommands)
		{
			var handlerType = typeof(ICommandHandler<>).MakeGenericType(command);

			var handlerCount = CountHandlers(services, handlerType);

			if (handlerCount == 0)
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command (void)",
					ValidationIssue.MissingHandler));
			}
			else if (handlerCount > 1)
			{
				results.Add(new ValidationResult(
					command,
					handlerType,
					"Command (void)",
					ValidationIssue.MultipleHandlers,
					handlerCount));
			}
		}
	}

	private static void PerformStreamingQueryValidation(IServiceCollection services, List<Type> messageTypes, List<ValidationResult> results)
	{
		var streamQueries = messageTypes
			.Where(t => t.GetInterfaces()
				.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamQuery<>)))
			.ToList();

		foreach (var query in streamQueries)
		{
			var queryInterface = query.GetInterfaces()
				.First(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamQuery<>));

			var resultType = queryInterface.GetGenericArguments()[0];
			var handlerType = typeof(IStreamQueryHandler<,>).MakeGenericType(query, resultType);

			var handlerCount = CountHandlers(services, handlerType);

			if (handlerCount == 0)
			{
				results.Add(new ValidationResult(
					query,
					handlerType,
					"StreamQuery",
					ValidationIssue.MissingHandler));
			}
			else if (handlerCount > 1)
			{
				results.Add(new ValidationResult(
					query,
					handlerType,
					"StreamQuery",
					ValidationIssue.MultipleHandlers,
					handlerCount));
			}
		}
	}

	private static void PerformQueryValidation(IServiceCollection services, List<Type> messageTypes, List<ValidationResult> results)
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

			var handlerCount = CountHandlers(services, handlerType);

			if (handlerCount == 0)
			{
				results.Add(new ValidationResult(
					query,
					handlerType,
					"Query",
					ValidationIssue.MissingHandler));
			}
			else if (handlerCount > 1)
			{
				results.Add(new ValidationResult(
					query,
					handlerType,
					"Query",
					ValidationIssue.MultipleHandlers,
					handlerCount));
			}
		}
	}

	private static void PerformMultipleInterfaceValidation(List<Type> messageTypes, List<ValidationResult> results)
	{
		foreach (var type in messageTypes)
		{
			var interfaces = type.GetInterfaces();
			int interfaceCount = 0;
			string[] implementedInterfaces = new string[4];
			int index = 0;

			if (interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)))
			{
				interfaceCount++;
				implementedInterfaces[index++] = "IQuery<>";
			}

			if (interfaces.Contains(typeof(ICommand)))
			{
				interfaceCount++;
				implementedInterfaces[index++] = "ICommand";
			}

			if (interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>)))
			{
				interfaceCount++;
				implementedInterfaces[index++] = "ICommand<>";
			}

			if (interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IStreamQuery<>)))
			{
				interfaceCount++;
				implementedInterfaces[index++] = "IStreamQuery<>";
			}

			if (interfaces.Contains(typeof(INotification)))
			{
				interfaceCount++;
				implementedInterfaces[index++] = "INotification";
			}

			if (interfaceCount > 1)
			{
				results.Add(new ValidationResult(
					type,
					null!,
					string.Join(", ", implementedInterfaces.Take(index)),
					ValidationIssue.MultipleMessageInterfaces,
					interfaceCount));
			}
		}
	}

	private static int CountHandlers(IServiceCollection services, Type handlerType)
	{
		return services.Count(d => d.ServiceType == handlerType);
	}

	private static void LogWarnings(List<ValidationResult> results, ILogger? logger)
	{
		var missingHandlers = results.Where(r => r.Issue == ValidationIssue.MissingHandler).ToList();
		var multipleHandlers = results.Where(r => r.Issue == ValidationIssue.MultipleHandlers).ToList();
		var multipleInterfaces = results.Where(r => r.Issue == ValidationIssue.MultipleMessageInterfaces).ToList();

		if (multipleInterfaces.Count > 0)
		{
			logger?.LogWarning(
				"EasyDispatch startup validation found {Count} message(s) implementing multiple message interfaces:",
				multipleInterfaces.Count);

			foreach (var result in multipleInterfaces)
			{
				logger?.LogWarning(
					"  '{MessageName}' implements {Count} interfaces: {Interfaces}. A message should only implement one message interface.",
					result.MessageType.Name,
					result.HandlerCount,
					result.MessageCategory);
			}
		}

		if (missingHandlers.Count > 0)
		{
			logger?.LogWarning(
				"EasyDispatch startup validation found {Count} message(s) without handlers:",
				missingHandlers.Count);

			foreach (var result in missingHandlers)
			{
				logger?.LogWarning(
					"  {MessageType} '{MessageName}' has no registered handler ({HandlerType})",
					result.MessageCategory,
					result.MessageType.Name,
					result.ExpectedHandlerType.Name);
			}
		}

		if (multipleHandlers.Count > 0)
		{
			logger?.LogWarning(
				"EasyDispatch startup validation found {Count} message(s) with multiple handlers:",
				multipleHandlers.Count);

			foreach (var result in multipleHandlers)
			{
				logger?.LogWarning(
					"  {MessageType} '{MessageName}' has {Count} handlers registered (expected 1). " +
					"Only the last registered handler will be used.",
					result.MessageCategory,
					result.MessageType.Name,
					result.HandlerCount);
			}
		}

		logger?.LogWarning(
			"These messages will fail at runtime if dispatched. " +
			"Register handlers or set StartupValidation to None to suppress this warning.");
	}

	private static void ThrowValidationException(List<ValidationResult> results)
	{
		var missingHandlers = results.Where(r => r.Issue == ValidationIssue.MissingHandler).ToList();
		var multipleHandlers = results.Where(r => r.Issue == ValidationIssue.MultipleHandlers).ToList();
		var multipleInterfaces = results.Where(r => r.Issue == ValidationIssue.MultipleMessageInterfaces).ToList();

		var errorMessages = new List<string>();

		if (multipleInterfaces.Count > 0)
		{
			errorMessages.Add($"\nMultiple Message Interfaces ({multipleInterfaces.Count}):");
			errorMessages.AddRange(multipleInterfaces.Select(r =>
				$"  - '{r.MessageType.FullName}' implements {r.HandlerCount} message interfaces: {r.MessageCategory}. " +
				$"A message should only implement one message interface."));
		}

		if (missingHandlers.Count > 0)
		{
			errorMessages.Add($"\nMissing Handlers ({missingHandlers.Count}):");
			errorMessages.AddRange(missingHandlers.Select(r =>
				$"  - {r.MessageCategory} '{r.MessageType.FullName}' has no registered handler " +
				$"(expected {r.ExpectedHandlerType.FullName})"));
		}

		if (multipleHandlers.Count > 0)
		{
			errorMessages.Add($"\nMultiple Handlers ({multipleHandlers.Count}):");
			errorMessages.AddRange(multipleHandlers.Select(r =>
				$"  - {r.MessageCategory} '{r.MessageType.FullName}' has {r.HandlerCount} handlers registered " +
				$"(expected 1). Only the last registered handler will be used."));
		}

		var message =
			$"EasyDispatch startup validation failed. {results.Count} issue(s) found:" +
			string.Join("\n", errorMessages) + "\n\n" +
			"To fix this issue:\n" +
			"  1. Register exactly one handler for each query/command/stream query\n" +
			"  2. Remove duplicate handler registrations\n" +
			"  3. Remove unused message types from the scanned assemblies\n" +
			"  4. Set StartupValidation to None or Warn to allow startup despite these issues";

		throw new InvalidOperationException(message);
	}

	private enum ValidationIssue
	{
		MissingHandler,
		MultipleHandlers,
		MultipleMessageInterfaces
	}

	private record ValidationResult(
		Type MessageType,
		Type ExpectedHandlerType,
		string MessageCategory,
		ValidationIssue Issue,
		int HandlerCount = 0);
}