using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

public class ServiceCollectionExtensionsTests
{
    // Sample handlers for testing registration
    private record SampleQuery(int Id) : IQuery<string>;
    private record SampleVoidCommand(string Name) : ICommand;
    private record SampleCommandWithResponse(int Value) : ICommand<int>;
    private record SampleNotification(string Message) : INotification;

    private class SampleQueryHandler : IQueryHandler<SampleQuery, string>
    {
        public Task<string> Handle(SampleQuery query, CancellationToken cancellationToken)
        {
            return Task.FromResult($"Result: {query.Id}");
        }
    }

    private class SampleVoidCommandHandler : ICommandHandler<SampleVoidCommand>
    {
        public Task Handle(SampleVoidCommand command, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private class SampleCommandWithResponseHandler : ICommandHandler<SampleCommandWithResponse, int>
    {
        public Task<int> Handle(SampleCommandWithResponse command, CancellationToken cancellationToken)
        {
            return Task.FromResult(command.Value);
        }
    }

    private class SampleNotificationHandler : INotificationHandler<SampleNotification>
    {
        public Task Handle(SampleNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddMediator_RegistersIMediator()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetService<IMediator>();
        
        mediator.Should().NotBeNull();
        mediator.Should().BeOfType<Mediator>();
    }

    [Fact]
    public void AddMediator_ThrowsWhenServicesIsNull()
    {
        // Arrange
        IServiceCollection services = null!;
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        var act = () => services.AddMediator(assembly);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("services");
    }

    [Fact]
    public void AddMediator_RegistersQueryHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var handler = provider.GetService<IQueryHandler<SampleQuery, string>>();
        
        handler.Should().NotBeNull();
        handler.Should().BeOfType<SampleQueryHandler>();
    }

    [Fact]
    public void AddMediator_RegistersVoidCommandHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var handler = provider.GetService<ICommandHandler<SampleVoidCommand>>();
        
        handler.Should().NotBeNull();
        handler.Should().BeOfType<SampleVoidCommandHandler>();
    }

    [Fact]
    public void AddMediator_RegistersCommandWithResponseHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var handler = provider.GetService<ICommandHandler<SampleCommandWithResponse, int>>();
        
        handler.Should().NotBeNull();
        handler.Should().BeOfType<SampleCommandWithResponseHandler>();
    }

    [Fact]
    public void AddMediator_RegistersNotificationHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<INotificationHandler<SampleNotification>>();
        
