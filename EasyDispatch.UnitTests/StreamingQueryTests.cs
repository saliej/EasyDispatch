using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for streaming query functionality.
/// </summary>
public class StreamingQueryTests
{
	public record GetNumbersStreamQuery(int Count) : IStreamQuery<int>;
	public record GetLargeDatasetQuery(int PageSize) : IStreamQuery<string>;

	public class GetNumbersStreamQueryHandler : IStreamQueryHandler<GetNumbersStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			GetNumbersStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			for (int i = 1; i <= query.Count; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Delay(1, cancellationToken); // Simulate async work
				yield return i;
			}
		}
	}

	public class GetLargeDatasetQueryHandler : IStreamQueryHandler<GetLargeDatasetQuery, string>
	{
		public async IAsyncEnumerable<string> Handle(
			GetLargeDatasetQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			int totalItems = query.PageSize * 3; // 3 pages worth
			for (int i = 1; i <= totalItems; i++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				await Task.Delay(1, cancellationToken);
				yield return $"Item {i}";
			}
		}
	}

	[Fact]
	public async Task StreamAsync_BasicStream_ReturnsAllItems()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, GetNumbersStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(5);

		// Act
		var results = new List<int>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().Equal(1, 2, 3, 4, 5);
	}

	[Fact]
	public async Task StreamAsync_LargeDataset_StreamsIncrementally()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingQueryTests).Assembly);

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetLargeDatasetQuery(10);

		// Act
		var results = new List<string>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().HaveCount(30); // 3 pages * 10 items
		results[0].Should().Be("Item 1");
		results[29].Should().Be("Item 30");
	}

	[Fact]
	public async Task StreamAsync_WithCancellation_StopsStreaming()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, GetNumbersStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(100);
		var cts = new CancellationTokenSource();

		// Act
		var results = new List<int>();
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query, cts.Token))
			{
				results.Add(item);
				if (results.Count == 5)
				{
					cts.Cancel();
				}
			}
		};

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>();
		results.Should().HaveCount(5);
	}

	[Fact]
	public async Task StreamAsync_MissingHandler_ThrowsInvalidOperationException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(5);

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
			.WithMessage("*No handler registered for streaming query*");
	}

	[Fact]
	public async Task StreamAsync_NullQuery_ThrowsArgumentNullException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingQueryTests).Assembly);

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync<int>(null!))
			{
				// Should not reach here
			}
		};

		// Assert
		await act.Should().ThrowAsync<ArgumentNullException>()
			.WithParameterName("query");
	}

	[Fact]
	public async Task StreamAsync_EmptyStream_CompletesSuccessfully()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, EmptyStreamHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(0);

		// Act
		var results = new List<int>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().BeEmpty();
	}

	[Fact]
	public async Task StreamAsync_HandlerThrowsException_PropagatesException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, FailingStreamHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(5);

		// Act
		var results = new List<int>();
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				results.Add(item);
			}
		};

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Stream handler failed");
		results.Should().HaveCount(2); // Should have received 2 items before failure
	}

	[Fact]
	public async Task StreamAsync_MultipleConsumers_EachGetsOwnStream()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, GetNumbersStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(3);

		// Act - Consume the same query twice
		var results1 = new List<int>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results1.Add(item);
		}

		var results2 = new List<int>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results2.Add(item);
		}

		// Assert - Both consumers should get all items
		results1.Should().Equal(1, 2, 3);
		results2.Should().Equal(1, 2, 3);
	}

	[Fact]
	public async Task StreamAsync_WithLinqOperations_WorksCorrectly()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddScoped<IStreamQueryHandler<GetNumbersStreamQuery, int>, GetNumbersStreamQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetNumbersStreamQuery(10);

		// Act - Use LINQ operations on the stream
		var results = new List<int>();
		await foreach (var item in mediator.StreamAsync(query).Where(x => x % 2 == 0))
		{
			results.Add(item);
		}

		// Assert
		results.Should().Equal(2, 4, 6, 8, 10);
	}

	// Helper handlers
	private class EmptyStreamHandler : IStreamQueryHandler<GetNumbersStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			GetNumbersStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await Task.CompletedTask;
			yield break; // Return empty stream
		}
	}

	private class FailingStreamHandler : IStreamQueryHandler<GetNumbersStreamQuery, int>
	{
		public async IAsyncEnumerable<int> Handle(
			GetNumbersStreamQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			yield return 1;
			await Task.Delay(1, cancellationToken);
			yield return 2;
			await Task.Delay(1, cancellationToken);
			throw new InvalidOperationException("Stream handler failed");
		}
	}
}

/// <summary>
/// Tests for streaming query with pipeline behaviors.
/// </summary>
public class StreamingBehaviorTests
{
	public record GetItemsQuery(int Count) : IStreamQuery<string>;

