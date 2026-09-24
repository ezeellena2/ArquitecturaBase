using ArquitecturaBase.Application.Configuration.Auth;
using System.Reflection;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Services.Settings;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.Services.WhatsApp;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<LoginCodeOptions>()
            .BindConfiguration(LoginCodeOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LoginLinkOptions>()
            .BindConfiguration(LoginLinkOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<WhatsAppLoginOptions>()
            .BindConfiguration(WhatsAppLoginOptions.SectionName)
            .ValidateDataAnnotations()
            .Validate(options => options.HasValidCountries(), WhatsAppLoginOptions.AllowedCountriesError)
            .ValidateOnStart();

        // Los helpers y casos de uso se registran explícitamente para mantener visible la composición.
        services.AddScoped<UserGuards>();
        services.AddScoped<DestinationCodeVerifier>();
        services.AddScoped<PhoneNumberChange>();
        services.AddScoped<LoginCodeIssuer>();
        services.AddScoped<LoginCodeVerifier>();
        services.AddScoped<LoginLinkIssuer>();
        services.AddScoped<AccountCreationPolicy>();
        services.AddScoped<WhatsAppContactLinker>();
        services.AddScoped<UserContactParser>();
        services.AddScoped<UserInvitationSender>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IConnectService, ConnectService>();
        services.AddScoped<IExternalLoginService, ExternalLoginService>();
        services.AddScoped<ILoginLinkService, LoginLinkService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<UserWriteOperations>();
        services.AddScoped<UserStatusOperations>();
        services.AddScoped<UserPhoneOperations>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ProfileEmailOperations>();
        services.AddScoped<ProfileWhatsAppOperations>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IWhatsAppDeliveryService, WhatsAppDeliveryService>();

        services.AddApplicationValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped(typeof(ServiceRequestValidator<>));

        return services;
    }

    public static IServiceCollection AddWhatsAppWebhookApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IWhatsAppWebhookPersistence, WhatsAppWebhookPersistence>();
        services.AddScoped<IWhatsAppWebhookService, WhatsAppWebhookService>();
        services.AddScoped<IWhatsAppInboundService, WhatsAppInboundService>();

        return services;
    }

    public static IServiceCollection AddApplicationValidatorsFromAssembly(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }

}
