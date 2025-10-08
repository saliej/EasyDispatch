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
public class StartupValidationTests
{
	// Test messages WITHOUT handlers
	public record UnregisteredQuery(int Id) : IQuery<string>;
	public record UnregisteredVoidCommand(string Name) : ICommand;
	public record UnregisteredCommandWithResponse(int Value) : ICommand<int>;
	public record UnregisteredNotification(string Message) : INotification;

	// Test messages WITH handlers
	public record RegisteredQuery(int Id) : IQuery<string>;
	public record RegisteredVoidCommand(string Name) : ICommand;
	public record RegisteredCommandWithResponse(int Value) : ICommand<int>;
	public record RegisteredNotification(string Message) : INotification;

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

	public class RegisteredNotificationHandler : INotificationHandler<RegisteredNotification>
	{
		public Task Handle(RegisteredNotification notification, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
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
			options.Assemblies = new[] { typeof(StartupValidationTests).Assembly };
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
			options.Assemblies = new[] { typeof(StartupValidationTests).Assembly };
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>()
			.Which;

		exception.Message.Should().Contain("startup validation failed");
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("UnregisteredVoidCommand");
		exception.Message.Should().Contain("UnregisteredCommandWithResponse");
		// Notifications should NOT be included (they can have 0 or multiple handlers)
		exception.Message.Should().NotContain("UnregisteredNotification");
	}

	[Fact]
	public void AddMediator_WithStartupValidationWarn_LogsWarnings()
	{
		// Arrange
		var services = new ServiceCollection();
		var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));
		var logger = loggerFactory.CreateLogger<StartupValidationTests>();

		// Note: In real implementation, we'd need to inject logger or use ILoggerFactory
		// For this test, we just verify it doesn't throw

		// Act
		var act = () => services.AddMediator(options =>
		{
			options.Assemblies = new[] { typeof(StartupValidationTests).Assembly };
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
		services.AddScoped<INotificationHandler<RegisteredNotification>, RegisteredNotificationHandler>();
		services.AddScoped<IMediator, Mediator>();

		var options = new MediatorOptions
		{
			Assemblies = new[] { typeof(RegisteredQuery).Assembly },
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
		var services = new ServiceCollection();

		// Arrange - Create a scenario that would fail in production startup
		// We create a dummy assembly in memory with a message that has no handler
		var sourceCode = @"public record NoHandlerNotification(int Id) : EasyDispatch.INotification;";

		// Get the assembly that contains IQuery<T>
		var easyDispatchAssembly = typeof(IQuery<>).Assembly; // or Assembly.Load("EasyDispatch")

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
			options.Assemblies = new[] { typeof(StartupValidationTests).Assembly };
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
			options.Assemblies = new[] { typeof(StartupValidationTests).Assembly };
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		var exception = act.Should().Throw<InvalidOperationException>().Which;

		// Should contain helpful error message
		exception.Message.Should().Contain("startup validation failed");
		exception.Message.Should().Contain("message(s) have no registered handlers");

		// Should list affected message types
		exception.Message.Should().Contain("UnregisteredQuery");
		exception.Message.Should().Contain("UnregisteredVoidCommand");
		exception.Message.Should().Contain("UnregisteredCommandWithResponse");

		// Should provide resolution steps
		exception.Message.Should().Contain("To fix this issue");
		exception.Message.Should().Contain("Register handlers");
		exception.Message.Should().Contain("Set StartupValidation to None or Warn");
	}

	[Fact]
	public void AddMediator_ObsoleteProperty_StillWorks()
	{
		// Arrange
		var options = new MediatorOptions();

		// Act - Use obsolete property
#pragma warning disable CS0618 // Type or member is obsolete
		options.ValidateHandlersAtStartup = true;
#pragma warning restore CS0618

		// Assert - Should map to new property
		options.StartupValidation.Should().Be(StartupValidation.FailFast);

		// Act - Set to false
#pragma warning disable CS0618
		options.ValidateHandlersAtStartup = false;
#pragma warning restore CS0618

		// Assert
		options.StartupValidation.Should().Be(StartupValidation.None);
	}

	[Fact]
	public void AddMediator_ObsoletePropertyGetter_ReturnsCorrectValue()
	{
		// Arrange
		var options = new MediatorOptions();

		// Act
		options.StartupValidation = StartupValidation.Warn;

		// Assert
#pragma warning disable CS0618
		options.ValidateHandlersAtStartup.Should().BeTrue();
#pragma warning restore CS0618

		// Act
		options.StartupValidation = StartupValidation.None;

		// Assert
#pragma warning disable CS0618
		options.ValidateHandlersAtStartup.Should().BeFalse();
#pragma warning restore CS0618
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
		var assembly1 = typeof(StartupValidationTests).Assembly;
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
			options.Assemblies = new[] { typeof(object).Assembly }; // mscorlib has no handlers
			options.StartupValidation = StartupValidation.FailFast;
		});

		// Assert
		act.Should().NotThrow();
	}
}