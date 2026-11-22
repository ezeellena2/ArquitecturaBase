using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Connection;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.Arca.Connection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.Arca.Connection;

/// <summary>
/// RC7-B con PostgreSQL: quiénes reciben el aviso de vencimiento del certificado y el worker de punta a punta, con la cola de
/// correos real. El worker está apagado en <see cref="ArcaTestModule"/>: cada test corre una vuelta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class ArcaCertificateNoticeTests(ApiFactory factory) : IDisposable
{
    private readonly FakeArcaCertificateAuthority authority = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ArcaCertificateNoticeBackgroundService Worker =>
        factory.Services.GetServices<IHostedService>().OfType<ArcaCertificateNoticeBackgroundService>().Single();

    [Fact]
    public async Task Only_active_accounts_that_manage_the_connection_with_a_confirmed_email_receive_it()
    {
        var unconfirmed = await AccountAsync([ArcaPermissions.Manage], confirmed: false);
        var reader = await AccountAsync([ArcaPermissions.Read]);
        var inactive = await AccountAsync([ArcaPermissions.Manage], active: false);
        var manager = await AccountAsync([ArcaPermissions.Manage]);

        var recipients = await factory.ExecuteScopeAsync(async services =>
            await services.GetRequiredService<IArcaNoticeRecipientReader>().ListManagersAsync(Ct));

        var emails = recipients.Select(recipient => recipient.Email).ToArray();
        Assert.Contains(manager, emails);
        Assert.DoesNotContain(unconfirmed, emails);
        Assert.DoesNotContain(reader, emails);
        Assert.DoesNotContain(inactive, emails);
        var ana = recipients.Single(recipient => recipient.Email == manager);
        Assert.Equal(("es", "America/Argentina/Buenos_Aires"), (ana.Culture, ana.TimeZoneId));
    }

    [Fact]
    public async Task Inside_the_window_the_worker_mails_whoever_manages_the_connection_once_and_marks_the_certificate()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.StartAsync([]);
        await UploadExpiringInAsync(scenario, TimeSpan.FromDays(10));
        var manager = await AccountAsync([ArcaPermissions.Manage]);
        var before = factory.EmailSender.CountFor(manager);

        await Worker.RunAsync(Ct);

        var notice = await factory.EmailSender.WaitForAsync(manager, before + 1);
        Assert.StartsWith("El certificado de ARCA vence el ", notice.Subject, StringComparison.Ordinal);
        Assert.Contains("Renovalo antes en Configuración › ARCA", notice.TextBody, StringComparison.Ordinal);
        var sentAt = await NoticeSentAtAsync();
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, sentAt);

        factory.Clock.Advance(TimeSpan.FromHours(1));
        await Worker.RunAsync(Ct);
        Assert.Equal(sentAt, await NoticeSentAtAsync());
    }

    [Fact]
    public async Task Before_the_days_of_the_connection_the_worker_sends_nothing()
    {
        await using var scenario = await ArcaHttpScenario.CreateAsync(factory, authority);
        await scenario.StartAsync([]);
        await UploadExpiringInAsync(scenario, TimeSpan.FromDays(40));

        await Worker.RunAsync(Ct);

        Assert.Null(await NoticeSentAtAsync());
    }

    public void Dispose() => authority.Dispose();

    /// <summary>Baja el pedido pendiente y sube un certificado de la CA de prueba que vence dentro de <paramref name="lifetime"/>.</summary>
    private async Task UploadExpiringInAsync(ArcaHttpScenario scenario, TimeSpan lifetime)
    {
        using var request = await scenario.Client.GetAsync("/api/arca/connection/certificate-requests/current", Ct);
        var pem = await request.Content.ReadAsStringAsync(Ct);
        var certificate = authority.Issue(pem, ArcaEnvironment.Testing, factory.Clock.GetUtcNow().UtcDateTime.Add(lifetime));
        var revision = (await scenario.GetConnectionAsync()).GetProperty("revision").GetInt64();
        await scenario.ReadAsync(await scenario.Client.PutAsJsonAsync("/api/arca/connection/certificate", new { certificate, expectedRevision = revision }, Ct),
            HttpStatusCode.OK);
    }

    private Task<DateTime?> NoticeSentAtAsync() => factory.InTransactionAsync(async services =>
    {
        var repository = services.GetRequiredService<IArcaConnectionRepository>();
        var connection = await repository.GetConnectionAsync(Ct);
        return (await repository.GetCertificateAsync(connection!.Id, ArcaCertificateRole.Active, Ct))?.ExpiryNoticeSentAtUtc;
    });

    /// <summary>Una cuenta con un rol propio que da <paramref name="permissions"/>; devuelve su correo.</summary>
    private Task<string> AccountAsync(IReadOnlyCollection<string> permissions, bool confirmed = true, bool active = true) =>
        factory.InTransactionAsync(async services =>
        {
            var role = "arca-notice-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, permissions, Ct);
            var users = services.GetRequiredService<IUserRepository>();
            var email = Email.Create(TestEmails.Unique("arca-notice")).Value;
            var user = confirmed ? await users.CreateAsync(email, null, false, "Ana", "es", Ct) : await users.CreateUnverifiedAsync(email, null, "Ana", "es", Ct);
            await users.SetRolesAsync(user.Id, [role], Ct);
            if (!active) await users.SetActiveAsync(user.Id, false, Ct);
            return email.Value;
        });
}
