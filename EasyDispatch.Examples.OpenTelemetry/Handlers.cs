using System;
using Microsoft.Extensions.Logging;

namespace EasyDispatch.Examples.OpenTelemetry;

// ============================================================================
// HANDLERS
// ============================================================================

public class GetUserQueryHandler(ILogger<GetUserQueryHandler> logger) : IQueryHandler<GetUserQuery, UserDto>
{
	private readonly ILogger<GetUserQueryHandler> _logger = logger;

	public async Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Fetching user {UserId}", query.UserId);

		// Simulate database call
		await Task.Delay(Random.Shared.Next(30, 80), cancellationToken);

		return new UserDto(
			query.UserId,
			$"User {query.UserId}",
			$"user{query.UserId}@example.com",
			DateTime.UtcNow.AddDays(-Random.Shared.Next(1, 100))
		);
	}
}

public class GetUserOrdersQueryHandler(ILogger<GetUserOrdersQueryHandler> logger) : IQueryHandler<GetUserOrdersQuery, List<OrderDto>>
{
	private readonly ILogger<GetUserOrdersQueryHandler> _logger = logger;

	public async Task<List<OrderDto>> Handle(GetUserOrdersQuery query, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Fetching orders for user {UserId}", query.UserId);

		// Simulate database call
		await Task.Delay(Random.Shared.Next(20, 60), cancellationToken);

		// Simulate an error for users with ID > 10
		if (query.UserId > 10)
		{
			_logger.LogError("User {UserId} not found in orders database", query.UserId);
			throw new InvalidOperationException($"User {query.UserId} not found in orders database");
		}

		var orderCount = Random.Shared.Next(1, 5);
		return [.. Enumerable.Range(1, orderCount)
			.Select(i => new OrderDto(
				i,
				query.UserId,
				$"Product {(char)('A' + i - 1)}",
				Random.Shared.Next(50, 500) + 0.99m))];
	}
}

public class CreateUserCommandHandler(ILogger<CreateUserCommandHandler> logger) : ICommandHandler<CreateUserCommand, int>
{
	private readonly ILogger<CreateUserCommandHandler> _logger = logger;
	private static int _nextUserId = 1;

	public async Task<int> Handle(CreateUserCommand command, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating user: {Name} ({Email})", command.Name, command.Email);

		// Simulate database insert
		await Task.Delay(Random.Shared.Next(50, 120), cancellationToken);

		var userId = Interlocked.Increment(ref _nextUserId);

		_logger.LogInformation("User created with ID: {UserId}", userId);
		return userId;
	}
}

public class DeleteUserCommandHandler(ILogger<DeleteUserCommandHandler> logger) : ICommandHandler<DeleteUserCommand>
{
	private readonly ILogger<DeleteUserCommandHandler> _logger = logger;

	public async Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Deleting user {UserId}", command.UserId);

		// Simulate database delete
		await Task.Delay(Random.Shared.Next(30, 70), cancellationToken);

		_logger.LogInformation("User {UserId} deleted", command.UserId);
	}
}

public class SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger) : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<SendWelcomeEmailHandler> _logger = logger;

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Sending welcome email to user {UserId}", notification.UserId);

		// Simulate email service call
		await Task.Delay(Random.Shared.Next(80, 150), cancellationToken);

		_logger.LogInformation("Welcome email sent to {Name}", notification.Name);
	}
}

public class CreateAnalyticsEventHandler(ILogger<CreateAnalyticsEventHandler> logger) : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<CreateAnalyticsEventHandler> _logger = logger;

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating analytics event for user {UserId}", notification.UserId);

		// Simulate analytics service call
		await Task.Delay(Random.Shared.Next(40, 80), cancellationToken);

		_logger.LogInformation("Analytics event created for {Name}", notification.Name);
	}
}

public class UpdateUserCacheHandler(ILogger<UpdateUserCacheHandler> logger) : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<UpdateUserCacheHandler> _logger = logger;

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Updating user cache for {UserId}", notification.UserId);

		// Simulate cache update
		await Task.Delay(Random.Shared.Next(20, 50), cancellationToken);

		_logger.LogInformation("Cache updated for {Name}", notification.Name);
	}
}