        handlers.Should().NotBeEmpty();
        handlers.Should().Contain(h => h.GetType() == typeof(SampleNotificationHandler));
    }

    [Fact]
    public void AddMediator_RegistersMultipleNotificationHandlers()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<INotificationHandler<SampleNotification>, SampleNotificationHandler>();
        services.AddScoped<INotificationHandler<SampleNotification>, AnotherSampleNotificationHandler>();
        services.AddScoped<IMediator, Mediator>();

        // Act
        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<INotificationHandler<SampleNotification>>();

        // Assert
        handlers.Should().HaveCount(2);
    }

    private class AnotherSampleNotificationHandler : INotificationHandler<SampleNotification>
    {
        public Task Handle(SampleNotification notification, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddMediator_ScansMultipleAssemblies()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly1 = typeof(ServiceCollectionExtensionsTests).Assembly;
        var assembly2 = typeof(Mediator).Assembly; // Different assembly

        // Act
        var act = () => services.AddMediator(assembly1, assembly2);

        // Assert
        act.Should().NotThrow();
        
        var provider = services.BuildServiceProvider();
        provider.GetService<IMediator>().Should().NotBeNull();
    }

    [Fact]
    public void AddMediator_ReturnsBuilderForFluentConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        var builder = services.AddMediator(assembly);

        // Assert
        builder.Should().NotBeNull();
        builder.Should().BeAssignableTo<IMediatorBuilder>();
        builder.Services.Should().BeSameAs(services);
    }

    [Fact]
    public void AddBehavior_RegistersSpecificBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly)
            .AddBehavior<SampleQuery, string, TestBehavior>();

        // Assert
        var provider = services.BuildServiceProvider();
        var behavior = provider.GetService<IPipelineBehavior<SampleQuery, string>>();
        
        behavior.Should().NotBeNull();
        behavior.Should().BeOfType<TestBehavior>();
    }

    private class TestBehavior : IPipelineBehavior<SampleQuery, string>
    {
        public Task<string> Handle(SampleQuery message, Func<Task<string>> next, CancellationToken cancellationToken)
        {
            return next();
        }
    }

    [Fact]
    public void AddOpenBehavior_RegistersOpenGenericBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly)
            .AddOpenBehavior(typeof(LoggingBehavior<,>));

        // Assert
        var provider = services.BuildServiceProvider();
        var behaviors = provider.GetServices<IPipelineBehavior<SampleQuery, string>>();
        
        behaviors.Should().NotBeEmpty();
    }

    private class LoggingBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, TResponse>
    {
        public async Task<TResponse> Handle(TMessage message, Func<Task<TResponse>> next, CancellationToken cancellationToken)
        {
            return await next();
        }
    }

    [Fact]
    public void AddOpenBehavior_ThrowsWhenTypeIsNull()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;
        var builder = services.AddMediator(assembly);

        // Act
        var act = () => builder.AddOpenBehavior(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("openBehaviorType");
    }

    [Fact]
    public void AddOpenBehavior_ThrowsWhenTypeIsNotGeneric()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;
        var builder = services.AddMediator(assembly);

        // Act
        var act = () => builder.AddOpenBehavior(typeof(NonGenericBehavior));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must be an open generic type*");
    }

    private class NonGenericBehavior : IPipelineBehavior<SampleQuery, string>
    {
        public Task<string> Handle(SampleQuery message, Func<Task<string>> next, CancellationToken cancellationToken)
        {
            return next();
        }
    }

    [Fact]
    public void AddOpenBehavior_ThrowsWhenTypeDoesNotImplementIPipelineBehavior()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;
        var builder = services.AddMediator(assembly);

        // Act
        var act = () => builder.AddOpenBehavior(typeof(InvalidOpenGeneric<,>));

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*must implement IPipelineBehavior*");
    }

    private class InvalidOpenGeneric<T1, T2>
    {
    }

    [Fact]
    public void AddMediator_HandlersHaveScopedLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var descriptor = services.FirstOrDefault(d => 
            d.ServiceType == typeof(IQueryHandler<SampleQuery, string>));
        
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddMediator_MediatorHasScopedLifetime()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        // Act
        services.AddMediator(assembly);

        // Assert
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IMediator));
        
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public async Task AddMediator_EndToEnd_WorksCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        var assembly = typeof(ServiceCollectionExtensionsTests).Assembly;

        services.AddMediator(assembly);

        var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var query = new SampleQuery(42);

        // Act
        var result = await mediator.SendAsync(query);

        // Assert
        result.Should().Be("Result: 42");
    }

    [Fact]
    public void AddMediator_ManuallyRegisterMultipleHandlers_WorksCorrectly()
    {
        // Arrange - Test that manually registering multiple handlers works
        var services = new ServiceCollection();
        services.AddScoped<INotificationHandler<SampleNotification>, SampleNotificationHandler>();
        services.AddScoped<INotificationHandler<SampleNotification>, AnotherSampleNotificationHandler>();
        services.AddScoped<IMediator, Mediator>();

        // Act
        var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<INotificationHandler<SampleNotification>>().ToList();

        // Assert
        handlers.Should().HaveCount(2);
        handlers.Select(h => h.GetType()).Should().Contain([
            typeof(SampleNotificationHandler), 
            typeof(AnotherSampleNotificationHandler)
        ]);
    }

    [Fact]
    public void AddMediator_WithoutParameters_UsesCallingAssembly()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act - The parameterless overload should use the calling assembly
        var builder = services.AddMediator();

        // Assert
        builder.Should().NotBeNull();
        var provider = services.BuildServiceProvider();
        var mediator = provider.GetService<IMediator>();
        mediator.Should().NotBeNull();
    }

    [Fact]
    public void AddMediator_WithEmptyAssemblyArray_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        var emptyAssemblies = Array.Empty<Assembly>();

        // Act
        var act = () => services.AddMediator(emptyAssemblies);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one assembly must be provided*")
            .WithParameterName("assemblies");
    }
}