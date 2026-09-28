using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Lo que la Api hace con la base al arrancar, según el ambiente (ADR 0006, Etapa 7 tarea 9). Vive en Infrastructure
/// porque el nombre del ambiente de los tests (<see cref="OpenIddictRegistration.TestingEnvironment"/>) es interno.
/// <list type="bullet">
/// <item>Development: aplica las migraciones y siembra.</item>
/// <item>Testing: nada. El arnés crea el esquema desde el modelo y siembra después; al arrancar el host todavía no hay
/// tablas.</item>
/// <item>Cualquier otro ambiente: las migraciones las aplica el bundle desde el pipeline, antes de la imagen. Si falta
/// alguna, la Api no arranca y lo dice: una base vieja no haría fallar al seed, que solo toca roles, ajustes y
/// OpenIddict. Con la base caída, el chequeo ya lanza. Después siembra, en cada arranque.</item>
/// </list>
/// Ni <c>dotnet ef</c> ni el bundle llegan acá: cortan el programa en <c>Build()</c>, antes de este código.
/// </summary>
public static class DatabaseInitialization
{
    public static Task InitializeDatabaseAsync(
        this IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        return InitializeAsync(
            environment,
            services.ApplyMigrationsAsync,
            ct => ListPendingMigrationsAsync(services, ct),
            services.SeedDatabaseAsync,
            cancellationToken);
    }

    /// <summary>La decisión por ambiente, con los pasos recibidos: la prueban tests sin base.</summary>
    internal static async Task InitializeAsync(
        IHostEnvironment environment,
        Func<CancellationToken, Task> migrate,
        Func<CancellationToken, Task<IEnumerable<string>>> listPendingMigrations,
        Func<CancellationToken, Task> seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentNullException.ThrowIfNull(listPendingMigrations);
        ArgumentNullException.ThrowIfNull(seed);

        if (environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment))
        {
            return;
        }

        if (environment.IsDevelopment())
        {
            await migrate(cancellationToken);
        }
        else
        {
            var pending = (await listPendingMigrations(cancellationToken)).ToList();

            if (pending.Count > 0)
            {
                throw new InvalidOperationException(PendingMigrationsMessage(pending));
            }
        }

        await seed(cancellationToken);
    }

    internal static string PendingMigrationsMessage(IEnumerable<string> pending) =>
        $"The database has pending migrations: {string.Join(", ", pending)}. Outside Development the Api does not " +
        "migrate on start: apply them with the migrations bundle (efbundle, built by 'dotnet ef migrations bundle') " +
        "and start the Api again.";

    private static async Task<IEnumerable<string>> ListPendingMigrationsAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
    }
}
