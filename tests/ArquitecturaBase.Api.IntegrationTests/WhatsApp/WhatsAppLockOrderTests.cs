using System.Data.Common;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// Los locks de los webhooks se toman ordenados y sin repetir. Dos webhooks simultáneos con las mismas personas en
/// distinto orden (A y B en uno, B y A en el otro) piden entonces las claves en el mismo orden, y ninguno espera al
/// otro para siempre: sin eso, Postgres corta uno con un deadlock (40P01), sale un 500 y Meta reintenta. Se mira el
/// orden de los comandos que llegan a Postgres, porque provocar el deadlock depende de cómo se crucen los pedidos.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppLockOrderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Contact_locks_are_taken_sorted_and_without_repeats()
    {
        var recorder = new AdvisoryLockRecorder();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = WithRecorder(scope.ServiceProvider, recorder);

        await new WhatsAppContactRepository(db).LockAsync(
            ["AR.9000000000000002", "AR.9000000000000001", "AR.9000000000000002"],
            ["5493519000002", "5493519000001"],
            Ct);

        Assert.Equal(
            [
                "whatsapp-contact:user:AR.9000000000000001",
                "whatsapp-contact:user:AR.9000000000000002",
                "whatsapp-contact:wa:5493519000001",
                "whatsapp-contact:wa:5493519000002",
            ],
            recorder.Keys);
    }

    [Fact]
    public async Task Message_locks_are_taken_sorted_and_without_repeats()
    {
        var recorder = new AdvisoryLockRecorder();
        await using var scope = factory.Services.CreateAsyncScope();
        await using var db = WithRecorder(scope.ServiceProvider, recorder);

        await new WhatsAppMessageRepository(db).LockAsync(["wamid.lock-order-b", "wamid.lock-order-a", "wamid.lock-order-b"], Ct);

        Assert.Equal(["whatsapp-message:wamid.lock-order-a", "whatsapp-message:wamid.lock-order-b"], recorder.Keys);
    }

    /// <summary>
    /// El contexto de producción (las mismas opciones que registra la Api) con un interceptor más. Al descartarlo se
    /// deshace la transacción que abrieron los locks, y los locks se sueltan.
    /// </summary>
    private static TestDbContext WithRecorder(IServiceProvider services, AdvisoryLockRecorder recorder) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>(services.GetRequiredService<DbContextOptions<ApplicationDbContext>>())
            .AddInterceptors(recorder)
            .Options);

    /// <summary>Las claves de <c>pg_advisory_xact_lock</c>, en el orden en que salen hacia Postgres.</summary>
    private sealed class AdvisoryLockRecorder : DbCommandInterceptor
    {
        public List<string> Keys { get; } = [];

        public override InterceptionResult<int> NonQueryExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result)
        {
            Record(command);

            return base.NonQueryExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);

            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void Record(DbCommand command)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                Keys.Add(Assert.IsType<string>(command.Parameters[0].Value));
            }
        }
    }
}
