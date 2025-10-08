using EasyDispatch;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

/// <summary>
/// Integration tests for startup validation behavior.
/// </summary>
public class StartupValidationIntegrationTests
{
	public record ValidQuery(int Id) : IQuery<string>;
	public record ValidCommand(string Name) : ICommand;
	public class ValidQueryHandler : IQueryHandler<ValidQuery, string>
	{
		public Task<string> Handle(ValidQuery query, CancellationToken cancellationToken)
		{
			return Task.FromResult($"Result: {query.Id}");
		}
	}

	public class ValidCommandHandler : ICommandHandler<ValidCommand>
	{
		public Task Handle(ValidCommand command, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	[Fact]
	public async Task RuntimeExecution_WithValidHandlers_WorksRegardlessOfValidationMode()
	{
		// Test with None
		await TestWithValidationMode(StartupValidation.None);

		// Test with Warn
		await TestWithValidationMode(StartupValidation.Warn);

		// Test with FailFast
		await TestWithValidationMode(StartupValidation.FailFast);
	}

	private async Task TestWithValidationMode(StartupValidation mode)
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(options =>
		{
			options.Assemblies = new[] { typeof(StartupValidationIntegrationTests).Assembly };
			options.StartupValidation = mode;
		});

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		var queryResult = await mediator.SendAsync(new ValidQuery(42));
		await mediator.SendAsync(new ValidCommand("test"));

		// Assert
		queryResult.Should().Be("Result: 42");
	}

	[Fact]
	public void Startup_WithFailFast_PreventsApplicationStartup()
	{
		// Arrange - Create a scenario that would fail in production startup
		// We create a dummy assembly in memory with a message that has no handler
		var sourceCode = @"public record NoHandlerQuery(int Id) : EasyDispatch.IQuery<string>;";

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

		// Act
		var act = () =>
		{
			var services = new ServiceCollection();
			services.AddMediator(options =>
			{
				options.Assemblies = [assembly];
				options.StartupValidation = StartupValidation.FailFast;
			});

			// This would be called during app startup
			services.BuildServiceProvider();
		};

		// Assert - Application should fail to start
		act.Should().Throw<InvalidOperationException>()
			.WithMessage("*startup validation failed*");
	}
}