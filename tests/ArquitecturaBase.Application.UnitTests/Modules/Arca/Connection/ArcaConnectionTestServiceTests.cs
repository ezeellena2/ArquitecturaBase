using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

public sealed class ArcaConnectionTestServiceTests
{
    private const string Invoicing = "wsfe";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_chosen_services_are_checked_with_the_active_certificate()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync();

        var tested = (await host.Test.TestAsync(Ct)).Value;

        Assert.Equal(["ws_sr_constancia_inscripcion"], tested.Checks.Select(check => check.Service));
        Assert.Equal([host.Store.Certificate(ArcaCertificateRole.Active)!.Id], host.RegistryFamily.CheckedCertificates);
        Assert.Empty(host.InvoicingFamily.CheckedCertificates);
    }

    [Fact]
    public async Task All_green_with_complete_families_activates_the_connection()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync(Invoicing);

        var connection = (await host.Test.TestAsync(Ct)).Value.Connection;

        Assert.Equal((ArcaConnectionStatus.Active, true), (connection.Status, connection.LastTestPassed));
        Assert.Equal(ArcaApplicationHost.NowUtc, connection.LastTestedAtUtc);
    }

    [Fact]
    public async Task An_incomplete_family_leaves_it_unverified_and_an_active_one_needing_attention()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync(Invoicing);
        host.InvoicingFamily.Complete = () => false;

        Assert.Equal(ArcaConnectionStatus.Unverified, (await host.Test.TestAsync(Ct)).Value.Connection.Status);

        host.InvoicingFamily.Complete = () => true;
        Assert.Equal(ArcaConnectionStatus.Active, (await host.Test.TestAsync(Ct)).Value.Connection.Status);
        host.InvoicingFamily.Complete = () => false;
        Assert.Equal(ArcaConnectionStatus.NeedsAttention, (await host.Test.TestAsync(Ct)).Value.Connection.Status);
    }

    [Fact]
    public async Task A_red_service_on_an_active_connection_needs_attention()
    {
        using var host = new ArcaApplicationHost();
        await host.ActivateAsync(Invoicing);
        host.InvoicingFamily.Status = ArcaServiceCheckStatus.NotAuthorized;

        var tested = (await host.Test.TestAsync(Ct)).Value;

        Assert.Equal(ArcaServiceCheckStatus.NotAuthorized, tested.Checks.Single(check => check.Service == Invoicing).Status);
        Assert.Equal((ArcaConnectionStatus.NeedsAttention, false), (tested.Connection.Status, tested.Connection.LastTestPassed));
    }

    [Fact]
    public async Task A_green_renewal_promotes_the_new_certificate_and_removes_the_old_one()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);
        var old = host.Store.Certificate(ArcaCertificateRole.Active)!.Id;
        await host.LoadRenewalAsync(active);
        var renewal = host.Store.Certificate(ArcaCertificateRole.Pending)!.Id;

        var connection = (await host.Test.TestAsync(Ct)).Value.Connection;

        Assert.Equal(renewal, host.RegistryFamily.CheckedCertificates[^1]);
        var certificate = Assert.Single(connection.Certificates);
        Assert.Equal((ArcaCertificateRole.Active, "renovacion"), (certificate.Role, certificate.Alias));
        Assert.DoesNotContain(host.Store.Certificates, stored => stored.Id == old);
        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public async Task A_red_renewal_keeps_the_active_certificate_and_the_status()
    {
        using var host = new ArcaApplicationHost();
        var active = await host.ActivateAsync(Invoicing);
        var loaded = await host.LoadRenewalAsync(active);
        host.RegistryFamily.Status = ArcaServiceCheckStatus.NotAuthorized;

        var tested = (await host.Test.TestAsync(Ct)).Value;

        Assert.Equal(ArcaServiceCheckStatus.NotAuthorized, tested.Checks[0].Status);
        Assert.Equal((ArcaConnectionStatus.Active, loaded.Revision), (tested.Connection.Status, tested.Connection.Revision));
        Assert.Equal("plataforma", host.Store.Certificate(ArcaCertificateRole.Active)!.Alias);
        Assert.Equal("renovacion", host.Store.Certificate(ArcaCertificateRole.Pending)!.Alias);
    }

    [Fact]
    public async Task A_result_for_a_certificate_that_stopped_being_active_while_testing_is_not_saved()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync();
        host.RegistryFamily.OnCheck = () =>
        {
            host.Store.InTransaction = () => true;
            var replaced = host.Store.Certificate(ArcaCertificateRole.Active)!;
            host.Store.Remove(replaced);
            var other = ArcaCertificate.Request(Guid.CreateVersion7(), replaced.ConnectionId, "otro-alias", "CSR", "protected").Value;
            other.Load("PEM", ArcaApplicationHost.NowUtc, ArcaApplicationHost.NowUtc.AddYears(2), "THUMB");
            other.Promote();
            host.Store.Add(other);
            host.Store.InTransaction = () => host.Work.InTransaction;
        };

        var result = await host.Test.TestAsync(Ct);

        Assert.Equal(ArcaConnectionErrors.RevisionConflict, result.Error);
        Assert.Equal(ArcaConnectionStatus.Unverified, host.Store.Connection!.Status);
        Assert.Null(host.Store.Connection.LastTestPassed);
    }

    [Fact]
    public async Task Testing_needs_a_connection_with_a_loaded_certificate()
    {
        using var host = new ArcaApplicationHost();
        Assert.Equal(ArcaConnectionErrors.NotConnected, (await host.Test.TestAsync(Ct)).Error);

        await host.Connection.StartCertificateRequestAsync("20-00000006-0", ArcaEnvironment.Testing, "plataforma", [], 0, Ct);

        Assert.Equal(ArcaConnectionErrors.CertificateNotLoaded, (await host.Test.TestAsync(Ct)).Error);
        Assert.Empty(host.RegistryFamily.CheckedCertificates);
    }

    [Fact]
    public async Task Reevaluating_uses_the_configuration_of_the_chosen_families_without_testing_again()
    {
        using var host = new ArcaApplicationHost();
        await host.StartAndUploadAsync(Invoicing);
        host.InvoicingFamily.Complete = () => false;
        await host.Test.TestAsync(Ct);
        host.InvoicingFamily.Complete = () => true;

        var connection = (await host.Test.ReevaluateAsync(Ct)).Value;

        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);
        Assert.Single(host.InvoicingFamily.CheckedCertificates);
    }
}
