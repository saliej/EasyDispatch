using EasyDispatch;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Register EasyDispatch with handlers
builder.Services.AddMediator(typeof(Program).Assembly);

// Configure OpenTelemetry
var exporterType = builder.Configuration["OpenTelemetry:Exporter"] ?? "Console";

builder.Services.AddOpenTelemetry()
	.ConfigureResource(resource => resource
		.AddService(
			serviceName: "EasyDispatch.Demo",
			serviceVersion: "1.0.0",
			serviceInstanceId: Environment.MachineName))
	.WithTracing(tracing =>
	{
		// Add EasyDispatch as a trace source
		tracing.AddSource("EasyDispatch");

		// Add ASP.NET Core instrumentation
		tracing.AddAspNetCoreInstrumentation(options =>
		{
			options.RecordException = true;
			options.Filter = (httpContext) =>
			{
				// Don't trace health checks
				return !httpContext.Request.Path.StartsWithSegments("/health");
			};
		});

		// Add HTTP client instrumentation
		tracing.AddHttpClientInstrumentation();

		// Configure exporter based on configuration
		switch (exporterType.ToLower())
		{
			case "jaeger":
				Console.WriteLine("Using Jaeger exporter");
				tracing.AddOtlpExporter(options =>
				{
					// Jaeger supports OTLP
					options.Endpoint = new Uri("http://localhost:4317");
				});
				break;

			case "otlp":
				Console.WriteLine("Using OTLP exporter");
				tracing.AddOtlpExporter(options =>
				{
					options.Endpoint = new Uri(
						builder.Configuration["OpenTelemetry:OtlpEndpoint"]
						?? "http://localhost:4317");
				});
				break;

			default:
				Console.WriteLine("Using Console exporter");
				tracing.AddConsoleExporter();
				break;
		}
	});

var app = builder.Build();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

// Root endpoint with instructions
app.MapGet("/", () => Results.Ok(new
{
	service = "EasyDispatch OpenTelemetry Demo",
	version = "1.0.0",
	exporter = exporterType,
	jaegerUI = exporterType.ToLower() == "jaeger" ? "http://localhost:16686" : null,
	endpoints = new[]
	{
		new { method = "GET", path = "/users/{id}", description = "Get user by ID" },
		new { method = "POST", path = "/users", description = "Create user (name, email in body)" },
		new { method = "GET", path = "/users/{id}/orders", description = "Get user orders (error demo for id > 10)" },
		new { method = "DELETE", path = "/users/{id}", description = "Delete user (void command)" },
		new { method = "GET", path = "/health", description = "Health check" }
	},
	examples = new
	{
		getUser = "curl http://localhost:5000/users/5",
		createUser = "curl -X POST http://localhost:5000/users -H 'Content-Type: application/json' -d '{\"name\":\"John\",\"email\":\"john@example.com\"}'",
		getOrders = "curl http://localhost:5000/users/5/orders",
		getOrdersError = "curl http://localhost:5000/users/15/orders",
		deleteUser = "curl -X DELETE http://localhost:5000/users/5"
	}
}));

// User endpoints
app.MapGet("/users/{id}", async ([FromRoute] int id, IMediator mediator) =>
{
	var user = await mediator.SendAsync(new GetUserQuery(id));
	return Results.Ok(user);
});

app.MapPost("/users", async ([FromBody] CreateUserRequest request, IMediator mediator) =>
{
	if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Email))
	{
		return Results.BadRequest(new { error = "Name and email are required" });
	}

	var userId = await mediator.SendAsync(new CreateUserCommand(request.Name, request.Email));

	// Publish notification - this will trigger multiple handlers
	await mediator.PublishAsync(new UserCreatedNotification(userId, request.Name));

	return Results.Created($"/users/{userId}", new { userId, name = request.Name });
});

app.MapGet("/users/{id}/orders", async ([FromRoute] int id, IMediator mediator) =>
{
	// This will fail for id > 10 to demonstrate error tracing
	var orders = await mediator.SendAsync(new GetUserOrdersQuery(id));
	return Results.Ok(orders);
});

app.MapDelete("/users/{id}", async ([FromRoute] int id, IMediator mediator) =>
{
	await mediator.SendAsync(new DeleteUserCommand(id));
	return Results.NoContent();
});

Console.WriteLine("============================================");
Console.WriteLine(" EasyDispatch OpenTelemetry Demo");
Console.WriteLine("============================================");
Console.WriteLine($" API: http://localhost:5000");
Console.WriteLine($" Exporter: {exporterType}");