	public class GetItemsQueryHandler : IStreamQueryHandler<GetItemsQuery, string>
	{
		public async IAsyncEnumerable<string> Handle(
			GetItemsQuery query,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			for (int i = 1; i <= query.Count; i++)
			{
				await Task.Delay(1, cancellationToken);
				yield return $"Item {i}";
			}
		}
	}

	public class LoggingStreamBehavior<TQuery, TResult> : IStreamPipelineBehavior<TQuery, TResult>
	{
		public List<string> Logs { get; } = new();

		public async IAsyncEnumerable<TResult> Handle(
			TQuery query,
			Func<IAsyncEnumerable<TResult>> next,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			Logs.Add($"Before streaming {typeof(TQuery).Name}");

			await foreach (var item in next().WithCancellation(cancellationToken))
			{
				Logs.Add($"Item: {item}");
				yield return item;
			}

			Logs.Add($"After streaming {typeof(TQuery).Name}");
		}
	}

	public class FilterStreamBehavior : IStreamPipelineBehavior<GetItemsQuery, string>
	{
		public async IAsyncEnumerable<string> Handle(
			GetItemsQuery query,
			Func<IAsyncEnumerable<string>> next,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var item in next().WithCancellation(cancellationToken))
			{
				// Filter out items containing "3"
				if (!item.Contains("3"))
				{
					yield return item;
				}
			}
		}
	}

	public class TransformStreamBehavior : IStreamPipelineBehavior<GetItemsQuery, string>
	{
		public async IAsyncEnumerable<string> Handle(
			GetItemsQuery query,
			Func<IAsyncEnumerable<string>> next,
			[System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var item in next().WithCancellation(cancellationToken))
			{
				yield return item.ToUpper();
			}
		}
	}

	[Fact]
	public async Task StreamAsync_WithLoggingBehavior_LogsAllItems()
	{
		// Arrange
		var loggingBehavior = new LoggingStreamBehavior<GetItemsQuery, string>();

		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingBehaviorTests).Assembly)
			.AddStreamBehavior(loggingBehavior);

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		var query = new GetItemsQuery(3);

		// Act
		var results = new List<string>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().Equal("Item 1", "Item 2", "Item 3");
		loggingBehavior.Logs.Should().Contain("Before streaming GetItemsQuery");
		loggingBehavior.Logs.Should().Contain("Item: Item 1");
		loggingBehavior.Logs.Should().Contain("Item: Item 2");
		loggingBehavior.Logs.Should().Contain("Item: Item 3");
		loggingBehavior.Logs.Should().Contain("After streaming GetItemsQuery");
	}

	[Fact]
	public async Task StreamAsync_WithFilterBehavior_FiltersItems()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingBehaviorTests).Assembly)
			.AddStreamBehavior<GetItemsQuery, string, FilterStreamBehavior>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new GetItemsQuery(5);

		// Act
		var results = new List<string>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().NotContain("Item 3");
		results.Should().Equal("Item 1", "Item 2", "Item 4", "Item 5");
	}

	[Fact]
	public async Task StreamAsync_WithTransformBehavior_TransformsItems()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingBehaviorTests).Assembly)
			.AddStreamBehavior<GetItemsQuery, string, TransformStreamBehavior>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new GetItemsQuery(3);

		// Act
		var results = new List<string>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert
		results.Should().Equal("ITEM 1", "ITEM 2", "ITEM 3");
	}

	[Fact]
	public async Task StreamAsync_WithMultipleBehaviors_ExecutesInOrder()
	{
		// Arrange - Behaviors execute in registration order
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingBehaviorTests).Assembly)
			.AddStreamBehavior<GetItemsQuery, string, TransformStreamBehavior>()
			.AddStreamBehavior<GetItemsQuery, string, FilterStreamBehavior>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new GetItemsQuery(5);

		// Act
		var results = new List<string>();
		await foreach (var item in mediator.StreamAsync(query))
		{
			results.Add(item);
		}

		// Assert - Transform happens first (uppercase), then filter
		// "ITEM 3" should be filtered out
		results.Should().NotContain("ITEM 3");
		results.Should().Equal("ITEM 1", "ITEM 2", "ITEM 4", "ITEM 5");
	}

	[Fact]
	public async Task StreamAsync_BehaviorThrowsException_PropagatesException()
	{
		// Arrange
		var services = new ServiceCollection();
		services.AddMediator(typeof(StreamingBehaviorTests).Assembly)
			.AddStreamBehavior<GetItemsQuery, string, ThrowingStreamBehavior>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		var query = new GetItemsQuery(3);

		// Act
		var act = async () =>
		{
			await foreach (var item in mediator.StreamAsync(query))
			{
				// Should not process any items
			}
		};

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>()
			.WithMessage("Behavior failed");
	}

	private class ThrowingStreamBehavior : IStreamPipelineBehavior<GetItemsQuery, string>
	{
		public IAsyncEnumerable<string> Handle(
			GetItemsQuery query,
			Func<IAsyncEnumerable<string>> next,
			CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("Behavior failed");
		}
	}
}