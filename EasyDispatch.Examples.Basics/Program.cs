using EasyDispatch;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.Examples.Basics;

class Program
{
	static async Task Main(string[] args)
	{
		Console.WriteLine("==============================================");
		Console.WriteLine("       EasyDispatch Library Demo");
		Console.WriteLine("==============================================");
		Console.WriteLine();

		var services = ConfigureServices();
		var serviceProvider = services.BuildServiceProvider();

		bool exit = false;
		while (!exit)
		{
			DisplayMenu();
			var choice = Console.ReadLine();

			Console.WriteLine();

			using var scope = serviceProvider.CreateScope();
			var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

			switch (choice)
			{
				case "1":
					await DemoQueries(mediator);
					break;
				case "2":
					await DemoVoidCommands(mediator);
					break;
				case "3":
					await DemoCommandsWithResponse(mediator);
					break;
				case "4":
					await DemoNotifications(mediator);
					break;
				case "5":
					await DemoPipelineBehaviors();
					break;
				case "6":
					await DemoPolymorphicDispatch(mediator);
					break;
				case "7":
					await DemoPublishStrategies();
					break;
				case "8":
					await DemoStrategyOverride();
					break;
				case "0":
					exit = true;
					Console.WriteLine("Exiting demo. Goodbye!");
					break;
				default:
					Console.WriteLine("Invalid choice. Please try again.");
					break;
			}

			if (!exit)
			{
				Console.WriteLine();
				Console.WriteLine("Press any key to continue...");
				Console.ReadKey();
				Console.Clear();
			}
		}
	}

	static void DisplayMenu()
	{
		Console.WriteLine("Select a feature to demonstrate:");
		Console.WriteLine("--------------------------------");
		Console.WriteLine("1. Query Handlers (Request-Response)");
		Console.WriteLine("2. Command Handlers (Void)");
		Console.WriteLine("3. Command Handlers (With Response)");
		Console.WriteLine("4. Notification Handlers (Pub/Sub)");
		Console.WriteLine("5. Pipeline Behaviors (Middleware)");
		Console.WriteLine("6. Polymorphic Dispatch");
		Console.WriteLine("7. Notification Publish Strategies");
		Console.WriteLine("8. Per-Call Strategy Override");
		Console.WriteLine("0. Exit");
		Console.WriteLine();
		Console.Write("Your choice: ");
	}

	static IServiceCollection ConfigureServices()
	{
		var services = new ServiceCollection();

		// Register mediator with current assembly
		services.AddMediator(typeof(Program).Assembly);

		return services;
	}

	// ========================================
	// Demo 1: Query Handlers
	// ========================================
	static async Task DemoQueries(IMediator mediator)
	{
		Console.WriteLine(">>> QUERY HANDLERS DEMO");
		Console.WriteLine("Queries are side-effect free operations that return data.");
		Console.WriteLine();

		Console.WriteLine("Sending: GetUserQuery(UserId: 123)");
		var userQuery = new GetUserQuery(123);
		var user = await mediator.SendAsync(userQuery);
		Console.WriteLine($"Result: {user.Name} ({user.Email})");
		Console.WriteLine();

		Console.WriteLine("Sending: GetProductQuery(ProductId: 456)");
		var productQuery = new GetProductQuery(456);
		var product = await mediator.SendAsync(productQuery);
		Console.WriteLine($"Result: {product.Name} - ${product.Price}");
	}

	// ========================================
	// Demo 2: Void Commands
	// ========================================
	static async Task DemoVoidCommands(IMediator mediator)
	{
		Console.WriteLine(">>> VOID COMMAND HANDLERS DEMO");
		Console.WriteLine("Commands cause side effects and don't return values.");
		Console.WriteLine();

		Console.WriteLine("Sending: DeleteUserCommand(UserId: 999)");
		await mediator.SendAsync(new DeleteUserCommand(999));
		Console.WriteLine("Command executed successfully!");
		Console.WriteLine();

		Console.WriteLine("Sending: SendEmailCommand");
		await mediator.SendAsync(new SendEmailCommand("user@example.com", "Hello!"));
		Console.WriteLine("Email sent!");
	}

