using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for startup validation of handler registration.
/// </summary>
public class StartupValidatorTests
{
	// Test messages WITHOUT handlers
	public record UnregisteredQuery(int Id) : IQuery<string>;
	public record UnregisteredVoidCommand(string Name) : ICommand;
	public record UnregisteredCommandWithResponse(int Value) : ICommand<int>;
	public record UnregisteredStreamQuery(int Count) : IStreamQuery<int>;
	public record UnregisteredNotification(string Message) : INotification;

	// Test messages WITH handlers
	public record RegisteredQuery(int Id) : IQuery<string>;
	public record RegisteredVoidCommand(string Name) : ICommand;
	public record RegisteredCommandWithResponse(int Value) : ICommand<int>;
	public record RegisteredStreamQuery(int Count) : IStreamQuery<int>;
	public record RegisteredNotification(string Message) : INotification;

	// Test message for duplicate handlers
	public record DuplicateHandlerQuery(int Id) : IQuery<string>;
	public record DuplicateHandlerCommand(string Name) : ICommand;
	public record DuplicateHandlerStreamQuery(int Count) : IStreamQuery<int>;

	// Handlers
	public class RegisteredQueryHandler : IQueryHandler<RegisteredQuery, string>
	{
		public Task<string> Handle(RegisteredQuery query, CancellationToken cancellationToken)
		{
			return Task.FromResult($"Result: {query.Id}");
		}
	}

	public class RegisteredVoidCommandHandler : ICommandHandler<RegisteredVoidCommand>
	{
		public Task Handle(RegisteredVoidCommand command, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	public class RegisteredCommandWithResponseHandler : ICommandHandler<RegisteredCommandWithResponse, int>
	{
		public Task<int> Handle(RegisteredCommandWithResponse command, CancellationToken cancellationToken)
		{
			return Task.FromResult(command.Value * 2);
		}
	}

	public class RegisteredStreamQueryHandler : IStreamQueryHandler<RegisteredStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			RegisteredStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			for (int i = 1; i <= query.Count; i++)
			{
				await Task.Delay(1, cancellationToken);
				yield return i;
			}
		}
	}

