using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyDispatch.IntegrationTests;

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
	public Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(new UserDto(query.UserId, "John Doe", "john@example.com"));
	}
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
{
	public Task<int> Handle(CreateUserCommand command, CancellationToken cancellationToken)
	{
		// Simulate user creation returning new user ID
		return Task.FromResult(123);
	}
}

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand>
{
	public static int DeletedUserId { get; set; }

	public Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
	{
		DeletedUserId = command.UserId;
		return Task.CompletedTask;
	}
}

public class EmailNotificationHandler : INotificationHandler<UserCreatedNotification>
{
	public static List<string> SentEmails { get; } = [];

	public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		SentEmails.Add($"Welcome email sent to user {notification.UserId}");
		return Task.CompletedTask;
	}
}

public class AuditNotificationHandler : INotificationHandler<UserCreatedNotification>
{
	public static List<string> AuditLog { get; } = [];

	public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		AuditLog.Add($"User created: {notification.Name} (ID: {notification.UserId})");
		return Task.CompletedTask;
	}
}