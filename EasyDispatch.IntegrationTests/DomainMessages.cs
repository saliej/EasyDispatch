using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyDispatch.IntegrationTests;

public record GetUserQuery(int UserId) : IQuery<UserDto>;
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record DeleteUserCommand(int UserId) : ICommand;
public record UserCreatedNotification(int UserId, string Name) : INotification;