	// ========================================
	// Demo 3: Commands with Response
	// ========================================
	static async Task DemoCommandsWithResponse(IMediator mediator)
	{
		Console.WriteLine(">>> COMMAND HANDLERS WITH RESPONSE DEMO");
		Console.WriteLine("Commands that perform operations and return results.");
		Console.WriteLine();

		Console.WriteLine("Sending: CreateUserCommand");
		var createCommand = new CreateUserCommand("Jane Doe", "jane@example.com");
		var userId = await mediator.SendAsync(createCommand);
		Console.WriteLine($"User created with ID: {userId}");
		Console.WriteLine();

		Console.WriteLine("Sending: ProcessPaymentCommand");
		var paymentCommand = new ProcessPaymentCommand(99.99m);
		var transactionId = await mediator.SendAsync(paymentCommand);
		Console.WriteLine($"Payment processed. Transaction ID: {transactionId}");
	}

	// ========================================
	// Demo 4: Notifications (Multiple Handlers)
	// ========================================
	static async Task DemoNotifications(IMediator mediator)
	{
		Console.WriteLine(">>> NOTIFICATION HANDLERS DEMO");
		Console.WriteLine("Notifications are published to multiple handlers (pub/sub pattern).");
		Console.WriteLine();

		Console.WriteLine("Publishing: UserCreatedNotification");
		await mediator.PublishAsync(new UserCreatedNotification(789, "John Smith"));
		Console.WriteLine();
		Console.WriteLine("Check the output above - multiple handlers executed:");
		Console.WriteLine("  - Email notification sent");
		Console.WriteLine("  - Audit log created");
		Console.WriteLine("  - Welcome message sent");
	}

	// ========================================
	// Demo 5: Pipeline Behaviors
	// ========================================
	static async Task DemoPipelineBehaviors()
	{
		Console.WriteLine(">>> PIPELINE BEHAVIORS DEMO");
		Console.WriteLine("Behaviors act as middleware that wraps handler execution.");
		Console.WriteLine();

		// Create a new scope with behaviors registered
		var services = new ServiceCollection();
		services.AddMediator(typeof(Program).Assembly)
			.AddOpenBehavior(typeof(LoggingBehavior<,>))
			.AddOpenBehavior(typeof(TimingBehavior<,>))
			.AddOpenBehavior(typeof(ValidationBehavior<,>));

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		Console.WriteLine("Sending query with behaviors attached...");
		Console.WriteLine();

		var query = new GetUserQuery(123);
		await mediator.SendAsync(query);

		Console.WriteLine();
		Console.WriteLine("Behaviors executed in order:");
		Console.WriteLine("  1. Logging (before)");
		Console.WriteLine("  2. Timing (before)");
		Console.WriteLine("  3. Validation (before)");
		Console.WriteLine("  4. Handler execution");
		Console.WriteLine("  5. Validation (after)");
		Console.WriteLine("  6. Timing (after)");
		Console.WriteLine("  7. Logging (after)");
	}

	// ========================================
	// Demo 6: Polymorphic Dispatch
	// ========================================
	static async Task DemoPolymorphicDispatch(IMediator mediator)
	{
		Console.WriteLine(">>> POLYMORPHIC DISPATCH DEMO");
		Console.WriteLine("Handlers for base types/interfaces are invoked when publishing derived notifications.");
		Console.WriteLine();

		Console.WriteLine("Publishing: OrderCreatedNotification (derived from BaseOrderEvent)");
		await mediator.PublishAsync(new OrderCreatedNotification(12345, 299.99m));
		Console.WriteLine();
		Console.WriteLine("Handlers invoked:");
		Console.WriteLine("  - BaseOrderEventHandler (base type)");
		Console.WriteLine("  - OrderCreatedNotificationHandler (concrete type)");
		Console.WriteLine();

		Console.WriteLine("Publishing: BaseOrderEvent directly");
		await mediator.PublishAsync(new BaseOrderEvent(99999));
		Console.WriteLine();
		Console.WriteLine("Only BaseOrderEventHandler invoked (no derived handlers)");
	}

