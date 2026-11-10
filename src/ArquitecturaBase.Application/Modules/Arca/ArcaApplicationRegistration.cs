using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.Arca.Channels;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Channels.Registry;
using ArquitecturaBase.Application.Modules.Arca.Configuration;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Channels.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Registry;
using ArquitecturaBase.Application.Modules.Arca.Services.Connection;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Registry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Application.Modules.Arca;

/// Una línea por familia de servicios (ADR 0025): quitar una familia es borrar sus carpetas y su línea.
public static class ArcaApplicationRegistration
{
    public static IServiceCollection AddArcaApplication(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, ArcaPermissionCatalogSource>());
        AddConnection(services);
        AddRegistry(services);
        AddInvoicing(services);
        return services;
    }

    private static void AddConnection(IServiceCollection services)
    {
        services.AddOptions<ArcaPlatformOptions>().BindConfiguration("Arca:Platform")
            .Validate(x => x.IsValid(), "Configure Arca:Platform with a valid CUIT and the name the guide shows, or leave both empty.").ValidateOnStart();
        services.TryAddScoped<ArcaServiceCatalogPolicy>();
        services.TryAddScoped<ArcaServiceSessionIssuer>();
        services.TryAddScoped<IArcaAccessTicketStore, ArcaAccessTicketStore>();
        services.TryAddScoped<IArcaConnectionService, ArcaConnectionService>();
        services.TryAddScoped<IArcaConnectionTestService, ArcaConnectionTestService>();
        services.AddOptions<ArcaRobotOptions>().BindConfiguration("Arca:Robot")
            .Validate(x => x.IsValid(), "Configure Arca:Robot with positive lapses, a lease of 30 seconds or more, 1 to 20 attempts and a valid alert email, or leave it empty.")
            .ValidateOnStart();
        services.TryAddScoped<ArcaAccessModePolicy>();
        services.TryAddScoped<ArcaRobotAlertPolicy>();
        services.TryAddScoped<IArcaRobotRunStore, ArcaRobotRunStore>();
        services.TryAddScoped<IArcaRobotService, ArcaRobotService>();
    }

    private static void AddRegistry(IServiceCollection services)
    {
        services.TryAddScoped<IArcaRegistryCompanyService, ArcaRegistryCompanyService>();
        services.TryAddScoped<IArcaCompanyProfileSource, ArcaRegistryCompanyProfileSource>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IArcaServiceDefinition, ArcaRegistryServiceDefinition>());
    }

    private static void AddInvoicing(IServiceCollection services)
    {
        services.TryAddScoped<IArcaInvoicingSetupService, ArcaInvoicingSetupService>();
        services.TryAddScoped<IArcaVoucherService, ArcaVoucherService>();
        services.TryAddScoped<IArcaVoucherReviewService, ArcaVoucherReviewService>();
        services.TryAddScoped<IArcaVoucherStore, ArcaVoucherStore>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPrivateFileRetentionSource, ArcaPrivateFileRetentionSource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IArcaServiceDefinition, ArcaInvoicingServiceDefinition>());
    }
}
