using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace EasyDispatch.IntegrationTests;

/// <summary>
/// Integration tests for startup validation behavior.
/// </summary>
public class StartupValidationIntegrationTests
{
	[Fact]
	public void Startup_WithFailFast_PreventsApplicationStartup()
	{
		// Arrange - Create a scenario that would fail in production startup
		// We create a dummy assembly in memory with a message that has no handler
		var sourceCode = @"public record NoHandlerQuery(int Id) : EasyDispatch.IQuery<string>;";

		var assembly = CreateDynamicAssembly(sourceCode);

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

	[Theory]
	[InlineData(StartupValidation.None)]
	[InlineData(StartupValidation.Warn)]
	[InlineData(StartupValidation.FailFast)]
	public async Task RuntimeExecution_WithValidHandlers_WorksRegardlessOfValidationMode(StartupValidation mode)
	{
		// Arrange
		var sourceCode = @"
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        
        public record ValidQuery(int Id) : EasyDispatch.IQuery<string>;
        public record ValidCommand(string Name) : EasyDispatch.ICommand;
        public record ValidStreamQuery(int Count) : EasyDispatch.IStreamQuery<int>;

        public class ValidQueryHandler : EasyDispatch.IQueryHandler<ValidQuery, string>
        {
            public Task<string> Handle(ValidQuery query, CancellationToken cancellationToken)
            {
                return Task.FromResult($""Result: {query.Id}"");
            }
        }

        public class ValidCommandHandler : EasyDispatch.ICommandHandler<ValidCommand>
        {
            public Task Handle(ValidCommand command, CancellationToken cancellationToken)
            {
                return Task.CompletedTask;
            }
        }

        public class ValidStreamQueryHandler : EasyDispatch.IStreamQueryHandler<ValidStreamQuery, int>
        {
            public async IAsyncEnumerable<int> Handle(
                ValidStreamQuery query,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
            {
                for (int i = 1; i <= query.Count; i++)
                {
                    await Task.Delay(1, cancellationToken);
                    yield return i;
                }
            }
        }";

		var dynamicAssembly = CreateDynamicAssembly(sourceCode);

		var services = new ServiceCollection();
		services.AddMediator(options =>
		{
			options.Assemblies = [dynamicAssembly];
			options.StartupValidation = mode;
		});

		using var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Create instances using reflection since types are dynamic
		var validQueryType = dynamicAssembly.GetType("ValidQuery");
		var validCommandType = dynamicAssembly.GetType("ValidCommand");
		var validStreamQueryType = dynamicAssembly.GetType("ValidStreamQuery");

		validQueryType.Should().NotBeNull();
		validCommandType.Should().NotBeNull();
		validStreamQueryType.Should().NotBeNull();

		var queryInstance = Activator.CreateInstance(validQueryType, 42);
		var commandInstance = Activator.CreateInstance(validCommandType, "test");
		var streamQueryInstance = Activator.CreateInstance(validStreamQueryType, 3);

		dynamic dynamicMediator = mediator;

		// Query
		var queryResult = await dynamicMediator.SendAsync((dynamic?)queryInstance);

		// Command  
		await dynamicMediator.SendAsync((dynamic?)commandInstance);

		// Stream
		var streamResults = new List<int>();
		var asyncEnumerable = dynamicMediator.StreamAsync((dynamic?)streamQueryInstance) as IAsyncEnumerable<int>;
		asyncEnumerable.Should().NotBeNull();
		await foreach (var item in asyncEnumerable)
		{
			streamResults.Add(item);
		}

		// Assert
		(queryResult as string).Should().Be("Result: 42");
		streamResults.Should().Equal(1, 2, 3);
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