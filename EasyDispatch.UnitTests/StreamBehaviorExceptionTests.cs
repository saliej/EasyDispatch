using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for exception handling in stream behaviors.
/// </summary>
public class StreamBehaviorExceptionTests
{
	public record TestStreamQuery(int Count) : IStreamQuery<int>;

	public class TestStreamQueryHandler : IStreamQueryHandler<TestStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			TestStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			for (int i = 1; i <= query.Count; i++)
			{
				await Task.Delay(1, cancellationToken);
				yield return i;
			}
		}
	}

	public class ThrowingStreamBehavior : IStreamPipelineBehavior<TestStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			TestStreamQuery query,
			Func<IAsyncEnumerable<int>> next,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			// Throw before delegating to next
			throw new InvalidOperationException("Stream behavior intentionally failed");

			// Required to satisfy the compiler, but will never be reached
			yield return 1;
		}
	}

	[Fact]
	public async Task SendStreamAsync_WithThrowingBehavior_PropagatesException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<TestStreamQuery, int>, TestStreamQueryHandler>();
		services.AddScoped<IStreamPipelineBehavior<TestStreamQuery, int>, ThrowingStreamBehavior>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new TestStreamQuery(5);

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				// Should not reach here
			}
		};

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Stream behavior intentionally failed");
	}

	[Fact]
	public async Task SendStreamAsync_WithThrowingBehavior_UnwrapsTargetInvocationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<TestStreamQuery, int>, TestStreamQueryHandler>();
		services.AddScoped<IStreamPipelineBehavior<TestStreamQuery, int>, ThrowingStreamBehavior>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new TestStreamQuery(5);

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				// Should not reach here
			}
		};

		// Assert
		// Should throw the inner exception, not TargetInvocationException
		var exception = await act.Should().ThrowAsync<InvalidOperationException>();
		exception.Which.Should().NotBeOfType<System.Reflection.TargetInvocationException>();
		exception.Which.Message.Should().Be("Stream behavior intentionally failed");
	}
}