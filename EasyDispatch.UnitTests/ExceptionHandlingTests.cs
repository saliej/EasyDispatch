using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for exception handling and error scenarios in Mediator.
/// </summary>
public class ExceptionHandlingTests
{
	// Test messages
	public record ThrowingQuery(int Id) : IQuery<string>;
	public record ThrowingCommand(string Name) : ICommand;
	public record ThrowingCommandWithResponse(int Value) : ICommand<int>;
	public record ThrowingStreamQuery(int Count) : IStreamQuery<int>;

	// Handlers that throw exceptions
	public class ThrowingQueryHandler : IQueryHandler<ThrowingQuery, string>
	{
		public Task<string> Handle(ThrowingQuery query, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException($"Query handler failed with ID: {query.Id}");
		}
	}

	public class ThrowingCommandHandler : ICommandHandler<ThrowingCommand>
	{
		public Task Handle(ThrowingCommand command, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException($"Command handler failed: {command.Name}");
		}
	}

	public class ThrowingCommandWithResponseHandler : ICommandHandler<ThrowingCommandWithResponse, int>
	{
		public Task<int> Handle(ThrowingCommandWithResponse command, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException($"Command handler failed with value: {command.Value}");
		}
	}

	public class ThrowingStreamQueryHandler : IStreamQueryHandler<ThrowingStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			ThrowingStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.Yield();
			throw new InvalidOperationException("Stream query handler failed");

			// Required to satisfy the compiler, but will never be reached
			yield break;
		}
	}

	[Fact]
	public async Task SendAsync_Query_PropagatesExceptionFromHandler()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IQueryHandler<ThrowingQuery, string>, ThrowingQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new ThrowingQuery(42);

		// Act
		var act = async () => await mediator.SendAsync(query);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Query handler failed with ID: 42");
	}

	[Fact]
	public async Task SendAsync_VoidCommand_PropagatesExceptionFromHandler()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<ThrowingCommand>, ThrowingCommandHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var command = new ThrowingCommand("test");

		// Act
		var act = async () => await mediator.SendAsync(command);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Command handler failed: test");
	}

	[Fact]
	public async Task SendAsync_CommandWithResponse_PropagatesExceptionFromHandler()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<ThrowingCommandWithResponse, int>, ThrowingCommandWithResponseHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var command = new ThrowingCommandWithResponse(99);

		// Act
		var act = async () => await mediator.SendAsync(command);

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Command handler failed with value: 99");
	}

	[Fact]
	public async Task SendStreamAsync_PropagatesExceptionFromHandler()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<ThrowingStreamQuery, int>, ThrowingStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new ThrowingStreamQuery(5);

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				// Should throw before yielding any items
			}
		};

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Stream query handler failed");
	}

	[Fact]
	public async Task SendAsync_Query_UnwrapsTargetInvocationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IQueryHandler<ThrowingQuery, string>, ThrowingQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new ThrowingQuery(42);

		// Act
		var act = async () => await mediator.SendAsync(query);

		// Assert
		// Should throw the inner exception, not TargetInvocationException
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();
		exception.Which.Should().NotBeOfType<System.Reflection.TargetInvocationException>();
		exception.Which.Message.Should().Be("Query handler failed with ID: 42");
	}

	[Fact]
	public async Task SendAsync_Command_UnwrapsTargetInvocationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<ThrowingCommand>, ThrowingCommandHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var command = new ThrowingCommand("test");

		// Act
		var act = async () => await mediator.SendAsync(command);

		// Assert
		// Should throw the inner exception, not TargetInvocationException
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();
		exception.Which.Should().NotBeOfType<System.Reflection.TargetInvocationException>();
		exception.Which.Message.Should().Be("Command handler failed: test");
	}

	[Fact]
	public async Task SendAsync_CommandWithResponse_UnwrapsTargetInvocationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<ThrowingCommandWithResponse, int>, ThrowingCommandWithResponseHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var command = new ThrowingCommandWithResponse(99);

		// Act
		var act = async () => await mediator.SendAsync(command);

		// Assert
		// Should throw the inner exception, not TargetInvocationException
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();
		exception.Which.Should().NotBeOfType<System.Reflection.TargetInvocationException>();
		exception.Which.Message.Should().Be("Command handler failed with value: 99");
	}

	[Fact]
	public async Task SendStreamAsync_UnwrapsTargetInvocationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<ThrowingStreamQuery, int>, ThrowingStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new ThrowingStreamQuery(5);

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				// Should throw
			}
		};

		// Assert
		// Should throw the inner exception, not TargetInvocationException
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();
		exception.Which.Should().NotBeOfType<System.Reflection.TargetInvocationException>();
		exception.Which.Message.Should().Be("Stream query handler failed");
	}
}