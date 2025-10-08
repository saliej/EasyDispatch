using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.IntegrationTests;

/// <summary>
/// Integration tests that verify the complete mediator pipeline end-to-end.
/// </summary>
[Collection("IntegrationTests")]
public class IntegrationTests
{
	private readonly List<Activity> _activities = [];
	private ActivityListener _listener;

	public Task InitializeAsync()
	{
		_listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == "EasyDispatch",
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => _activities.Add(activity)
		};
		ActivitySource.AddActivityListener(_listener);

		LoggingBehavior<GetUserQuery, UserDto>.Logs.Clear();

		return Task.CompletedTask;
	}

	public Task DisposeAsync()
	{
		_listener?.Dispose();
		_activities.Clear();
		return Task.CompletedTask;
	}

	[Fact]
    public async Task CompleteFlow_Query_WithMultipleBehaviors()
    {
        // Arrange
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Clear();
        ValidationBehavior<GetUserQuery, UserDto>.Validations.Clear();
        PerformanceBehavior<GetUserQuery, UserDto>.Metrics.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly)
            .AddOpenBehavior(typeof(LoggingBehavior<,>))
            .AddOpenBehavior(typeof(ValidationBehavior<,>))
            .AddOpenBehavior(typeof(PerformanceBehavior<,>));

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new GetUserQuery(42);

        // Act
        var result = await mediator.SendAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(42);
        result.Name.Should().Be("John Doe");

        // Verify behaviors executed
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Should().Contain(log => log.Contains("Executing GetUserQuery"));
        LoggingBehavior<GetUserQuery, UserDto>.Logs.Should().Contain(log => log.Contains("Executed GetUserQuery"));
        ValidationBehavior<GetUserQuery, UserDto>.Validations.Should().Contain(val => val.Contains("GetUserQuery"));
        PerformanceBehavior<GetUserQuery, UserDto>.Metrics.Should().Contain(metric => metric.Contains("GetUserQuery"));
    }

    [Fact]
    public async Task CompleteFlow_CommandWithResponse_ReturnsValue()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new CreateUserCommand("Jane Doe", "jane@example.com");

        // Act
        var userId = await mediator.SendAsync(command);

        // Assert
        userId.Should().Be(123);
    }

    [Fact]
    public async Task CompleteFlow_VoidCommand_ExecutesSuccessfully()
    {
        // Arrange
        DeleteUserCommandHandler.DeletedUserId = 0;

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new DeleteUserCommand(456);

        // Act
        await mediator.SendAsync(command);

        // Assert
        DeleteUserCommandHandler.DeletedUserId.Should().Be(456);
    }

    [Fact]
    public async Task CompleteFlow_Notification_ExecutesAllHandlers()
    {
        // Arrange
        EmailNotificationHandler.SentEmails.Clear();
        AuditNotificationHandler.AuditLog.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var notification = new UserCreatedNotification(789, "Alice");

        // Act
        await mediator.PublishAsync(notification);

        // Assert
        EmailNotificationHandler.SentEmails.Should().ContainSingle()
            .Which.Should().Contain("789");
        AuditNotificationHandler.AuditLog.Should().ContainSingle()
            .Which.Should().Contain("Alice");
    }

    [Fact]
    public async Task CompleteFlow_ValidationBehavior_CanPreventExecution()
    {
        // Arrange
        ValidationBehavior<CreateUserCommand, int>.Validations.Clear();

        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly)
            .AddOpenBehavior(typeof(ValidationBehavior<,>));

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var command = new CreateUserCommand("", "invalid@example.com");

        // Act
        var act = async () => await mediator.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Name is required");
        
        ValidationBehavior<CreateUserCommand, int>.Validations.Should()
            .Contain(v => v.Contains("CreateUserCommand"));
    }

    [Fact]
    public async Task CompleteFlow_BehaviorsExecuteInCorrectOrder()
    {
        // Arrange
        var executionOrder = new List<string>();
        
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<GetUserQuery, UserDto>, GetUserQueryHandler>();
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("First", executionOrder));
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("Second", executionOrder));
        services.AddScoped<IPipelineBehavior<GetUserQuery, UserDto>>(sp => 
            new OrderTrackingBehavior("Third", executionOrder));
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new GetUserQuery(1);

        // Act
        await mediator.SendAsync(query);

        // Assert
        executionOrder.Should().Equal(
            "First: Before",
            "Second: Before",
            "Third: Before",
            "Third: After",
            "Second: After",
            "First: After");
    }

    private class OrderTrackingBehavior(string name, List<string> log) : IPipelineBehavior<GetUserQuery, UserDto>
    {
        private readonly string _name = name;
        private readonly List<string> _log = log;

		public async Task<UserDto> Handle(
            GetUserQuery message,
            Func<Task<UserDto>> next,
            CancellationToken cancellationToken)
        {
            _log.Add($"{_name}: Before");
            var result = await next();
            _log.Add($"{_name}: After");
            return result;
        }
    }

    [Fact]
    public async Task CompleteFlow_ScopedServices_IsolatedBetweenRequests()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(IntegrationTests).Assembly);

        var provider = services.BuildServiceProvider();

        // Act & Assert - First scope
        using var scope1 = provider.CreateScope();
        var mediator1 = scope1.ServiceProvider.GetRequiredService<IMediator>();
        var result1 = await mediator1.SendAsync(new GetUserQuery(1));
        result1.Should().NotBeNull();
        

		// Act & Assert - Second scope (should be independent)
		using var scope2 = provider.CreateScope();
		var mediator2 = scope2.ServiceProvider.GetRequiredService<IMediator>();
		var result2 = await mediator2.SendAsync(new GetUserQuery(2));
		result2.Should().NotBeNull();
		result2.Id.Should().Be(2);
	}

    [Fact]
    public async Task CompleteFlow_CancellationToken_PropagatesToHandler()
    {
        // Arrange - Use manual registration to avoid conflicts with other handlers
        var services = new ServiceCollection();
        services.AddScoped<IQueryHandler<CancellationTestQuery, UserDto>, CancellationAwareHandler>();
        services.AddScoped<IMediator, Mediator>();

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var query = new CancellationTestQuery(1);

        // Act
        var act = async () => await mediator.SendAsync(query, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // Separate query type to avoid conflicts with GetUserQuery
    private record CancellationTestQuery(int UserId) : IQuery<UserDto>;

    private class CancellationAwareHandler : IQueryHandler<CancellationTestQuery, UserDto>
    {
        public Task<UserDto> Handle(CancellationTestQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new UserDto(query.UserId, "Test", "test@example.com"));
        }
    }

	[Fact]
	public async Task CompleteFlow_WithTracing_CreatesActivitiesForFullPipeline()
	{
		// Arrange
		var activities = new List<Activity>();
		using var listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == "EasyDispatch",
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
			ActivityStopped = activity => activities.Add(activity)
		};
		ActivitySource.AddActivityListener(listener);

		LoggingBehavior<GetUserQuery, UserDto>.Logs.Clear();

		var services = new ServiceCollection();
		services.AddMediator(typeof(IntegrationTests).Assembly)
			.AddOpenBehavior(typeof(LoggingBehavior<,>));

		var provider = services.BuildServiceProvider();
		var mediator = provider.GetRequiredService<IMediator>();

		// Act - Execute query, command, and notification
		var userDto = await mediator.SendAsync(new GetUserQuery(1));
		var newUserId = await mediator.SendAsync(new CreateUserCommand("Jane", "jane@example.com"));
		await mediator.SendAsync(new DeleteUserCommand(123));
		await mediator.PublishAsync(new UserCreatedNotification(newUserId, "Jane"));

		// Assert
		activities.Should().HaveCount(4);

		// Verify query activity
		var queryActivity = activities[0];
		queryActivity.DisplayName.Should().Be("EasyDispatch.IntegrationTests.GetUserQuery");
		queryActivity.Status.Should().Be(ActivityStatusCode.Ok);
		queryActivity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.operation" && tag.Value == "Query");

		// Verify command with response activity
		var commandActivity = activities[1];
		commandActivity.DisplayName.Should().Be("EasyDispatch.IntegrationTests.CreateUserCommand");
		commandActivity.Status.Should().Be(ActivityStatusCode.Ok);

		// Verify void command activity
		var voidCommandActivity = activities[2];
		voidCommandActivity.DisplayName.Should().Be("EasyDispatch.IntegrationTests.DeleteUserCommand");
		voidCommandActivity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.response_type" && tag.Value == "void");

		// Verify notification activity
		var notificationActivity = activities[3];
		notificationActivity.DisplayName.Should().Be("EasyDispatch.IntegrationTests.UserCreatedNotification");
		notificationActivity.Tags.Should().Contain(tag =>
			tag.Key == "easydispatch.handler_count");
	}
}