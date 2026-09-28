using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Interceptors;
using ArquitecturaBase.Infrastructure.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Todo lo de la base en un solo lugar: el contexto con sus interceptores, el health check, la unidad de trabajo, los
/// repositorios, los lectores y los seeders. Es el único que nombra sus clases concretas (lo verifica
/// PersistenceRegistrationTests). Data Protection, el caché y el revocador de tokens usan la base pero no son
/// persistencia: se registran con lo suyo.
/// </summary>
internal static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // El orden importa: primero el soft delete convierte el borrado en modificación y después se audita.
        services.AddScoped<ISaveChangesInterceptor, SoftDeleteInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

        // Única configuración del DbContext. Los tests de integración reutilizan estas opciones y solo cambian
        // el tipo de contexto y la cadena de conexión: lo que se agregue acá (por ejemplo, OpenIddict) también llega a ellos.
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) => options
            .UseNpgsql(GetConnectionString(configuration))
            .UseOpenIddict<Guid>()
            .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));

        // Readiness: sin la base no hay nada que responder. Va sin el tag "live" a propósito, para que una base
        // caída no marque el proceso como muerto y el orquestador lo reinicie en cadena sin arreglar nada.
        services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>("database");

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ILoginCodeRepository, LoginCodeRepository>();
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();
        services.AddScoped<ILoginLinkRepository, LoginLinkRepository>();
        services.AddScoped<IWhatsAppContactRepository, WhatsAppContactRepository>();
        services.AddScoped<IWhatsAppMessageRepository, WhatsAppMessageRepository>();
        services.AddScoped<IWhatsAppMessageRetentionRepository, WhatsAppMessageRetentionRepository>();
        services.AddScoped<IUserInvitationRepository, UserInvitationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
        services.AddScoped<IRoleReader, RoleReader>();
        services.AddScoped<ISystemSettingsReader, SystemSettingsReader>();
        services.AddScoped<IUserReader, UserReader>();
        services.AddScoped<IUserInvitationReader, UserInvitationReader>();
        services.AddScoped<IPermissionReader, PermissionReader>();

        // Solo las usa SystemSettingsSeeder, que crea la fila de ajustes con Registration:Mode.
        services.AddOptions<RegistrationOptions>()
            .BindConfiguration(RegistrationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<RoleSeeder>();
        services.AddScoped<SystemSettingsSeeder>();
        services.AddScoped<OpenIddictSeeder>();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DependencyInjection.DatabaseConnectionName)
        ?? throw new InvalidOperationException(
            $"Missing connection string 'ConnectionStrings:{DependencyInjection.DatabaseConnectionName}'. Start the API from the AppHost.");
}
