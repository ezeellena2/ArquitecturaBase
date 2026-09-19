using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Interceptors;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Nombre de la base en el AppHost: Aspire inyecta ConnectionStrings:appdb.</summary>
    public const string DatabaseConnectionName = "appdb";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        // El orden importa: primero el soft delete convierte el borrado en modificación y después se audita.
        services.AddScoped<ISaveChangesInterceptor, SoftDeleteInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();

        // Única configuración del DbContext. Los tests de integración reutilizan estas opciones y solo cambian
        // el tipo de contexto y la cadena de conexión: lo que se agregue acá (por ejemplo, OpenIddict) también llega a ellos.
        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) => options
            .UseNpgsql(GetConnectionString(configuration))
            .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<ILoginCodeRepository, LoginCodeRepository>();
        services.AddScoped<ILoginAuditRepository, LoginAuditRepository>();

        services.AddOptions<LoginCodeHashOptions>()
            .BindConfiguration(LoginCodeHashOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<ILoginCodeGenerator, LoginCodeGenerator>();
        services.AddSingleton<ILoginCodeHasher, LoginCodeHasher>();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseConnectionName)
        ?? throw new InvalidOperationException(
            $"Missing connection string 'ConnectionStrings:{DatabaseConnectionName}'. Start the API from the AppHost.");
}
