using EasyDispatch;
using EasyDispatch.Examples.OpenTelemetry;
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
	jaegerUI = String.Equals(exporterType, "jaeger", StringComparison.CurrentCultureIgnoreCase) ? "http://localhost:16686" : null,
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

if (String.Equals(exporterType, "jaeger", StringComparison.CurrentCultureIgnoreCase))
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
