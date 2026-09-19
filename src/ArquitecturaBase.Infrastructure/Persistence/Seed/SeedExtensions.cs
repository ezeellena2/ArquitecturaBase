using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

public static class SeedExtensions
{
    /// <summary>
    /// Crea o actualiza los datos base. Se puede correr las veces que haga falta. La Api lo llama al arrancar en
    /// desarrollo, después de las migraciones; los tests, al crear la base.
    /// </summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<RoleSeeder>().SeedAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<OpenIddictSeeder>().SeedAsync(cancellationToken);
    }
}
