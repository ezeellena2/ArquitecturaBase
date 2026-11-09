using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

public sealed class ArcaConnectionServiceTests
{
    private const string Registry = "ws_sr_constancia_inscripcion";
    private const string Invoicing = "wsfe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_connection_the_screen_gets_the_installed_services_and_revision_zero()
    {
        using var host = new ArcaApplicationHost();

        var connection = (await host.Connection.GetAsync(Ct)).Value;

        Assert.Null(connection.Status);
        Assert.Equal(0, connection.Revision);
        Assert.Equal([new(Registry, "Registry", true, false), new(Invoicing, "Invoicing", false, true)], connection.InstalledServices);
    }

    [Fact]
    public async Task Starting_creates_the_connection_with_a_pending_request()
    {
        using var host = new ArcaApplicationHost();

        var result = await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [Invoicing], 0, ArcaAccessMode.CompanyCertificate, Ct);

        var connection = result.Value;
        Assert.Equal((ArcaConnectionStatus.AwaitingCertificate, "20000000060", 1L), (connection.Status, connection.Cuit, connection.Revision));
        Assert.Equal([Registry, Invoicing], connection.EnabledServices);
        var certificate = Assert.Single(connection.Certificates);
        Assert.Equal((ArcaCertificateRole.Pending, "plataforma", false), (certificate.Role, certificate.Alias, certificate.IsLoaded));
        var stored = Assert.Single(host.Store.Certificates);
        Assert.Equal(("CSR:plataforma", $"protected:{stored.Id}"), (stored.RequestPem, stored.ProtectedPrivateKey));
        Assert.Equal(new("plataforma", "CSR:plataforma"), (await host.Connection.GetPendingRequestAsync(Ct)).Value);
    }

    [Fact]
    public async Task Starting_with_a_service_that_is_not_installed_or_an_invalid_alias_creates_nothing()
    {
        using var host = new ArcaApplicationHost();

        var notInstalled = await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", ["wsmtxca"], 0, ArcaAccessMode.CompanyCertificate, Ct);
        var invalidAlias = await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "1-alias", [], 0, ArcaAccessMode.CompanyCertificate, Ct);

        Assert.Equal(ArcaConnectionErrors.ServiceNotInstalled, notInstalled.Error);
        Assert.Equal(ArcaConnectionErrors.AliasInvalid, invalidAlias.Error);
        Assert.Null(host.Store.Connection);
        Assert.Empty(host.Store.Certificates);
    }

    [Fact]
    public async Task Starting_on_an_active_connection_only_adds_the_renewal_request()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);

        var renewal = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "renovacion", [], active.Revision, ArcaAccessMode.CompanyCertificate, Ct)).Value;

        Assert.Equal(ArcaConnectionStatus.Active, renewal.Status);
        Assert.Equal(active.Revision + 1, renewal.Revision);
        Assert.Equal([Registry, Invoicing], renewal.EnabledServices);
        Assert.Equal([(ArcaCertificateRole.Active, "plataforma"), (ArcaCertificateRole.Pending, "renovacion")],
            renewal.Certificates.Select(certificate => (certificate.Role, certificate.Alias)));
    }

    [Fact]
    public async Task Starting_again_replaces_the_pending_request()
    {
        using var host = new ArcaApplicationHost();
        var first = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [], 0, ArcaAccessMode.CompanyCertificate, Ct)).Value;

        var second = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "otro-alias", [], first.Revision, ArcaAccessMode.CompanyCertificate, Ct)).Value;

        Assert.Equal("otro-alias", Assert.Single(second.Certificates).Alias);
        Assert.Equal(ArcaConnectionStatus.AwaitingCertificate, second.Status);
    }

    [Fact]
    public async Task Choosing_services_saves_them_and_keeps_the_required_ones()
    {
        using var host = new ArcaApplicationHost();
        var started = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [Invoicing], 0, ArcaAccessMode.CompanyCertificate, Ct)).Value;

        var chosen = (await host.Connection.ChooseServicesAsync([], started.Revision, Ct)).Value;

        Assert.Equal([Registry], chosen.EnabledServices);
        Assert.Equal(ArcaConnectionErrors.ServiceNotInstalled, (await host.Connection.ChooseServicesAsync(["wsmtxca"], chosen.Revision, Ct)).Error);
    }

    [Fact]
    public async Task A_stale_revision_is_a_conflict_that_changes_nothing()
    {
        using var host = new ArcaApplicationHost();
        var started = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [Invoicing], 0, ArcaAccessMode.CompanyCertificate, Ct)).Value;

        Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await host.Connection.ChooseServicesAsync([], started.Revision - 1, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await host.Connection.UploadCertificateAsync("CERT", started.Revision - 1, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.RevisionConflict, (await host.Connection.DisconnectAsync(started.Revision - 1, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.RevisionConflict,
            (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [], 0, ArcaAccessMode.CompanyCertificate, Ct)).Error);
        Assert.Equal(started.Revision, host.Store.Connection!.Revision);
        Assert.Equal([Registry, Invoicing], host.Store.Connection.EnabledServices);
    }

    [Fact]
    public async Task An_invalid_certificate_returns_the_inspector_error_and_changes_nothing()
    {
        using var host = new ArcaApplicationHost();
        var started = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [], 0, ArcaAccessMode.CompanyCertificate, Ct)).Value;
        host.Credentials.InspectError = ArcaConnectionErrors.CertificateKeyMismatch;

        var result = await host.Connection.UploadCertificateAsync("OTHER", started.Revision, Ct);

        Assert.Equal(ArcaConnectionErrors.CertificateKeyMismatch, result.Error);
        Assert.False(host.Store.Certificate(ArcaCertificateRole.Pending)!.IsLoaded);
        Assert.Equal((ArcaConnectionStatus.AwaitingCertificate, started.Revision), (host.Store.Connection!.Status, host.Store.Connection.Revision));
    }

    [Fact]
    public async Task A_valid_certificate_in_the_alta_becomes_active_and_leaves_the_connection_unverified()
    {
        using var host = new ArcaApplicationHost();

        var connection = await host.StartAndUploadAsync(Invoicing);

        Assert.Equal(ArcaConnectionStatus.Unverified, connection.Status);
        var certificate = Assert.Single(connection.Certificates);
        Assert.Equal((ArcaCertificateRole.Active, true), (certificate.Role, certificate.IsLoaded));
        Assert.Equal(ArcaApplicationHost.NowUtc.AddYears(2), certificate.NotAfterUtc);
        Assert.Equal("PEM:CERT", host.Store.Certificate(ArcaCertificateRole.Active)!.CertificatePem);
    }

    [Fact]
    public async Task A_valid_certificate_in_a_renewal_stays_pending_without_replacing_the_active_one()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);

        var renewal = await host.LoadRenewalAsync(active);

        Assert.Equal(ArcaConnectionStatus.Active, renewal.Status);
        Assert.Equal([(ArcaCertificateRole.Active, "plataforma", true), (ArcaCertificateRole.Pending, "renovacion", true)],
            renewal.Certificates.Select(certificate => (certificate.Role, certificate.Alias, certificate.IsLoaded)));
        Assert.Equal("PEM:CERT", host.Store.Certificate(ArcaCertificateRole.Active)!.CertificatePem);
    }

    [Fact]
    public async Task Cancelling_the_renewal_removes_the_pending_request()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);
        var renewal = await host.LoadRenewalAsync(active);

        var cancelled = (await host.Connection.CancelRenewalAsync(renewal.Revision, Ct)).Value;

        Assert.Equal(ArcaCertificateRole.Active, Assert.Single(cancelled.Certificates).Role);
        Assert.Equal(ArcaConnectionErrors.NoPendingRequest, (await host.Connection.CancelRenewalAsync(cancelled.Revision, Ct)).Error);
        Assert.Equal(ArcaConnectionErrors.NoPendingRequest, (await host.Connection.GetPendingRequestAsync(Ct)).Error);
    }

    [Fact]
    public async Task Disconnecting_removes_everything()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);

        Assert.True((await host.Connection.DisconnectAsync(active.Revision, Ct)).IsSuccess);

        Assert.Null(host.Store.Connection);
        Assert.Empty(host.Store.Certificates);
        Assert.Null((await host.Connection.GetAsync(Ct)).Value.Status);
        Assert.Equal(ArcaConnectionErrors.NotConnected, (await host.Connection.DisconnectAsync(0, Ct)).Error);
    }

    [Fact]
    public async Task The_screen_gets_the_configured_platform_and_which_services_the_company_delegates()
    {
        using var host = new ArcaApplicationHost(withPlatform: true);
        using var without = new ArcaApplicationHost();

        var connection = (await host.Connection.GetAsync(Ct)).Value;

        Assert.Equal(new(ArcaApplicationHost.PlatformCuit, ArcaApplicationHost.PlatformName), connection.Platform);
        Assert.Equal([new(Registry, "Registry", true, false), new(Invoicing, "Invoicing", false, true)], connection.InstalledServices);
        Assert.Null((await without.Connection.GetAsync(Ct)).Value.Platform);
    }

    [Fact]
    public async Task Starting_by_delegation_keeps_the_company_cuit_and_requests_the_certificate_for_the_platform()
    {
        using var host = new ArcaApplicationHost(withPlatform: true);

        var started = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma-lostilos",
            [Invoicing], 0, ArcaAccessMode.PlatformDelegation, Ct)).Value;

        Assert.Equal((ArcaAccessMode.PlatformDelegation, "20000000060", ArcaApplicationHost.PlatformCuit),
            (started.Mode, started.Cuit, started.PlatformCuit));
        Assert.Equal([ArcaApplicationHost.PlatformCuit], host.Credentials.RequestedCuits);

        var uploaded = (await host.Connection.UploadCertificateAsync("CERT", started.Revision, Ct)).Value;
        Assert.Equal(ArcaConnectionStatus.Unverified, uploaded.Status);
        Assert.Equal([ArcaApplicationHost.PlatformCuit], host.Credentials.InspectedCuits);

        var renewal = (await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma-lostilos",
            [], uploaded.Revision, ArcaAccessMode.CompanyCertificate, Ct)).Value;
        Assert.Equal(ArcaAccessMode.PlatformDelegation, renewal.Mode);
        Assert.Equal([ArcaApplicationHost.PlatformCuit, ArcaApplicationHost.PlatformCuit], host.Credentials.RequestedCuits);
    }

    [Fact]
    public async Task Delegation_without_the_platform_configured_or_by_robot_creates_nothing()
    {
        using var host = new ArcaApplicationHost();
        using var configured = new ArcaApplicationHost(withPlatform: true);

        var unconfigured = await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma",
            [Invoicing], 0, ArcaAccessMode.PlatformDelegation, Ct);
        var automated = await configured.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Production, "plataforma",
            [Invoicing], 0, ArcaAccessMode.PlatformDelegationAutomated, Ct);

        Assert.Equal(ArcaConnectionErrors.PlatformNotConfigured, unconfigured.Error);
        Assert.Equal(ArcaConnectionErrors.ModeUnavailable, automated.Error);
        Assert.Null(host.Store.Connection);
        Assert.Null(configured.Store.Connection);
    }
}
