using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

/// <summary>
/// Tests for Activity/tracing support.
/// These tests are isolated in a collection to prevent parallel execution
/// from causing ActivityListener to capture activities from other tests.
/// </summary>
[Collection("ActivityTests")]
public class ActivityTests
{
	private record TestQuery(int Id) : IQuery<string>;
	private record TestCommand(string Name) : ICommand;
	private record TestCommandWithResponse(int Value) : ICommand<int>;
	private record TestNotification(string Message) : INotification;

	private class TestQueryHandler : IQueryHandler<TestQuery, string>
	{
		public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
		{
			return Task.FromResult($"Result: {query.Id}");
		}
	}

	private class TestCommandHandler : ICommandHandler<TestCommand>
	{
		public Task Handle(TestCommand command, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	private class TestCommandWithResponseHandler : ICommandHandler<TestCommandWithResponse, int>
	{
		public Task<int> Handle(TestCommandWithResponse command, CancellationToken cancellationToken)
		{
			return Task.FromResult(command.Value * 2);
		}
	}

	private class TestNotificationHandler : INotificationHandler<TestNotification>
	{
		public Task Handle(TestNotification notification, CancellationToken cancellationToken)
		{
			return Task.CompletedTask;
		}
	}

	private class FailingQueryHandler : IQueryHandler<TestQuery, string>
	{
		public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
		{
			throw new InvalidOperationException("Handler failed");
		}
	}

	[Fact]
	public async Task SendAsync_Query_CreatesActivityWithCorrectTags()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<IQueryHandler<TestQuery, string>, TestQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.SendAsync(new TestQuery(42));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.DisplayName.Should().Be("EasyDispatch.UnitTests.ActivityTests+TestQuery");
		activity.Status.Should().Be(ActivityStatusCode.Ok);

		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.operation" && tag.Value == "Query");
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.message_type" && tag.Value!.Contains("TestQuery"));
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.response_type" && tag.Value == "System.String");
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.handler_type" && tag.Value!.Contains("TestQueryHandler"));
	}

	[Fact]
	public async Task SendAsync_VoidCommand_CreatesActivityWithCorrectTags()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<TestCommand>, TestCommandHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.SendAsync(new TestCommand("test"));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.DisplayName.Should().Be("EasyDispatch.UnitTests.ActivityTests+TestCommand");
		activity.Status.Should().Be(ActivityStatusCode.Ok);

		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.operation" && tag.Value == "Command");
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.response_type" && tag.Value == "void");
	}

	[Fact]
	public async Task SendAsync_CommandWithResponse_CreatesActivityWithCorrectTags()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<ICommandHandler<TestCommandWithResponse, int>, TestCommandWithResponseHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.SendAsync(new TestCommandWithResponse(10));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.DisplayName.Should().Be("EasyDispatch.UnitTests.ActivityTests+TestCommandWithResponse");
		activity.Status.Should().Be(ActivityStatusCode.Ok);

		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.response_type" && tag.Value == "System.Int32");
	}

	[Fact]
	public async Task PublishAsync_Notification_CreatesActivityWithCorrectTags()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<INotificationHandler<TestNotification>, TestNotificationHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new TestNotification("test"));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.DisplayName.Should().Be("EasyDispatch.UnitTests.ActivityTests+TestNotification");
		activity.Status.Should().Be(ActivityStatusCode.Ok);

		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.operation" && tag.Value == "Notification");
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.handler_count" && tag.Value == "1");
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.publish_strategy");
	}

	[Fact]
	public async Task SendAsync_WithBehaviors_IncludesBehaviorCount()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<IQueryHandler<TestQuery, string>, TestQueryHandler>();
		services.AddScoped<IPipelineBehavior<TestQuery, string>, DummyBehavior1>();
		services.AddScoped<IPipelineBehavior<TestQuery, string>, DummyBehavior2>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.SendAsync(new TestQuery(1));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.behavior_count" && tag.Value == "2");
	}

	private class DummyBehavior1 : IPipelineBehavior<TestQuery, string>
	{
		public Task<string> Handle(TestQuery message, Func<Task<string>> next, CancellationToken cancellationToken)
		{
			return next();
		}
	}

	private class DummyBehavior2 : IPipelineBehavior<TestQuery, string>
	{
		public Task<string> Handle(TestQuery message, Func<Task<string>> next, CancellationToken cancellationToken)
		{
			return next();
		}
	}

	[Fact]
	public async Task SendAsync_WhenHandlerFails_SetsActivityError()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<IQueryHandler<TestQuery, string>, FailingQueryHandler>();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		var act = async () => await mediator.SendAsync(new TestQuery(1));

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>();

		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.Status.Should().Be(ActivityStatusCode.Error);
		activity.StatusDescription.Should().Be("Handler failed");

		// Verify exception was recorded
		activity.Events.Should().Contain(e => e.Name == "exception");
	}

	[Fact]
	public async Task SendAsync_MissingHandler_SetsActivityError()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		var act = async () => await mediator.SendAsync(new TestQuery(1));

		// Assert
		await act.Should().ThrowAsync<InvalidOperationException>();

		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.Status.Should().Be(ActivityStatusCode.Error);
		activity.StatusDescription.Should().StartWith("No handler registered");
	}

	[Fact]
	public async Task PublishAsync_NoHandlers_CompletesSuccessfully()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == EasyDispatchActivitySource.SourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		var services = new ServiceCollection();
		services.AddScoped<IMediator, Mediator>();

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		// Act
		await mediator.PublishAsync(new TestNotification("test"));

		// Assert
		activities.Should().ContainSingle();
		var activity = activities[0];

		activity.Status.Should().Be(ActivityStatusCode.Ok);
		activity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.handler_count" && tag.Value == "0");
	}

	[Fact]
	public void ActivitySource_HasCorrectNameAndVersion()
	{
		// Assert
		EasyDispatchActivitySource.Source.Name.Should().Be("EasyDispatch");
		EasyDispatchActivitySource.Source.Version.Should().NotBeNullOrEmpty();
		EasyDispatchActivitySource.SourceName.Should().Be("EasyDispatch");
		EasyDispatchActivitySource.Version.Should().NotBeNullOrEmpty();

		// Version should match assembly version format
		EasyDispatchActivitySource.Version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
	}
}

/// <summary>
/// Collection definition to ensure Activity tests run sequentially.
/// This prevents ActivityListener from capturing activities from parallel tests.
/// </summary>
[CollectionDefinition("ActivityTests", DisableParallelization = true)]
public class ActivityTestsCollection
{
}