	// ========================================
	// Demo 7: Publish Strategies
	// ========================================
	static async Task DemoPublishStrategies()
	{
		Console.WriteLine(">>> NOTIFICATION PUBLISH STRATEGIES DEMO");
		Console.WriteLine("Different strategies for handling multiple notification handlers.");
		Console.WriteLine("Publishing ErrorProneNotification (3 handlers: success, fail, success)");
		Console.WriteLine();

		// Strategy 1: StopOnFirstException (default)
		Console.WriteLine("1. StopOnFirstException (default)");
		Console.WriteLine("   - Handlers execute sequentially");
		Console.WriteLine("   - Stops on first exception");
		await DemoStrategy(NotificationPublishStrategy.StopOnFirstException);
		Console.WriteLine();

		// Strategy 2: ContinueOnException
		Console.WriteLine("2. ContinueOnException");
		Console.WriteLine("   - Handlers execute sequentially");
		Console.WriteLine("   - Continues even if handlers fail");
		Console.WriteLine("   - Collects all exceptions");
		await DemoStrategy(NotificationPublishStrategy.ContinueOnException);
		Console.WriteLine();

		// Strategy 3: ParallelWhenAll
		Console.WriteLine("3. ParallelWhenAll");
		Console.WriteLine("   - All handlers execute in parallel");
		Console.WriteLine("   - Waits for all to complete");
		Console.WriteLine("   - Collects all exceptions");
		await DemoStrategy(NotificationPublishStrategy.ParallelWhenAll);
		Console.WriteLine();

		// Strategy 4: ParallelNoWait
		Console.WriteLine("4. ParallelNoWait (Fire and Forget)");
		Console.WriteLine("   - Handlers execute in parallel");
		Console.WriteLine("   - Returns immediately without waiting");
		Console.WriteLine("   - Exceptions are logged but not thrown");
		await DemoStrategy(NotificationPublishStrategy.ParallelNoWait);
		Console.WriteLine("   [NOTE] Method returned immediately - handlers may still be running");
		await Task.Delay(100); // Give handlers time to complete
	}

	static async Task DemoStrategy(NotificationPublishStrategy strategy)
	{
		var services = new ServiceCollection();
		services.AddMediator(options =>
		{
			options.Assemblies = [typeof(Program).Assembly];
			options.NotificationPublishStrategy = strategy;
		});

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		try
		{
			await mediator.PublishAsync(new ErrorProneNotification("Test"));
			Console.WriteLine("   [SUCCESS] All handlers completed without throwing");
		}
		catch (AggregateException ex)
		{
			Console.WriteLine($"   [EXCEPTION] AggregateException with {ex.InnerExceptions.Count} inner exception(s)");
			foreach (var inner in ex.InnerExceptions)
			{
				Console.WriteLine($"     - {inner.Message}");
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"   [EXCEPTION] {ex.GetType().Name}: {ex.Message}");
		}
	}

	// ========================================
	// Demo 8: Per-Call Strategy Override
	// ========================================
	static async Task DemoStrategyOverride()
	{
		Console.WriteLine(">>> PER-CALL STRATEGY OVERRIDE DEMO");
		Console.WriteLine("Override the configured strategy for specific notifications.");
		Console.WriteLine();

		// Configure with StopOnFirstException as default
		var services = new ServiceCollection();
		services.AddMediator(options =>
		{
			options.Assemblies = [typeof(Program).Assembly];
			options.NotificationPublishStrategy = NotificationPublishStrategy.StopOnFirstException;
		});

		var provider = services.BuildServiceProvider();
		using var scope = provider.CreateScope();
		var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

		Console.WriteLine("Configured Strategy: StopOnFirstException");
		Console.WriteLine();

		// Test 1: Use configured strategy (no override)
		Console.WriteLine("1. Publishing WITHOUT override (uses configured strategy):");
		try
		{
			await mediator.PublishAsync(new ErrorProneNotification("Test1"));
			Console.WriteLine("   [SUCCESS] Completed");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"   [EXCEPTION] {ex.GetType().Name}: {ex.Message}");
			Console.WriteLine("   Handler 3 was NOT executed (stopped on first exception)");
		}
		Console.WriteLine();

