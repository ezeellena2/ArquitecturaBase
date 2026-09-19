using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Infrastructure.Persistence;
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

        services.AddDbContext<ApplicationDbContext>((serviceProvider, options) => options
            .UseNpgsql(GetConnectionString(configuration))
            .AddInterceptors(serviceProvider.GetServices<ISaveChangesInterceptor>()));

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }

    private static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(DatabaseConnectionName)
        ?? throw new InvalidOperationException(
            $"Missing connection string 'ConnectionStrings:{DatabaseConnectionName}'. Start the API from the AppHost.");
}