if (exporterType.ToLower() == "jaeger")
{
	Console.WriteLine($"Jaeger UI: http://localhost:16686");
	Console.WriteLine("   → Look for service: 'EasyDispatch.Demo'");
}

Console.WriteLine("============================================");
Console.WriteLine("\nQuick Test:");
Console.WriteLine("   curl http://localhost:5000/users/5");
Console.WriteLine("   curl -X POST http://localhost:5000/users -H 'Content-Type: application/json' -d '{\"name\":\"Test\",\"email\":\"test@example.com\"}'");
Console.WriteLine("");

app.Run();

// ============================================================================
// MESSAGE DEFINITIONS
// ============================================================================

public record GetUserQuery(int UserId) : IQuery<UserDto>;
public record GetUserOrdersQuery(int UserId) : IQuery<List<OrderDto>>;
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record DeleteUserCommand(int UserId) : ICommand;
public record UserCreatedNotification(int UserId, string Name) : INotification;

// DTOs
public record UserDto(int Id, string Name, string Email, DateTime CreatedAt);
public record OrderDto(int Id, int UserId, string Product, decimal Amount);
public record CreateUserRequest(string Name, string Email);

// ============================================================================
// HANDLERS
// ============================================================================

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
	private readonly ILogger<GetUserQueryHandler> _logger;

	public GetUserQueryHandler(ILogger<GetUserQueryHandler> logger)
	{
		_logger = logger;
	}

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

public class GetUserOrdersQueryHandler : IQueryHandler<GetUserOrdersQuery, List<OrderDto>>
{
	private readonly ILogger<GetUserOrdersQueryHandler> _logger;

	public GetUserOrdersQueryHandler(ILogger<GetUserOrdersQueryHandler> logger)
	{
		_logger = logger;
	}

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
		return Enumerable.Range(1, orderCount)
			.Select(i => new OrderDto(
				i,
				query.UserId,
				$"Product {(char)('A' + i - 1)}",
				Random.Shared.Next(50, 500) + 0.99m))
			.ToList();
	}
}

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
{
	private readonly ILogger<CreateUserCommandHandler> _logger;
	private static int _nextUserId = 1;

	public CreateUserCommandHandler(ILogger<CreateUserCommandHandler> logger)
	{
		_logger = logger;
	}

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

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand>
{
	private readonly ILogger<DeleteUserCommandHandler> _logger;

	public DeleteUserCommandHandler(ILogger<DeleteUserCommandHandler> logger)
	{
		_logger = logger;
	}

	public async Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Deleting user {UserId}", command.UserId);

		// Simulate database delete
		await Task.Delay(Random.Shared.Next(30, 70), cancellationToken);

		_logger.LogInformation("User {UserId} deleted", command.UserId);
	}
}

// ============================================================================
// NOTIFICATION HANDLERS
// ============================================================================

public class SendWelcomeEmailHandler : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<SendWelcomeEmailHandler> _logger;

	public SendWelcomeEmailHandler(ILogger<SendWelcomeEmailHandler> logger)
	{
		_logger = logger;
	}

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Sending welcome email to user {UserId}", notification.UserId);

		// Simulate email service call
		await Task.Delay(Random.Shared.Next(80, 150), cancellationToken);

		_logger.LogInformation("Welcome email sent to {Name}", notification.Name);
	}
}

public class CreateAnalyticsEventHandler : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<CreateAnalyticsEventHandler> _logger;

	public CreateAnalyticsEventHandler(ILogger<CreateAnalyticsEventHandler> logger)
	{
		_logger = logger;
	}

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Creating analytics event for user {UserId}", notification.UserId);

		// Simulate analytics service call
		await Task.Delay(Random.Shared.Next(40, 80), cancellationToken);

		_logger.LogInformation("Analytics event created for {Name}", notification.Name);
	}
}

public class UpdateUserCacheHandler : INotificationHandler<UserCreatedNotification>
{
	private readonly ILogger<UpdateUserCacheHandler> _logger;

	public UpdateUserCacheHandler(ILogger<UpdateUserCacheHandler> logger)
	{
		_logger = logger;
	}

	public async Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		_logger.LogInformation("Updating user cache for {UserId}", notification.UserId);

		// Simulate cache update
		await Task.Delay(Random.Shared.Next(20, 50), cancellationToken);

		_logger.LogInformation("Cache updated for {Name}", notification.Name);
	}
}