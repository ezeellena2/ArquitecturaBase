using ArquitecturaBase.Application.Abstractions.Behaviors;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application.UnitTests;

public sealed class DependencyInjectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Application_without_handlers_can_be_registered()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddApplication());

        Assert.Null(exception);
    }

    [Fact]
    public void Command_handler_is_wrapped_with_logging_as_the_outermost_decorator()
    {
        using var provider = BuildProvider(new FakeUnitOfWork());
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingCommand, string>>();

        Assert.IsType<LoggingDecorator.CommandHandler<PingCommand, string>>(handler);
    }

    [Fact]
    public async Task Invalid_command_is_rejected_before_saving()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingCommand, string>>();

        var result = await handler.Handle(new PingCommand(""), Ct);

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Successful_command_is_saved_once()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<PingBaseCommand>>();

        var result = await handler.Handle(new PingBaseCommand("hola"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Queries_are_validated_and_never_saved()
    {
        var unitOfWork = new FakeUnitOfWork();
        using var provider = BuildProvider(unitOfWork);
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<PingQuery, string>>();

        var invalid = await handler.Handle(new PingQuery(""), Ct);
        var valid = await handler.Handle(new PingQuery("hola"), Ct);

        Assert.IsType<ValidationError>(invalid.Error);
        Assert.Equal("pong: hola", valid.Value);
        Assert.Equal(0, unitOfWork.SaveChangesCalls);
    }

    [Fact]
    public void Decorators_are_not_registered_as_handlers()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        services.AddFeaturesFromAssembly(typeof(DependencyInjectionTests).Assembly);

        Assert.DoesNotContain(services, descriptor => ImplementationTypeOf(descriptor) is { IsGenericTypeDefinition: true });
    }

    // Los descriptores con clave (los que usa Scrutor para decorar) lanzan si se lee ImplementationType.
    private static Type? ImplementationTypeOf(ServiceDescriptor descriptor) =>
        descriptor.IsKeyedService ? descriptor.KeyedImplementationType : descriptor.ImplementationType;

    private static ServiceProvider BuildProvider(FakeUnitOfWork unitOfWork)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IUnitOfWork>(unitOfWork);

        services.AddApplication();
        services.AddFeaturesFromAssembly(typeof(DependencyInjectionTests).Assembly);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}