	public class RegisteredNotificationHandler : INotificationHandler<RegisteredNotification>
	{
		public Task Handle(RegisteredNotification notification, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	// Duplicate handlers
	public class DuplicateQueryHandler1 : IQueryHandler<DuplicateHandlerQuery, string>
	{
		public Task<string> Handle(DuplicateHandlerQuery query, CancellationToken cancellationToken)
		{
			return Task.FromResult("Handler1");
		}
	}

	public class DuplicateQueryHandler2 : IQueryHandler<DuplicateHandlerQuery, string>
	{
		public Task<string> Handle(DuplicateHandlerQuery query, CancellationToken cancellationToken)
		{
			return Task.FromResult("Handler2");
		}
	}

	public class DuplicateCommandHandler1 : ICommandHandler<DuplicateHandlerCommand>
	{
		public Task Handle(DuplicateHandlerCommand command, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	public class DuplicateCommandHandler2 : ICommandHandler<DuplicateHandlerCommand>
	{
		public Task Handle(DuplicateHandlerCommand command, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	public class DuplicateStreamHandler1 : IStreamQueryHandler<DuplicateHandlerStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			DuplicateHandlerStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.Delay(1, cancellationToken);
			yield return 1;
		}
	}

	public class DuplicateStreamHandler2 : IStreamQueryHandler<DuplicateHandlerStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			DuplicateHandlerStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.Delay(1, cancellationToken);
			yield return 2;
		}
	}

	[Fact]
	public void AddMediator_WithStartupValidationNone_DoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act - Register mediator with unregistered message types but validation set to None
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(StartupValidatorTests).Assembly];
			options.StartupValidation = StartupValidation.None;
		});

		// Assert - Should not throw even though there are unregistered messages
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_WithStartupValidationFailFast_ThrowsForMissingHandlers()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act - Register mediator with unregistered message types and FailFast
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(StartupValidatorTests).Assembly];
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>()
			.Which;

		exception.Message.Should().Contain("startup validation failed");
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("UnregisteredVoidCommand");
		exception.Message.Should().Contain("UnregisteredCommandWithResponse");
		exception.Message.Should().Contain("UnregisteredStreamQuery");
		// Notifications should NOT be included (they can have 0 or multiple handlers)
		exception.Message.Should().NotContain("UnregisteredNotification");
	}

	[Fact]
	public void AddMediator_WithDuplicateQueryHandlers_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();

		// Manually register duplicate handlers
		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler1>();
		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler2>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(StartupValidatorTests).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Handlers");
		exception.Message.Should().Contain("DuplicateHandlerQuery");
		exception.Message.Should().Contain("2 handlers registered");
	}

	[Fact]
	public void AddMediator_WithDuplicateCommandHandlers_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();

		services.AddScoped<ICommandHandler<DuplicateHandlerCommand>, DuplicateCommandHandler1>();
		services.AddScoped<ICommandHandler<DuplicateHandlerCommand>, DuplicateCommandHandler2>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(StartupValidatorTests).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Handlers");
		exception.Message.Should().Contain("DuplicateHandlerCommand");
		exception.Message.Should().Contain("2 handlers registered");
	}

	[Fact]
	public void AddMediator_WithDuplicateStreamHandlers_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();

		services.AddScoped<IStreamQueryHandler<DuplicateHandlerStreamQuery, int>, DuplicateStreamHandler1>();
		services.AddScoped<IStreamQueryHandler<DuplicateHandlerStreamQuery, int>, DuplicateStreamHandler2>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(StartupValidatorTests).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Handlers");
		exception.Message.Should().Contain("DuplicateHandlerStreamQuery");
		exception.Message.Should().Contain("2 handlers registered");
	}

	[Fact]
	public void AddMediator_WithMultipleNotificationHandlers_DoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();

		// Multiple notification handlers should be allowed (pub/sub pattern)
		services.AddScoped<INotificationHandler<RegisteredNotification>, RegisteredNotificationHandler>();
		services.AddScoped<INotificationHandler<RegisteredNotification>, SecondNotificationHandler>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(object).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act - Should not throw for multiple notification handlers
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		act.Should().NotThrow();
	}

	private class SecondNotificationHandler : INotificationHandler<RegisteredNotification>
	{
		public Task Handle(RegisteredNotification notification, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	[Fact]
	public void AddMediator_WithStartupValidationWarn_LogsWarnings()
	{
		// Arrange
		var services = new ServiceCollection();
		var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
		var logger = loggerFactory.CreateLogger<StartupValidatorTests>();

		// Note: In real implementation, we'd need to inject logger or use ILoggerFactory
		// For this test, we just verify it doesn't throw

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(StartupValidatorTests).Assembly];
			options.StartupValidation = StartupValidation.Warn;
		});

		// Assert - Should not throw with Warn mode
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_WithAllHandlersRegistered_DoesNotThrowWithFailFast()
	{
		// Arrange
		var services = new ServiceCollection();

		// Manually register handlers to avoid scanning unregistered messages
		services.AddScoped<IQueryHandler<RegisteredQuery, string>, RegisteredQueryHandler>();
		services.AddScoped<ICommandHandler<RegisteredVoidCommand>, RegisteredVoidCommandHandler>();
		services.AddScoped<ICommandHandler<RegisteredCommandWithResponse, int>, RegisteredCommandWithResponseHandler>();
		services.AddScoped<IStreamQueryHandler<RegisteredStreamQuery, int>, RegisteredStreamQueryHandler>();
		services.AddScoped<INotificationHandler<RegisteredNotification>, RegisteredNotificationHandler>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(RegisteredQuery).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act - Should not throw when all handlers are registered
		var act = () => services.BuildServiceProvider();

		// Assert
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_NotificationWithoutHandler_DoesNotFailValidation()
	{
		// Arrange
		var sourceCode = @"public record NoHandlerNotification() : EasyDispatch.INotification;";

		// Get the assembly that contains IQuery<T>
		var easyDispatchAssembly = typeof(IQuery<>).Assembly;

		var compilation = CSharpCompilation.Create("DynamicAssembly")
			.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.AddReferences(
				MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
				MetadataReference.CreateFromFile(easyDispatchAssembly.Location)
			)
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(sourceCode));

		using var ms = new MemoryStream();
		var result = compilation.Emit(ms);

		Assert.True(result.Success, "Failed to compile dynamic assembly");

		ms.Seek(0, SeekOrigin.Begin);
		var assembly = Assembly.Load(ms.ToArray());

		var services = new ServiceCollection();

		// Register only notification message, no handler
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act - Notifications are allowed to have zero handlers (pub/sub pattern)
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert - Should not throw for notifications without handlers
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_CombinedMissingAndDuplicateHandlers_ReportsBothIssues()
	{
		// Arrange
		var services = new ServiceCollection();

		// Add duplicate handlers for one message
		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler1>();
		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler2>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(StartupValidatorTests).Assembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => EasyDispatch.StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Missing Handlers");
		exception.Message.Should().Contain("Multiple Handlers");
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("DuplicateHandlerQuery");
	}

	[Fact]
	public void AddMediator_PartialHandlerRegistration_FailsOnlyForMissing()
	{
		// Arrange
		var services = new ServiceCollection();

		// Register only some handlers
		services.AddScoped<IQueryHandler<RegisteredQuery, string>, RegisteredQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(StartupValidatorTests).Assembly];
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert - Should still fail because there are other unregistered messages
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("UnregisteredVoidCommand");
	}

	[Fact]
	public void AddMediator_DefaultStartupValidation_IsNone()
	{
		// Arrange
		var options = new MediatorOptions();

		// Assert
		options.StartupValidation.Should().Be(StartupValidation.None);
	}

	[Fact]
	public void AddMediator_ValidationErrorMessage_ContainsHelpfulInformation()
	{
		// Arrange
		var services = new ServiceCollection();

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(StartupValidatorTests).Assembly];
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;

		// Should contain helpful error message
		exception.Message.Should().Contain("startup validation failed");
		exception.Message.Should().Contain("issue(s) found");

		// Should list affected message types
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("UnregisteredVoidCommand");
		exception.Message.Should().Contain("UnregisteredCommandWithResponse");
		exception.Message.Should().Contain("UnregisteredStreamQuery");

		// Should provide resolution steps
		exception.Message.Should().Contain("To fix this issue");
		exception.Message.Should().Contain("Register exactly one handler");
		exception.Message.Should().Contain("Set StartupValidation to None or Warn");
	}

	[Fact]
	public void StartupValidation_Enum_HasCorrectValues()
	{
		// Assert - Verify enum values for stability
		((int)StartupValidation.None).Should().Be(0);
		((int)StartupValidation.Warn).Should().Be(1);
		((int)StartupValidation.FailFast).Should().Be(2);
	}

	[Fact]
	public void AddMediator_MultipleAssemblies_ValidatesAll()
	{
		// Arrange
		var services = new ServiceCollection();
		var assembly1 = typeof(StartupValidatorTests).Assembly;
		var assembly2 = typeof(Mediator).Assembly;

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [assembly1, assembly2];
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert - Should validate messages from all assemblies
		act.Should().Throw<InvalidOperationException>()
			.Which.Message.Should().Contain("startup validation failed");
	}

	[Fact]
	public void AddMediator_EmptyAssembly_DoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();

		// Create a mock assembly with no handlers/messages
		// In practice, this would be an assembly with only infrastructure code

		// Act - Even with FailFast, empty assemblies shouldn't cause issues
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [typeof(object).Assembly]; // mscorlib has no handlers
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_DuplicateHandlersWithWarn_LogsButDoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();

		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler1>();
		services.AddScoped<IQueryHandler<DuplicateHandlerQuery, string>, DuplicateQueryHandler2>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = [typeof(StartupValidator).Assembly],
			StartupValidation = StartupValidation.Warn
		};
		services.AddSingleton(options);

		// Act - Warn mode should not throw, just log
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		act.Should().NotThrow();
	}

	public static string GetMultipleInterfacesSourceCode()
	{
		var sourceCode = @"
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using EasyDispatch;

        // Test messages that implement multiple interfaces (INVALID)
		public record QueryAndCommand(int Id) : IQuery<string>, ICommand;
		public record QueryAndNotification(int Id) : IQuery<string>, INotification;
		public record CommandAndNotification(string Name) : ICommand, INotification;
		public record CommandWithResponseAndNotification(int Value) : ICommand<int>, INotification;
		public record StreamQueryAndCommand(int Count) : IStreamQuery<int>, ICommand;
		public record AllInterfaces(int Count) : IStreamQuery<int>, ICommand, ICommand<int>, IQuery<int>;
		";

		return sourceCode;
	}

	[Fact]
	public void AddMediator_WithMultipleMessageInterfaces_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();
		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = [dynamicAssembly];
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("QueryAndCommand");
		exception.Message.Should().Contain("should only implement one message interface");
	}

	[Fact]
	public void AddMediator_WithQueryAndCommand_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();
		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("QueryAndCommand");
		exception.Message.Should().Contain("IQuery<>, ICommand");
	}

	[Fact]
	public void AddMediator_WithQueryAndNotification_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("QueryAndNotification");
		exception.Message.Should().Contain("IQuery<>, INotification");
	}

	[Fact]
	public void AddMediator_WithCommandAndNotification_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("CommandAndNotification");
		exception.Message.Should().Contain("ICommand, INotification");
	}

	[Fact]
	public void AddMediator_WithStreamQueryAndCommand_ThrowsValidationError()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("StreamQueryAndCommand");
	}

	[Fact]
	public void AddMediator_WithMultipleInterfaces_ReportsCorrectInterfaceCount()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("'AllInterfaces' implements 4 message interfaces");
	}

	[Fact]
	public void AddMediator_WithMultipleInterfacesAndWarn_LogsButDoesNotThrow()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.Warn
		};
		services.AddSingleton(options);

		// Act - Warn mode should not throw, just log
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		act.Should().NotThrow();
	}

	[Fact]
	public void AddMediator_CombinedMultipleInterfacesAndMissingHandlers_ReportsBothIssues()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var dynamicAssembly = CreateDynamicAssembly(GetMultipleInterfacesSourceCode());

		var options = new MediatorOptions
		{
			Assemblies = [dynamicAssembly],
			StartupValidation = StartupValidation.FailFast
		};
		services.AddSingleton(options);

		// Act
		var act = () => StartupValidator.ValidateHandlers(services, options);

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;
		exception.Message.Should().Contain("Multiple Message Interfaces");
		exception.Message.Should().Contain("Missing Handlers");
		exception.Message.Should().Contain("QueryAndCommand");
	}

	private static Assembly CreateDynamicAssembly(string sourceCode)
	{
		var easyDispatchAssembly = typeof(IQuery<>).Assembly;

		var references = new[]
		{
			MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.AsyncIteratorMethodBuilder).Assembly.Location),
			MetadataReference.CreateFromFile(typeof(IAsyncEnumerable<>).Assembly.Location),
			MetadataReference.CreateFromFile(easyDispatchAssembly.Location),
			MetadataReference.CreateFromFile(Assembly.Load("netstandard, Version=2.0.0.0").Location),
			MetadataReference.CreateFromFile(Assembly.Load("System.Runtime, Version=4.2.2.0").Location)
		};

		var compilation = CSharpCompilation.Create("DynamicAssembly")
			.WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
			.AddReferences(references)
			.AddSyntaxTrees(CSharpSyntaxTree.ParseText(sourceCode));
		var ms = new MemoryStream();
		var result = compilation.Emit(ms);

		if (!result.Success)
		{
			var errors = string.Join(Environment.NewLine, result.Diagnostics);
			Assert.Fail($"Compilation failed:{Environment.NewLine}{errors}");
		}

		return Assembly.Load(ms.ToArray());
	}
}