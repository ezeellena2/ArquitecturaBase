using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.Services.WhatsApp;
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
            Assert.NotNull(scope.ServiceProvider.GetRequiredService(
                typeof(ServiceRequestValidator<>).MakeGenericType(contract.GenericTypeArguments[0])));
        }
    }

    [Fact]
    public async Task Resolved_service_validator_rejects_invalid_request()
    {
        using var provider = BuildProviderWithConfiguration([]);
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<ServiceRequestValidator<CreateUserRequest>>();

        var error = await validator.ValidateAsync(new CreateUserRequest("invalid", "Ana", null), Ct);

        Assert.Contains("email", Assert.IsType<ValidationError>(error).Errors.Keys);
    }

    [Fact]
    public void Application_services_are_registered_explicitly_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddApplication();

        (Type Contract, Type Implementation)[] cases =
        [
            (typeof(IAccountService), typeof(AccountService)),
            (typeof(IConnectService), typeof(ConnectService)),
            (typeof(IExternalLoginService), typeof(ExternalLoginService)),
            (typeof(ILoginLinkService), typeof(LoginLinkService)),
            (typeof(IRoleService), typeof(RoleService)),
            (typeof(ISystemSettingsService), typeof(SystemSettingsService)),
            (typeof(IUserService), typeof(UserService)),
            (typeof(IProfileService), typeof(ProfileService)),
            (typeof(IWhatsAppDeliveryService), typeof(WhatsAppDeliveryService))
        ];

        foreach (var (contract, implementation) in cases)
        {
            var descriptor = Assert.Single(services, registration => registration.ServiceType == contract);
            Assert.Equal(implementation, descriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }

        Assert.DoesNotContain(services, registration => registration.ServiceType == typeof(IWhatsAppWebhookService));

        services.AddWhatsAppWebhookApplicationServices();

        (Type Contract, Type Implementation)[] webhookCases =
        [
            (typeof(IWhatsAppWebhookPersistence), typeof(WhatsAppWebhookPersistence)),
            (typeof(IWhatsAppWebhookService), typeof(WhatsAppWebhookService)),
            (typeof(IWhatsAppInboundService), typeof(WhatsAppInboundService))
        ];

        foreach (var (contract, implementation) in webhookCases)
        {
            var descriptor = Assert.Single(services, registration => registration.ServiceType == contract);
            Assert.Equal(implementation, descriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        }
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
