using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Integrations.Connection;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Infrastructure.Modules.Arca;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>
/// El ticket compartido con PostgreSQL y FakeArca. Cada paso del proveedor abre su scope, con su DbContext y su
/// transacción, así dos pedidos en paralelo se comportan como dos hosts sobre la misma base.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaAccessTicketProviderTests(ApiFactory factory) : IDisposable
{
    private const string Wsfe = "wsfe";

    private readonly FakeArcaCertificateAuthority authority = new();

    [Fact]
    public async Task The_first_request_logs_in_once_and_the_next_reuses_the_encrypted_ticket()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            var first = Assert.IsType<ArcaTicketOutcome.Ready>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            var second = Assert.IsType<ArcaTicketOutcome.Ready>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));

            Assert.Equal(1, arca.Calls("loginCms", Wsfe));
            Assert.Equal((first.Token, first.Sign), (second.Token, second.Sign));
            Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddHours(12), first.ExpiresAtUtc, TimeSpan.FromSeconds(1));
            var row = await FindTicketAsync(certificate);
            Assert.NotNull(row?.ProtectedCredentials);
            Assert.DoesNotContain(first.Token, row.ProtectedCredentials, StringComparison.Ordinal);
            Assert.Equal(first.ExpiresAtUtc, row.ExpiresAtUtc);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task Two_hosts_asking_at_once_log_in_once_and_share_the_token()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            arca.Hold();
            var first = Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct);
            await WaitUntilAsync(() => arca.Calls("loginCms", Wsfe) == 1);
            var second = Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct);
            // El segundo encuentra la reserva del primero y espera con el reloj del sistema.
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            arca.Release();
            var firstReady = Assert.IsType<ArcaTicketOutcome.Ready>(await first);
            await AdvanceUntilAsync(second);
            var secondReady = Assert.IsType<ArcaTicketOutcome.Ready>(await second);

            Assert.Equal(1, arca.Calls("loginCms", Wsfe));
            Assert.Equal(firstReady.Token, secondReady.Token);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task An_already_authenticated_answer_blocks_the_service_for_the_environment_window()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        var window = factory.Services.GetRequiredService<IOptions<ArcaOptions>>().Value.Testing.TicketRetryWindow;
        try
        {
            arca.Fail(Wsfe, FakeArcaFailure.AlreadyAuthenticated);
            var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;

            var blocked = Assert.IsType<ArcaTicketOutcome.Blocked>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            var again = Assert.IsType<ArcaTicketOutcome.Blocked>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));

            Assert.Equal(nowUtc + window, blocked.UntilUtc);
            Assert.Equal(blocked.UntilUtc, again.UntilUtc);
            Assert.Equal(1, arca.Calls("loginCms", Wsfe));
            Assert.Equal(blocked.UntilUtc, (await FindTicketAsync(certificate))?.BlockedUntilUtc);
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_not_authorized_answer_is_returned_and_the_next_request_tries_again()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            arca.Fail(Wsfe, FakeArcaFailure.NotAuthorized);

            Assert.IsType<ArcaTicketOutcome.NotAuthorized>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            Assert.IsType<ArcaTicketOutcome.Ready>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            Assert.Equal(2, arca.Calls("loginCms", Wsfe));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_503_is_unavailable_and_releases_the_lease()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        try
        {
            arca.Fail(Wsfe, FakeArcaFailure.ServiceUnavailable);

            Assert.IsType<ArcaTicketOutcome.Unavailable>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            Assert.IsType<ArcaTicketOutcome.Ready>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            Assert.Equal(2, arca.Calls("loginCms", Wsfe));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    [Fact]
    public async Task A_timeout_is_unavailable_and_releases_the_lease()
    {
        var ct = TestContext.Current.CancellationToken;
        var arca = Arca();
        var certificate = await ArcaPersistenceScenario.AddActiveCertificateAsync(factory, authority);
        var timeout = factory.Services.GetRequiredService<IOptions<ArcaOptions>>().Value.RequestTimeout;
        try
        {
            arca.Fail(Wsfe, FakeArcaFailure.Hang);
            var pending = Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct);
            await WaitUntilAsync(() => arca.Calls("loginCms", Wsfe) == 1);
            factory.Clock.Advance(timeout + TimeSpan.FromSeconds(1));

            Assert.IsType<ArcaTicketOutcome.Unavailable>(await pending);
            Assert.IsType<ArcaTicketOutcome.Ready>(await Provider().GetAsync(certificate, ArcaPersistenceScenario.Invoicing, ct));
            Assert.Equal(2, arca.Calls("loginCms", Wsfe));
        }
        finally
        {
            await ArcaPersistenceScenario.RemoveConnectionAsync(factory);
        }
    }

    public void Dispose() => authority.Dispose();

    private FakeArca Arca()
    {
        var arca = factory.Services.GetRequiredService<FakeArca>();
        arca.Reset();
        return arca;
    }

    private IArcaAccessTicketProvider Provider() => factory.Services.GetRequiredService<IArcaAccessTicketProvider>();

    private Task<ArcaTicketRow?> FindTicketAsync(ArcaCertificateRef certificate) => factory.ExecuteScopeAsync(services =>
        services.GetRequiredService<IArcaConnectionReader>().FindTicketAsync(certificate.Id, ArcaPersistenceScenario.Invoicing, TestContext.Current.CancellationToken));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++) await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    /// <summary>Adelanta el reloj de a medio segundo, lo que espera el proveedor entre reservas, hasta que el pedido termina.</summary>
    private async Task AdvanceUntilAsync(Task pending)
    {
        for (var attempt = 0; attempt < 40 && !pending.IsCompleted; attempt++)
        {
            factory.Clock.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
    }
}
