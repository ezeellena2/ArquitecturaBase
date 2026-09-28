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
        services.AddScoped<UserGuard>();
        services.AddScoped<DestinationCodeIssuer>();
        services.AddScoped<DestinationCodeVerifier>();
        services.AddScoped<PhoneNumberLinker>();
        services.AddScoped<AccountAccessRevoker>();
        services.AddScoped<LoginCodeIssuer>();
        services.AddScoped<SignInCodeIssuer>();
        services.AddScoped<LoginCodeVerifier>();
        services.AddScoped<LoginAuditRecorder>();
        services.AddScoped<LoginLinkIssuer>();
        services.AddScoped<LoginLinkVerifier>();
        services.AddScoped<AccountCreationPolicy>();
        services.AddScoped<WhatsAppContactLinker>();
        services.AddScoped<UserContactLinker>();
        services.AddScoped<UserInvitationIssuer>();
        services.AddScoped<WhatsAppInvitationIssuer>();
        services.AddScoped<ILoginMethodsService, LoginMethodsService>();
        services.AddScoped<ILoginCodeService, LoginCodeService>();
        services.AddScoped<IConnectService, ConnectService>();
        services.AddScoped<IExternalLoginService, ExternalLoginService>();
        services.AddScoped<ILoginLinkService, LoginLinkService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ISystemSettingsService, SystemSettingsService>();
        services.AddScoped<IUserQueryService, UserQueryService>();
        services.AddScoped<IUserAdministrationService, UserAdministrationService>();
        services.AddScoped<IUserAccessService, UserAccessService>();
        services.AddScoped<IProfileQueryService, ProfileQueryService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<IProfileWhatsAppService, ProfileWhatsAppService>();
        services.AddScoped<IWhatsAppDeliveryService, WhatsAppDeliveryService>();

        services.AddApplicationValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddScoped<IRequestValidator, RequestValidator>();

        return services;
    }

    public static IServiceCollection AddWhatsAppWebhookApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IWhatsAppWebhookPersistence, WhatsAppWebhookPersistence>();
        services.AddScoped<IWhatsAppWebhookService, WhatsAppWebhookService>();
        services.AddScoped<WhatsAppLinkIssuer>();
        services.AddScoped<WhatsAppReplyPolicy>();
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
