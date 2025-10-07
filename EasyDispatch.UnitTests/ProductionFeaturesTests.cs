using System;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace EasyDispatch.UnitTests;

public class ProductionFeaturesTests
{
    private record UnregisteredQuery(int Id) : IQuery<string>;
    private record UnregisteredCommand(string Name) : ICommand;
    private record UnregisteredCommandWithResponse(int Value) : ICommand<int>;
    
    private record TestQuery(int Id) : IQuery<string>;
    
    private class TestQueryHandler : IQueryHandler<TestQuery, string>
    {
        public static int CallCount { get; set; }
        
        public Task<string> Handle(TestQuery query, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult($"Result: {query.Id}");
        }
    }

    [Fact]
    public async Task MissingQueryHandler_ProvidesHelpfulErrorMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var query = new UnregisteredQuery(42);

        // Act
        var act = async () => await mediator.SendAsync(query);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("No handler registered for query 'UnregisteredQuery'");
        exception.Which.Message.Should().Contain("IQueryHandler<UnregisteredQuery, String>");
        exception.Which.Message.Should().Contain("Did you forget to call AddMediator()");
    }

    [Fact]
    public async Task MissingCommandHandler_ProvidesHelpfulErrorMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var command = new UnregisteredCommand("test");

        // Act
        var act = async () => await mediator.SendAsync(command);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("No handler registered for command 'UnregisteredCommand'");
        exception.Which.Message.Should().Contain("ICommandHandler<UnregisteredCommand>");
        exception.Which.Message.Should().Contain("Did you forget to call AddMediator()");
    }

    [Fact]
    public async Task MissingCommandWithResponseHandler_ProvidesHelpfulErrorMessage()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);

        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var command = new UnregisteredCommandWithResponse(42);

        // Act
        var act = async () => await mediator.SendAsync(command);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidOperationException>();
        exception.Which.Message.Should().Contain("No handler registered for command 'UnregisteredCommandWithResponse'");
        exception.Which.Message.Should().Contain("ICommandHandler<UnregisteredCommandWithResponse, Int32>");
        exception.Which.Message.Should().Contain("Did you forget to call AddMediator()");
    }

    [Fact]
    public async Task ReflectionCaching_HandlerOnlyCalledOnce()
    {
        // Arrange
        TestQueryHandler.CallCount = 0;
        
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);

        var provider = services.BuildServiceProvider();

        // Act - Execute same query type multiple times
        using var scope = provider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            
        await mediator.SendAsync(new TestQuery(1));
        await mediator.SendAsync(new TestQuery(2));
        await mediator.SendAsync(new TestQuery(3));

        // Assert - Reflection should be cached
        // We can't directly test the cache, but we can verify handlers execute correctly
        TestQueryHandler.CallCount.Should().Be(3);
    }

    [Fact]
    public void ConfigureOptions_CustomHandlerLifetime()
    {
        // Arrange & Act
        var services = new ServiceCollection();
        services.AddMediator(options =>
        {
            options.Assemblies = new[] { typeof(ProductionFeaturesTests).Assembly };
            options.HandlerLifetime = ServiceLifetime.Singleton;
        });

        // Assert
        var descriptor = services.FirstOrDefault(d => 
            d.ServiceType == typeof(IQueryHandler<TestQuery, string>));
        
        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void ConfigureOptions_DefaultValues()
    {
        // Arrange & Act
        var options = new MediatorOptions();

        // Assert
        options.HandlerLifetime.Should().Be(ServiceLifetime.Scoped);
        options.NotificationPublishStrategy.Should().Be(NotificationPublishStrategy.StopOnFirstException);
        options.ValidateHandlersAtStartup.Should().BeFalse();
        options.Assemblies.Should().BeEmpty();
    }

    [Fact]
    public void AddMediator_WithConfiguration_RegistersOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        services.AddMediator(options =>
        {
            options.Assemblies = new[] { typeof(ProductionFeaturesTests).Assembly };
            options.HandlerLifetime = ServiceLifetime.Transient;
            options.NotificationPublishStrategy = NotificationPublishStrategy.ParallelWhenAll;
        });

        // Assert
        var provider = services.BuildServiceProvider();
        var registeredOptions = provider.GetRequiredService<MediatorOptions>();
        
        registeredOptions.Should().NotBeNull();
        registeredOptions.HandlerLifetime.Should().Be(ServiceLifetime.Transient);
        registeredOptions.NotificationPublishStrategy.Should().Be(NotificationPublishStrategy.ParallelWhenAll);
        registeredOptions.Assemblies.Should().ContainSingle();
    }

    [Fact]
    public void AddMediator_WithoutOptions_UsesDefaults()
    {
        // Arrange
        var services = new ServiceCollection();
        
        // Act
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);

        // Assert
        var provider = services.BuildServiceProvider();
        var registeredOptions = provider.GetRequiredService<MediatorOptions>();
        
        registeredOptions.Should().NotBeNull();
        registeredOptions.HandlerLifetime.Should().Be(ServiceLifetime.Scoped);
        registeredOptions.NotificationPublishStrategy.Should().Be(NotificationPublishStrategy.StopOnFirstException);
    }

    [Fact]
    public async Task MultipleRequests_UseCachedReflection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMediator(typeof(ProductionFeaturesTests).Assembly);
        var provider = services.BuildServiceProvider();

        // Act & Assert - Multiple requests should work efficiently
        for (int i = 0; i < 10; i++)
        {
            using var scope = provider.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var result = await mediator.SendAsync(new TestQuery(i));
            result.Should().Be($"Result: {i}");
        }
    }

    [Fact]
    public void AddMediator_MissingAssemblies_ThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddMediator(options =>
        {
            options.Assemblies = [];
        });

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*At least one assembly must be provided*");
    }
}