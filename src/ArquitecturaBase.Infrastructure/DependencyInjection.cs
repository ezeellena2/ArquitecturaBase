using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Abstractions.Phones;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Infrastructure.Emails;
using ArquitecturaBase.Infrastructure.Identity;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Interceptors;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using ArquitecturaBase.Infrastructure.Phones;
using ArquitecturaBase.Infrastructure.Security;
using ArquitecturaBase.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la base en el AppHost: Aspire inyecta ConnectionStrings:appdb.</summary>
    public const string DatabaseConnectionName = "appdb";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.TryAddSingleton(TimeProvider.System);

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

        services.AddOptions<RegistrationOptions>()
            .BindConfiguration(RegistrationOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<ISystemSettingsRepository, SystemSettingsRepository>();
        services.AddScoped<SystemSettingsSeeder>();
        services.AddScoped<ISystemSettingsReader, SystemSettingsReader>();

        services.AddOptions<LoginCodeHashOptions>()
            .BindConfiguration(LoginCodeHashOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ILoginCodeGenerator, LoginCodeGenerator>();
        services.AddSingleton<ILoginCodeHasher, LoginCodeHasher>();

        // Sin estado propio: usa la instancia única de libphonenumber, que carga las reglas de cada país una sola vez.
        services.AddSingleton<IPhoneNumberParser, LibPhoneNumberParser>();

        services.AddIdentityServices(configuration);
        services.AddOpenIddictServer(configuration, environment);

        services.AddEmails();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseConnectionName)
        ?? throw new InvalidOperationException(
            $"Missing connection string 'ConnectionStrings:{DatabaseConnectionName}'. Start the API from the AppHost.");
}
