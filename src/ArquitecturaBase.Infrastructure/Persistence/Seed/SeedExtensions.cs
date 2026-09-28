using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Infrastructure.Persistence.Seed;

public static class SeedExtensions
{
    /// <summary>
    /// Crea o actualiza los datos base. Se puede correr las veces que haga falta. La Api lo llama al arrancar en
    /// desarrollo, después de las migraciones; los tests, al crear la base. Es idempotente: crea lo que falta, y alinea el
    /// cliente y el scope de OpenIddict con la configuración, así que también puede sacarles lo que ya no está
    /// configurado. Corre en un scope propio con <see cref="DatabaseSeeder"/>: todo en una transacción y en fila entre
    /// réplicas con el lock "seed:database". Si algo lanza, no queda nada y la excepción sale tal cual.
    /// </summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync(cancellationToken);
    }
}
