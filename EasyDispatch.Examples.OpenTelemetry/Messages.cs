using System;

namespace EasyDispatch.Examples.OpenTelemetry;

public record GetUserQuery(int UserId) : IQuery<UserDto>;
public record GetUserOrdersQuery(int UserId) : IQuery<List<OrderDto>>;
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record DeleteUserCommand(int UserId) : ICommand;
public record UserCreatedNotification(int UserId, string Name) : INotification;