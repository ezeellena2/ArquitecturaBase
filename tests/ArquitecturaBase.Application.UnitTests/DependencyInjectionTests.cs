using System.Reflection;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Domain.Results;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests;

public sealed class DependencyInjectionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Application_can_be_registered()
    {
        var services = new ServiceCollection();

        var exception = Record.Exception(() => services.AddApplication());

        Assert.Null(exception);
    }

    [Fact]
    public void Every_application_validator_is_registered_and_resolves_in_a_scope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddApplication();

        var validators = typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType
                    && contract.GetGenericTypeDefinition() == typeof(IValidator<>))
                .Select(contract => (Implementation: type, Contract: contract)))
            .ToArray();

        Assert.True(validators.Length >= 15, "Expected the application request validators to be discovered.");
        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        foreach (var (implementation, contract) in validators)
        {
            Assert.Contains(services, descriptor =>
                descriptor.ServiceType == contract && descriptor.ImplementationType == implementation);
            Assert.Contains(
                scope.ServiceProvider.GetServices(contract),
                validator => implementation.IsInstanceOfType(validator));
        }

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRequestValidator>());
    }

    [Fact]
    public async Task Resolved_service_validator_rejects_invalid_request()
    {
        using var provider = BuildProviderWithConfiguration([]);
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IRequestValidator>();

        var error = await validator.ValidateAsync(new CreateUserRequest("invalid", "Ana", null), Ct);

        Assert.Contains("email", Assert.IsType<ValidationError>(error).Errors.Keys);
    }

    /// <summary>
    /// Reemplaza a la antigua lista fija de servicios: deriva las interfaces de Interfaces.Services por reflexión y,
    /// además, camina el constructor de cada implementación registrada para exigir que toda dependencia de
    /// Application.Services, IRequestValidator o Interfaces.Services esté a su vez registrada. Cubre desde el día uno
    /// a cualquier servicio o helper nuevo, sin esperar a un 500 en integración.
    /// </summary>
    [Fact]
    public void Every_application_dependency_is_registered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddApplication();

        Assert.DoesNotContain(services, registration => registration.ServiceType == typeof(IWhatsAppWebhookService));

        services.AddWhatsAppWebhookApplicationServices();

        // Control positivo: el recorrido ve las dependencias de los helpers, no solo las de los servicios.
        var dependencies = TrackedDependencies(services).ToArray();
        Assert.Contains((typeof(UserAdministrationService), typeof(UserContactLinker)), dependencies);
        Assert.Contains((typeof(UserAdministrationService), typeof(IRequestValidator)), dependencies);
        Assert.Contains((typeof(UserContactLinker), typeof(PhoneNumberLinker)), dependencies);

        Assert.Empty(FindUnregisteredDependencies(services));

        var contracts = ApplicationServiceInterfaces().ToArray();
        Assert.NotEmpty(contracts);
        foreach (var contract in contracts)
        {
            var descriptor = Assert.Single(services, registration => registration.ServiceType == contract);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
    }

    /// <summary>
    /// Servicios, helpers y el <see cref="IRequestValidator"/> son scoped: uno singleton capturaría un scoped
    /// (el <see cref="IServiceProvider"/> que recibe RequestValidator sería el raíz, no el del pedido).
    /// </summary>
    [Fact]
    public void Application_services_helpers_and_the_request_validator_are_scoped()
    {
        var services = new ServiceCollection();
        services.AddApplication().AddWhatsAppWebhookApplicationServices();

        var tracked = services
            .Where(descriptor => descriptor.ImplementationType is { } implementation
                && (IsTrackedDependency(descriptor.ServiceType) || IsTrackedDependency(implementation)))
            .ToArray();

        Assert.Contains(tracked, descriptor => descriptor.ServiceType == typeof(IRequestValidator));
        Assert.Contains(tracked, descriptor => descriptor.ServiceType == typeof(UserGuard));
        Assert.All(tracked, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));
    }

    /// <summary>
    /// Caso de control: un tipo armado en memoria con una dependencia sin registrar de cada clase que se vigila
    /// (un helper de Application.Services, un contrato de Interfaces.Services y el IRequestValidator).
    /// </summary>
    [Fact]
    public void Missing_application_service_dependencies_are_detected()
    {
        var services = new ServiceCollection();
        services.AddScoped<ServiceWithMissingDependencies>();

        var missing = FindUnregisteredDependencies(services).ToArray();

        Assert.Equal(
            [
                (typeof(ServiceWithMissingDependencies), typeof(LoginCodeIssuer)),
                (typeof(ServiceWithMissingDependencies), typeof(IUserQueryService)),
                (typeof(ServiceWithMissingDependencies), typeof(IRequestValidator))
            ],
            missing);
    }

    private static IEnumerable<Type> ApplicationServiceInterfaces() =>
        typeof(DependencyInjection).Assembly.GetTypes()
            .Where(type => type.IsInterface && IsUnderNamespace(type, "ArquitecturaBase.Application.Interfaces.Services"));

    /// <summary>
    /// Toda dependencia de constructor de un tipo registrado que sea de Application.Services, IRequestValidator o
    /// Interfaces.Services, y que no esté a su vez registrada en el mismo contenedor.
    /// </summary>
    private static IEnumerable<(Type Owner, Type Dependency)> FindUnregisteredDependencies(IServiceCollection services)
    {
        var registered = services.Select(descriptor => descriptor.ServiceType).ToHashSet();

        return TrackedDependencies(services).Where(edge => !registered.Contains(edge.Dependency));
    }

    /// <summary>Las dependencias de constructor vigiladas de cada tipo registrado con su implementación.</summary>
    private static IEnumerable<(Type Owner, Type Dependency)> TrackedDependencies(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationType is not { } implementationType)
            {
                continue;
            }

            var constructor = implementationType
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .FirstOrDefault();

            if (constructor is null)
            {
                continue;
            }

            foreach (var parameter in constructor.GetParameters())
            {
                if (IsTrackedDependency(parameter.ParameterType))
                {
                    yield return (implementationType, parameter.ParameterType);
                }
            }
        }
    }

    private static bool IsTrackedDependency(Type type) =>
        type == typeof(IRequestValidator)
        || IsUnderNamespace(type, "ArquitecturaBase.Application.Services")
        || IsUnderNamespace(type, "ArquitecturaBase.Application.Interfaces.Services");

    private static bool IsUnderNamespace(Type type, string ns) =>
        type.Namespace is { } typeNamespace
        && (typeNamespace == ns || typeNamespace.StartsWith(ns + ".", StringComparison.Ordinal));

    private sealed class ServiceWithMissingDependencies(
        LoginCodeIssuer issuer,
        IUserQueryService users,
        IRequestValidator validator)
    {
        public LoginCodeIssuer Issuer { get; } = issuer;

        public IUserQueryService Users { get; } = users;

        public IRequestValidator Validator { get; } = validator;
    }

    [Fact]
    public void The_account_access_revoker_is_registered_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        var descriptor = Assert.Single(services, registration => registration.ServiceType == typeof(AccountAccessRevoker));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Login_code_options_are_read_from_configuration()
    {
        using var provider = BuildProviderWithConfiguration(new() { ["Authentication:LoginCode:Length"] = "8" });

        Assert.Equal(8, provider.GetRequiredService<IOptions<LoginCodeOptions>>().Value.Length);
    }

    [Fact]
    public void Invalid_login_code_options_are_rejected()
    {
        using var provider = BuildProviderWithConfiguration(new() { ["Authentication:LoginCode:MaxAttempts"] = "0" });

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<LoginCodeOptions>>().Value);

        Assert.Contains(nameof(LoginCodeOptions.MaxAttempts), exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildProviderWithConfiguration(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddApplication();

        return services.BuildServiceProvider();
    }
}
