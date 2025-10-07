using System;

namespace EasyDispatch.Examples.OpenTelemetry;

public record UserDto(int Id, string Name, string Email, DateTime CreatedAt);
public record OrderDto(int Id, int UserId, string Product, decimal Amount);
public record CreateUserRequest(string Name, string Email);