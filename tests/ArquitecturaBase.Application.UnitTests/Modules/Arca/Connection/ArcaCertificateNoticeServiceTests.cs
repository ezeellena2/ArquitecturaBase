using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

// RC7-B: once the active certificate enters the days of the connection, each person who manages it gets one email in their
// language, with the date in their zone, and the certificate is marked: one notice per certificate.
public sealed class ArcaCertificateNoticeServiceTests
{
    private const string Invoicing = "wsfe";
    private static readonly ArcaNoticeRecipientRow Ana = new("ana@ejemplo.com", "es", "America/Argentina/Buenos_Aires");
    private static readonly ArcaNoticeRecipientRow John = new("john@example.com", "en", "America/New_York");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Inside_the_window_each_manager_gets_one_email_in_their_language_and_the_certificate_is_marked()
    {
        using var host = new ArcaApplicationHost();
        // The fake inspector gives two years from the upload: 2028-11-05 at 01:00 UTC, still the 4th in Buenos Aires and New York.
        host.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero));
        await host.ActivateAsync(Invoicing);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.NoticeRecipients.People.AddRange([Ana, John]);
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-30)));

        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);

        Assert.Equal(
            [("ana@ejemplo.com", "El certificado de ARCA vence el 04/11/2028"), ("john@example.com", "The ARCA certificate expires on 11/04/2028")],
            host.Emails.Messages.Select(message => (message.To, message.Subject)));
        Assert.Contains("vence el 04/11/2028. Renovalo antes en Configuración › ARCA", host.Emails.Messages[0].TextBody, StringComparison.Ordinal);
        Assert.Contains("expires on 11/04/2028. Renew it beforehand in Settings › ARCA", host.Emails.Messages[1].TextBody, StringComparison.Ordinal);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, certificate.ExpiryNoticeSentAtUtc);
    }

    [Fact]
    public async Task Before_the_window_or_after_the_expiry_nothing_is_sent()
    {
        using var host = new ArcaApplicationHost();
        await host.ActivateAsync(Invoicing);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.NoticeRecipients.People.Add(Ana);

        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-31)));
        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc.Value));
        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);

        Assert.Empty(host.Emails.Messages);
        Assert.Null(certificate.ExpiryNoticeSentAtUtc);
    }

    [Fact]
    public async Task The_notice_goes_once_per_certificate()
    {
        using var host = new ArcaApplicationHost();
        await host.ActivateAsync(Invoicing);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.NoticeRecipients.People.Add(Ana);
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-20)));

        await host.Notice.NotifyAsync(Ct);
        host.Clock.Advance(TimeSpan.FromDays(1));
        await host.Notice.NotifyAsync(Ct);

        Assert.Single(host.Emails.Messages);
    }

    [Fact]
    public async Task The_window_follows_the_days_of_the_connection()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);
        await host.Connection.ChangeCertificateNoticeAsync(60, active.Revision, Ct);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.NoticeRecipients.People.Add(Ana);
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-45)));

        await host.Notice.NotifyAsync(Ct);

        Assert.Single(host.Emails.Messages);
    }

    [Fact]
    public async Task Without_anyone_to_tell_the_certificate_stays_unmarked_for_the_next_run()
    {
        using var host = new ArcaApplicationHost();
        await host.ActivateAsync(Invoicing);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-10)));

        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);
        Assert.Null(certificate.ExpiryNoticeSentAtUtc);

        host.NoticeRecipients.People.Add(Ana);
        await host.Notice.NotifyAsync(Ct);
        Assert.Single(host.Emails.Messages);
        Assert.NotNull(certificate.ExpiryNoticeSentAtUtc);
    }

    // The emails go in the transaction that marks the certificate: if the queue refuses one, none is kept and the next run tries again.
    [Fact]
    public async Task A_full_queue_rolls_the_run_back_without_marking_the_certificate()
    {
        using var host = new ArcaApplicationHost();
        await host.ActivateAsync(Invoicing);
        var certificate = host.Store.Certificate(ArcaCertificateRole.Active)!;
        host.NoticeRecipients.People.AddRange([Ana, John]);
        host.Clock.SetUtcNow(new DateTimeOffset(certificate.NotAfterUtc!.Value.AddDays(-10)));
        host.Emails.Accepts = false;
        var rollbacks = host.Work.Rollbacks;

        Assert.Equal(ArcaConnectionErrors.ExpiryNoticeNotQueued, (await host.Notice.NotifyAsync(Ct)).Error);

        Assert.Equal(rollbacks + 1, host.Work.Rollbacks);
        Assert.Null(certificate.ExpiryNoticeSentAtUtc);
    }

    [Fact]
    public async Task Without_a_connection_or_a_loaded_certificate_nothing_happens()
    {
        using var host = new ArcaApplicationHost();
        host.NoticeRecipients.People.Add(Ana);

        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);
        await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [Invoicing], 0,
            ArcaAccessMode.CompanyCertificate, Ct);
        Assert.True((await host.Notice.NotifyAsync(Ct)).IsSuccess);

        Assert.Empty(host.Emails.Messages);
    }

    [Fact]
    public void A_recipient_never_shows_the_email_in_its_text()
    {
        Assert.DoesNotContain("ana@ejemplo.com", Ana.ToString(), StringComparison.Ordinal);
    }
}