		// Test 2: Override with ContinueOnException
		Console.WriteLine("2. Publishing WITH override to ContinueOnException:");
		try
		{
			await mediator.PublishAsync(
				new ErrorProneNotification("Test2"),
				NotificationPublishStrategy.ContinueOnException);
			Console.WriteLine("   [SUCCESS] Completed");
		}
		catch (AggregateException ex)
		{
			Console.WriteLine($"   [EXCEPTION] AggregateException with {ex.InnerExceptions.Count} exception(s)");
			Console.WriteLine("   Handler 3 WAS executed (continued despite exception)");
		}
		Console.WriteLine();

		// Test 3: Override with ParallelWhenAll
		Console.WriteLine("3. Publishing WITH override to ParallelWhenAll:");
		var startTime = DateTime.UtcNow;
		try
		{
			await mediator.PublishAsync(
				new UserCreatedNotification(1, "Parallel Test"),
				NotificationPublishStrategy.ParallelWhenAll);
			var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
			Console.WriteLine($"   [SUCCESS] Completed in {elapsed:F0}ms (parallel execution)");
		}
		catch (Exception ex)
		{
			Console.WriteLine($"   [EXCEPTION] {ex.GetType().Name}");
		}
		Console.WriteLine();

		Console.WriteLine("Summary:");
		Console.WriteLine("  - Each PublishAsync call can use a different strategy");
		Console.WriteLine("  - Per-call strategy takes precedence over configured strategy");
		Console.WriteLine("  - Useful for special cases requiring different behavior");
	}
}

// ========================================
// QUERIES
// ========================================
public record GetUserQuery(int UserId) : IQuery<UserDto>;
public record GetProductQuery(int ProductId) : IQuery<ProductDto>;

public record UserDto(int Id, string Name, string Email);
public record ProductDto(int Id, string Name, decimal Price);

public class GetUserQueryHandler : IQueryHandler<GetUserQuery, UserDto>
{
	public Task<UserDto> Handle(GetUserQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(new UserDto(query.UserId, "John Doe", "john@example.com"));
	}
}

public class GetProductQueryHandler : IQueryHandler<GetProductQuery, ProductDto>
{
	public Task<ProductDto> Handle(GetProductQuery query, CancellationToken cancellationToken)
	{
		return Task.FromResult(new ProductDto(query.ProductId, "Widget Pro", 29.99m));
	}
}

// ========================================
// COMMANDS (Void)
// ========================================
public record DeleteUserCommand(int UserId) : ICommand;
public record SendEmailCommand(string To, string Message) : ICommand;

public class DeleteUserCommandHandler : ICommandHandler<DeleteUserCommand>
{
	public Task Handle(DeleteUserCommand command, CancellationToken cancellationToken)
	{
		// Simulate deletion
		Console.WriteLine($"[HANDLER] User {command.UserId} deleted from database");
		return Task.CompletedTask;
	}
}

public class SendEmailCommandHandler : ICommandHandler<SendEmailCommand>
{
	public Task Handle(SendEmailCommand command, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[HANDLER] Email sent to {command.To}: {command.Message}");
		return Task.CompletedTask;
	}
}

// ========================================
// COMMANDS (With Response)
// ========================================
public record CreateUserCommand(string Name, string Email) : ICommand<int>;
public record ProcessPaymentCommand(decimal Amount) : ICommand<string>;

public class CreateUserCommandHandler : ICommandHandler<CreateUserCommand, int>
{
	private static int _nextId = 1000;

	public Task<int> Handle(CreateUserCommand command, CancellationToken cancellationToken)
	{
		var userId = _nextId++;
		Console.WriteLine($"[HANDLER] Creating user: {command.Name} ({command.Email})");
		return Task.FromResult(userId);
	}
}

