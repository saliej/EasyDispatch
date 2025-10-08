using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for polymorphic notification dispatch where handlers for base types
/// and interfaces are also invoked when publishing derived notifications.
/// </summary>
public class PolymorphicDispatchTests
{
	// Base notification types
	public record BaseNotification(string Message) : INotification;
	public record DerivedNotification(string Message, int Value) : BaseNotification(Message);
	public record DoublyDerivedNotification(string Message, int Value, bool Flag) : DerivedNotification(Message, Value);

	// Interface-based notifications
	public interface ITaggedNotification : INotification
	{
		string Tag { get; }
	}

	public record TaggedNotification(string Tag, string Data) : INotification, ITaggedNotification;
	public record SpecialTaggedNotification(string Tag, string Data, int Priority) : TaggedNotification(Tag, Data);

	// Handlers
	private class BaseNotificationHandler : INotificationHandler<BaseNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(BaseNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"Base: {notification.Message}");
			return Task.CompletedTask;
		}
	}

	private class DerivedNotificationHandler : INotificationHandler<DerivedNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(DerivedNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"Derived: {notification.Message} ({notification.Value})");
			return Task.CompletedTask;
		}
	}

	private class DoublyDerivedNotificationHandler : INotificationHandler<DoublyDerivedNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(DoublyDerivedNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"DoublyDerived: {notification.Message} ({notification.Value}, {notification.Flag})");
			return Task.CompletedTask;
		}
	}

	private class TaggedNotificationHandler : INotificationHandler<ITaggedNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(ITaggedNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"Tagged: {notification.Tag}");
			return Task.CompletedTask;
		}
	}

	private class ConcreteTaggedHandler : INotificationHandler<TaggedNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(TaggedNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"ConcreteTagged: {notification.Tag} - {notification.Data}");
			return Task.CompletedTask;
		}
	}

	private class SpecialTaggedHandler : INotificationHandler<SpecialTaggedNotification>
	{
		public List<string> ReceivedMessages { get; } = [];

		public Task Handle(SpecialTaggedNotification notification, CancellationToken cancellationToken)
		{
			ReceivedMessages.Add($"SpecialTagged: {notification.Tag} (Priority: {notification.Priority})");
			return Task.CompletedTask;
		}
	}

	[Fact]
	public async Task PublishAsync_DerivedNotification_InvokesBaseAndDerivedHandlers()
	{
		// Arrange
		var baseHandler = new BaseNotificationHandler();
		var derivedHandler = new DerivedNotificationHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler);
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new DerivedNotification("Test", 42));

		// Assert
		baseHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Base: Test");
		derivedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Derived: Test (42)");
	}

	[Fact]
	public async Task PublishAsync_BaseNotification_InvokesOnlyBaseHandler()
	{
		// Arrange
		var baseHandler = new BaseNotificationHandler();
		var derivedHandler = new DerivedNotificationHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler);
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new BaseNotification("Test"));

		// Assert
		baseHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Base: Test");
		derivedHandler.ReceivedMessages.Should().BeEmpty();
	}

	[Fact]
	public async Task PublishAsync_DoublyDerived_InvokesAllHandlersInHierarchy()
	{
		// Arrange
		var baseHandler = new BaseNotificationHandler();
		var derivedHandler = new DerivedNotificationHandler();
		var doublyDerivedHandler = new DoublyDerivedNotificationHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler);
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);
		services.AddSingleton<INotificationHandler<DoublyDerivedNotification>>(doublyDerivedHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new DoublyDerivedNotification("Test", 42, true));

		// Assert - All three handlers should be invoked
		baseHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Base: Test");
		derivedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Derived: Test (42)");
		doublyDerivedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("DoublyDerived: Test (42, True)");
	}

	[Fact]
	public async Task PublishAsync_InterfaceImplementation_InvokesInterfaceAndConcreteHandlers()
	{
		// Arrange
		var taggedHandler = new TaggedNotificationHandler();
		var concreteHandler = new ConcreteTaggedHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<ITaggedNotification>>(taggedHandler);
		services.AddSingleton<INotificationHandler<TaggedNotification>>(concreteHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new TaggedNotification("urgent", "data"));

		// Assert
		taggedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Tagged: urgent");
		concreteHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("ConcreteTagged: urgent - data");
	}

	[Fact]
	public async Task PublishAsync_DerivedFromInterfaceImplementation_InvokesAllHandlers()
	{
		// Arrange
		var taggedHandler = new TaggedNotificationHandler();
		var concreteHandler = new ConcreteTaggedHandler();
		var specialHandler = new SpecialTaggedHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<ITaggedNotification>>(taggedHandler);
		services.AddSingleton<INotificationHandler<TaggedNotification>>(concreteHandler);
		services.AddSingleton<INotificationHandler<SpecialTaggedNotification>>(specialHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new SpecialTaggedNotification("critical", "important", 10));

		// Assert - All three handlers should be invoked
		taggedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Tagged: critical");
		concreteHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("ConcreteTagged: critical - important");
		specialHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("SpecialTagged: critical (Priority: 10)");
	}

	[Fact]
	public async Task PublishAsync_MultipleHandlersForSameType_AllAreInvoked()
	{
		// Arrange
		var baseHandler1 = new BaseNotificationHandler();
		var baseHandler2 = new BaseNotificationHandler();
		var derivedHandler = new DerivedNotificationHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler1);
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler2);
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new DerivedNotification("Test", 99));

		// Assert - Both base handlers + derived handler should execute
		baseHandler1.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Base: Test");
		baseHandler2.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Base: Test");
		derivedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Derived: Test (99)");
	}

	[Fact]
	public async Task PublishAsync_WithContinueOnException_ExecutesAllHandlersEvenWhenBaseFails()
	{
		// Arrange
		var derivedHandler = new DerivedNotificationHandler();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(new FailingHandler());
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PolymorphicDispatchTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.ContinueOnException
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		var act = async () => await mediator.PublishAsync(new DerivedNotification("Test", 42));

		// Assert
		var exception = await act.Should().ThrowAsync<AggregateException>();
		exception.Which.InnerExceptions.Should().ContainSingle()
			.Which.Message.Should().Contain("Handler failed");

		// Derived handler should still execute despite base handler failure
		derivedHandler.ReceivedMessages.Should().ContainSingle()
			.Which.Should().Be("Derived: Test (42)");
	}

	[Fact]
	public async Task PublishAsync_WithParallelWhenAll_ExecutesAllHandlersInParallel()
	{
		// Arrange
		var executionTimes = new List<DateTime>();

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(
			new DelayedHandler(executionTimes, TimeSpan.FromMilliseconds(50)));
		services.AddSingleton<INotificationHandler<DerivedNotification>>(
			new DerivedDelayedHandler(executionTimes, TimeSpan.FromMilliseconds(50)));

		var options = new MediatorOptions
		{
			Assemblies = [typeof(PolymorphicDispatchTests).Assembly],
			NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll
		};
		services.AddSingleton(options);
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act
		var startTime = DateTime.UtcNow;
		await mediator.PublishAsync(new DerivedNotification("Test", 1));
		var totalTime = DateTime.UtcNow - startTime;

		// Assert - Should complete in ~50ms (parallel), not 100ms (sequential)
		totalTime.Should().BeLessThan(TimeSpan.FromMilliseconds(120));
		executionTimes.Should().HaveCount(2);
	}

	[Fact]
	public async Task PublishAsync_WithBehaviors_AppliesBehaviorsToAllPolymorphicHandlers()
	{
		// Arrange
		var executionLog = new List<string>();
		var baseHandler = new LoggingBaseHandler(executionLog);
		var derivedHandler = new LoggingDerivedHandler(executionLog);

		var services = new ServiceCollection();
		services.AddSingleton<INotificationHandler<BaseNotification>>(baseHandler);
		services.AddSingleton<INotificationHandler<DerivedNotification>>(derivedHandler);
		services.AddScoped<IPipelineBehavior<DerivedNotification, Unit>>(
			sp => new TestBehavior(executionLog));
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new DerivedNotification("Test", 42));

		// Assert - Behavior should wrap both handlers
		// Handlers execute from most specific to least specific (Derived → Base)
		executionLog.Should().Equal(
			"Behavior: Before",
			"DerivedHandler: Test (42)",
			"Behavior: After",
			"Behavior: Before",
			"BaseHandler: Test",
			"Behavior: After");
	}

	// Helper handlers
	private class FailingHandler : INotificationHandler<BaseNotification>
	{
		public Task Handle(BaseNotification notification, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("Handler failed");
		}
	}

	private class DelayedHandler : INotificationHandler<BaseNotification>
	{
		private readonly List<DateTime> _executionTimes;
		private readonly TimeSpan _delay;

		public DelayedHandler(List<DateTime> executionTimes, TimeSpan delay)
		{
			_executionTimes = executionTimes;
			_delay = delay;
		}

		public async Task Handle(BaseNotification notification, CancellationToken cancellationToken)
		{
			lock (_executionTimes)
			{
				_executionTimes.Add(DateTime.UtcNow);
			}
			await Task.Delay(_delay, cancellationToken);
		}
	}

	private class DerivedDelayedHandler : INotificationHandler<DerivedNotification>
	{
		private readonly List<DateTime> _executionTimes;
		private readonly TimeSpan _delay;

		public DerivedDelayedHandler(List<DateTime> executionTimes, TimeSpan delay)
		{
			_executionTimes = executionTimes;
			_delay = delay;
		}

		public async Task Handle(DerivedNotification notification, CancellationToken cancellationToken)
		{
			lock (_executionTimes)
			{
				_executionTimes.Add(DateTime.UtcNow);
			}
			await Task.Delay(_delay, cancellationToken);
		}
	}

	private class LoggingBaseHandler : INotificationHandler<BaseNotification>
	{
		private readonly List<string> _log;

		public LoggingBaseHandler(List<string> log)
		{
			_log = log;
		}

		public Task Handle(BaseNotification notification, CancellationToken cancellationToken)
		{
			_log.Add($"BaseHandler: {notification.Message}");
			return Task.CompletedTask;
		}
	}

	private class LoggingDerivedHandler : INotificationHandler<DerivedNotification>
	{
		private readonly List<string> _log;

		public LoggingDerivedHandler(List<string> log)
		{
			_log = log;
		}

		public Task Handle(DerivedNotification notification, CancellationToken cancellationToken)
		{
			_log.Add($"DerivedHandler: {notification.Message} ({notification.Value})");
			return Task.CompletedTask;
		}
	}

	private class TestBehavior : IPipelineBehavior<DerivedNotification, Unit>
	{
		private readonly List<string> _log;

		public TestBehavior(List<string> log)
		{
			_log = log;
		}

		public async Task<Unit> Handle(
			DerivedNotification message,
			Func<Task<Unit>> next,
			CancellationToken cancellationToken)
		{
			_log.Add("Behavior: Before");
			var result = await next();
			_log.Add("Behavior: After");
			return result;
		}
	}
}