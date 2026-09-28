using System.Data.Common;
using System.Net.Sockets;
using ArquitecturaBase.Infrastructure.Identity.OpenIddict;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ArquitecturaBase.Infrastructure.Persistence;

/// <summary>
/// Lo que la Api hace con la base al arrancar, según el ambiente (ADR 0006, Etapa 7 tarea 9). Vive en Infrastructure
/// porque el nombre del ambiente de los tests (<see cref="OpenIddictRegistration.TestingEnvironment"/>) es interno.
/// Fuera de Testing, lo primero es validar las opciones, lo mismo que ValidateOnStart hace al arrancar el host: este
/// código corre antes de <c>RunAsync</c>, y sin eso una configuración mal escrita recién se vería después de tocar la base
/// (o detrás de un error de la base caída), y el seed ya habría corrido con las opciones que sí eran válidas.
/// <list type="bullet">
/// <item>Development: aplica las migraciones y siembra.</item>
/// <item>Testing: nada. El arnés crea el esquema desde el modelo y siembra después; al arrancar el host todavía no hay
/// tablas.</item>
/// <item>Cualquier otro ambiente: las migraciones las aplica el bundle desde el pipeline, antes de la imagen. Si falta
/// alguna, la Api no arranca y lo dice: una base vieja no haría fallar al seed, que solo toca roles, ajustes y
/// OpenIddict. Antes de ese chequeo prueba que el servidor responda: con la base caída o un reset de conexión, EF puede
/// listar todas las migraciones como pendientes, y el error tiene que decir que la base no responde. Una base que no
/// existe sí responde (el servidor está), y cae en el chequeo de migraciones, que manda al bundle. Después siembra, en
/// cada arranque.</item>
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
            () => services.GetService<IStartupValidator>()?.Validate(),
            services.ApplyMigrationsAsync,
            ct => ProbeConnectionAsync(services, ct),
            ct => ListPendingMigrationsAsync(services, ct),
            services.SeedDatabaseAsync,
            cancellationToken);
    }

    /// <summary>La decisión por ambiente, con los pasos recibidos: la prueban tests sin base.</summary>
    internal static async Task InitializeAsync(
        IHostEnvironment environment,
        Action validateOptions,
        Func<CancellationToken, Task> migrate,
        Func<CancellationToken, Task<Exception?>> probeConnection,
        Func<CancellationToken, Task<IEnumerable<string>>> listPendingMigrations,
        Func<CancellationToken, Task> seed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(validateOptions);
        ArgumentNullException.ThrowIfNull(migrate);
        ArgumentNullException.ThrowIfNull(probeConnection);
        ArgumentNullException.ThrowIfNull(listPendingMigrations);
        ArgumentNullException.ThrowIfNull(seed);

        if (environment.IsEnvironment(OpenIddictRegistration.TestingEnvironment))
        {
            return;
        }

        validateOptions();

        if (environment.IsDevelopment())
        {
            await migrate(cancellationToken);
        }
        else
        {
            // Con un reset de conexión, EF puede creer que la base no existe y listar todas las migraciones como
            // pendientes: el error mandaría a correr el bundle cuando lo que falla es la conexión.
            if (await probeConnection(cancellationToken) is { } connectionFailure)
            {
                throw new InvalidOperationException(UnreachableDatabaseMessage, connectionFailure);
            }

            var pending = (await listPendingMigrations(cancellationToken)).ToList();

            if (pending.Count > 0)
            {
                throw new InvalidOperationException(PendingMigrationsMessage(pending));
            }
        }

        await seed(cancellationToken);
    }

    internal const string UnreachableDatabaseMessage =
        "The database does not respond: the Api could not connect to it to check the migrations (the inner exception " +
        "says why). Check the connection string and that the database server is up and reachable, and start the Api " +
        "again.";

    internal static string PendingMigrationsMessage(IEnumerable<string> pending) =>
        $"The database has pending migrations: {string.Join(", ", pending)}. Outside Development the Api does not " +
        "migrate on start: apply them with the migrations bundle (efbundle, built by 'dotnet ef migrations bundle') " +
        "and start the Api again.";

    // Null si el servidor responde; si no, la falla, que viaja como InnerException del error de arranque (una contraseña
    // mal escrita también cae acá, y la causa lo dice). No es Database.CanConnectAsync: esa devuelve false también cuando la base no existe, y
    // una base que nadie creó es la de un despliegue sin el bundle, que tiene que seguir diciendo que falta el bundle
    // (lo hace el chequeo de migraciones, que la cuenta con todas pendientes). Por eso abre la conexión y corre un
    // comando: una conexión reseteada del pool falla recién en el primer comando.
    private static async Task<Exception?> ProbeConnectionAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database;

        try
        {
            await database.OpenConnectionAsync(cancellationToken);
            await database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return null;
        }
        catch (Exception exception) when (Causes(exception).Any(IsConnectionFailure))
        {
            // Si el servidor respondió que la base no existe, responde.
            return Causes(exception).Any(cause => cause is PostgresException
            {
                SqlState: PostgresErrorCodes.InvalidCatalogName,
            })
                ? null
                : exception;
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }

    // EF envuelve una falla transitoria (la conexión rechazada, un reset) en un InvalidOperationException.
    private static IEnumerable<Exception> Causes(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private static bool IsConnectionFailure(Exception exception) =>
        exception is DbException or IOException or SocketException or TimeoutException;

    private static async Task<IEnumerable<string>> ListPendingMigrationsAsync(
        IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await dbContext.Database.GetPendingMigrationsAsync(cancellationToken);
    }
}