public class ProcessPaymentCommandHandler : ICommandHandler<ProcessPaymentCommand, string>
{
	public Task<string> Handle(ProcessPaymentCommand command, CancellationToken cancellationToken)
	{
		var transactionId = Guid.NewGuid().ToString("N")[..8].ToUpper();
		Console.WriteLine($"[HANDLER] Processing payment of ${command.Amount}");
		return Task.FromResult(transactionId);
	}
}

// ========================================
// NOTIFICATIONS
// ========================================
public record UserCreatedNotification(int UserId, string Name) : INotification;

public class EmailNotificationHandler : INotificationHandler<UserCreatedNotification>
{
	public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[EMAIL HANDLER] Welcome email sent to user {notification.UserId}");
		return Task.CompletedTask;
	}
}

public class AuditNotificationHandler : INotificationHandler<UserCreatedNotification>
{
	public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[AUDIT HANDLER] Logged: User created - {notification.Name} (ID: {notification.UserId})");
		return Task.CompletedTask;
	}
}

public class WelcomeMessageHandler : INotificationHandler<UserCreatedNotification>
{
	public Task Handle(UserCreatedNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[WELCOME HANDLER] Welcome message displayed for {notification.Name}");
		return Task.CompletedTask;
	}
}

// ========================================
// PIPELINE BEHAVIORS
// ========================================
public class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		var messageName = typeof(TMessage).Name;
		Console.WriteLine($"[LOGGING] Executing {messageName}...");

		var result = await next();

		Console.WriteLine($"[LOGGING] Executed {messageName} successfully");
		return result;
	}
}

public class TimingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		var result = await next();

		stopwatch.Stop();
		Console.WriteLine($"[TIMING] Execution took {stopwatch.ElapsedMilliseconds}ms");
		return result;
	}
}

public class ValidationBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
{
	public async Task<TResponse> Handle(
		TMessage message,
		Func<Task<TResponse>> next,
		CancellationToken cancellationToken)
	{
		Console.WriteLine($"[VALIDATION] Validating {typeof(TMessage).Name}...");

		// Simulate validation
		if (message == null)
			throw new ArgumentNullException(nameof(message));

		Console.WriteLine($"[VALIDATION] Validation passed");
		return await next();
	}
}

// ========================================
// POLYMORPHIC DISPATCH
// ========================================
public record BaseOrderEvent(int OrderId) : INotification;
public record OrderCreatedNotification(int OrderId, decimal Amount) : BaseOrderEvent(OrderId);

public class BaseOrderEventHandler : INotificationHandler<BaseOrderEvent>
{
	public Task Handle(BaseOrderEvent notification, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[BASE HANDLER] Processing order event for Order #{notification.OrderId}");
		return Task.CompletedTask;
	}
}

public class OrderCreatedNotificationHandler : INotificationHandler<OrderCreatedNotification>
{
	public Task Handle(OrderCreatedNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine($"[SPECIFIC HANDLER] Order #{notification.OrderId} created with amount ${notification.Amount}");
		return Task.CompletedTask;
	}
}

// ========================================
// ERROR-PRONE NOTIFICATIONS (for strategy demo)
// ========================================
public record ErrorProneNotification(string Message) : INotification;

public class FirstErrorProneHandler : INotificationHandler<ErrorProneNotification>
{
	public Task Handle(ErrorProneNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine("   [HANDLER 1] Executing successfully...");
		return Task.CompletedTask;
	}
}

public class SecondErrorProneHandler : INotificationHandler<ErrorProneNotification>
{
	public Task Handle(ErrorProneNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine("   [HANDLER 2] About to throw exception...");
		throw new InvalidOperationException("Handler 2 failed intentionally");
	}
}

public class ThirdErrorProneHandler : INotificationHandler<ErrorProneNotification>
{
	public Task Handle(ErrorProneNotification notification, CancellationToken cancellationToken)
	{
		Console.WriteLine("   [HANDLER 3] Executing successfully...");
		return Task.CompletedTask;
	}
}