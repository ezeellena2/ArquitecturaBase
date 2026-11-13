using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Connection;

public sealed class ArcaConnectionTests
{
    private static readonly ArcaServiceCode Registry = ArcaServiceCode.Create("ws_sr_constancia_inscripcion")!.Value;
    private static readonly ArcaServiceCode Invoicing = ArcaServiceCode.Create("wsfe")!.Value;
    private static readonly ArcaServiceCode[] Installed = [Registry, Invoicing];
    private static readonly ArcaServiceCode[] Required = [Registry];
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Starting_with_a_valid_cuit_awaits_the_certificate_of_the_company()
    {
        var connection = Start([Invoicing]);

        Assert.Equal("30710001231", connection.Cuit);
        Assert.Equal(ArcaEnvironment.Production, connection.Environment);
        Assert.Equal(ArcaAccessMode.CompanyCertificate, connection.Mode);
        Assert.Equal(ArcaConnectionStatus.AwaitingCertificate, connection.Status);
        Assert.Equal(1, connection.Revision);
        Assert.Null(connection.LastTestPassed);
    }

    [Fact]
    public void An_invalid_cuit_is_rejected()
    {
        var result = ArcaConnection.Start("30-71000123-2", ArcaEnvironment.Testing, [Invoicing], Installed, Required);
        Assert.Equal(ArcaConnectionErrors.CuitInvalid, result.Error);
    }

    // Task 17 of the documents: ARCA validates with the core's CUIT, so eleven zeros are no CUIT and any space or dash separates.
    [Fact]
    public void The_cuit_follows_the_rule_of_the_core()
    {
        Assert.Equal(ArcaConnectionErrors.CuitInvalid, ArcaConnection.Start("00000000000", ArcaEnvironment.Testing, [Invoicing], Installed, Required).Error);
        Assert.Equal("30710001231", ArcaConnection.Start("30–71000123–1", ArcaEnvironment.Testing, [Invoicing], Installed, Required).Value.Cuit);
    }

    [Fact]
    public void A_required_service_is_always_chosen_and_the_codes_are_kept_once_in_order()
    {
        var connection = Start([Invoicing, Invoicing]);
        Assert.Equal(["ws_sr_constancia_inscripcion", "wsfe"], connection.EnabledServices);
    }

    [Fact]
    public void A_service_that_is_not_installed_cannot_be_chosen()
    {
        var unknown = ArcaServiceCode.Create("wscpe")!.Value;

        var start = ArcaConnection.Start("30710001231", ArcaEnvironment.Testing, [unknown], Installed, Required);
        Assert.Equal(ArcaConnectionErrors.ServiceNotInstalled, start.Error);

        var connection = Start([Invoicing]);
        Assert.Equal(ArcaConnectionErrors.ServiceNotInstalled, connection.ChooseServices([unknown], Installed, Required).Error);
        Assert.Equal(1, connection.Revision);
    }

    [Fact]
    public void Choosing_services_replaces_them_but_keeps_the_required_ones()
    {
        var connection = Start([Invoicing]);

        Assert.True(connection.ChooseServices([], Installed, Required).IsSuccess);

        Assert.Equal(["ws_sr_constancia_inscripcion"], connection.EnabledServices);
        Assert.Equal(2, connection.Revision);
    }

    [Fact]
    public void Loading_the_first_certificate_leaves_the_connection_unverified()
    {
        var connection = Start([Invoicing]);

        connection.CertificateLoaded();

        Assert.Equal(ArcaConnectionStatus.Unverified, connection.Status);
        Assert.Equal(2, connection.Revision);
    }

    [Fact]
    public void A_passing_test_activates_the_connection_only_when_every_chosen_family_is_complete()
    {
        var connection = Loaded();

        connection.RecordTest(passed: true, familiesComplete: false, Now);
        Assert.Equal(ArcaConnectionStatus.Unverified, connection.Status);
        Assert.True(connection.LastTestPassed);
        Assert.Equal(Now, connection.LastTestedAtUtc);

        connection.RecordTest(passed: true, familiesComplete: true, Now.AddMinutes(1));
        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public void An_active_connection_needs_attention_after_a_failing_test_and_recovers_after_a_passing_one()
    {
        var connection = Active();

        connection.RecordTest(passed: false, familiesComplete: true, Now.AddMinutes(5));
        Assert.Equal(ArcaConnectionStatus.NeedsAttention, connection.Status);
        Assert.False(connection.LastTestPassed);

        connection.RecordTest(passed: true, familiesComplete: true, Now.AddMinutes(6));
        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public void A_failing_test_of_an_unverified_connection_keeps_it_unverified()
    {
        var connection = Loaded();
        connection.RecordTest(passed: false, familiesComplete: true, Now);
        Assert.Equal(ArcaConnectionStatus.Unverified, connection.Status);
    }

    [Fact]
    public void Testing_before_any_certificate_is_a_bug()
    {
        var connection = Start([Invoicing]);
        Assert.Throws<InvalidOperationException>(() => connection.RecordTest(passed: true, familiesComplete: true, Now));
    }

    [Fact]
    public void The_families_are_reevaluated_without_a_new_test()
    {
        var connection = Loaded();
        connection.RecordTest(passed: true, familiesComplete: false, Now);

        connection.ReevaluateFamilies(familiesComplete: true);
        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);

        connection.ReevaluateFamilies(familiesComplete: false);
        Assert.Equal(ArcaConnectionStatus.NeedsAttention, connection.Status);
    }

    [Fact]
    public void Families_never_activate_a_connection_whose_last_test_failed()
    {
        var connection = Loaded();
        connection.RecordTest(passed: false, familiesComplete: true, Now);

        connection.ReevaluateFamilies(familiesComplete: true);

        Assert.Equal(ArcaConnectionStatus.Unverified, connection.Status);
    }

    [Fact]
    public void An_expired_certificate_makes_an_active_connection_need_attention()
    {
        var connection = Active();
        var revision = connection.Revision;

        connection.CertificateExpired();

        Assert.Equal(ArcaConnectionStatus.NeedsAttention, connection.Status);
        Assert.Equal(revision + 1, connection.Revision);
    }

    [Fact]
    public void The_text_representation_masks_the_cuit()
    {
        var text = Start([Invoicing]).ToString();
        Assert.DoesNotContain("30710001231", text, StringComparison.Ordinal);
        Assert.Contains("30-*****123-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Requesting_or_cancelling_a_certificate_advances_the_revision_without_changing_the_status()
    {
        var connection = Active();
        var revision = connection.Revision;

        connection.CertificateRequested();
        connection.RequestCancelled();

        Assert.Equal(revision + 2, connection.Revision);
        Assert.Equal(ArcaConnectionStatus.Active, connection.Status);
    }

    [Fact]
    public void Starting_by_delegation_keeps_the_company_as_the_cuit_and_signs_with_the_platform()
    {
        var connection = ArcaConnection.StartDelegated("30-71000123-1", "30-71999888-3", ArcaEnvironment.Production, [Invoicing],
            Installed, Required).Value;

        Assert.Equal(ArcaAccessMode.PlatformDelegation, connection.Mode);
        Assert.Equal("30710001231", connection.Cuit);
        Assert.Equal("30719998883", connection.PlatformCuit);
        Assert.Equal("30719998883", connection.SignerCuit);
        Assert.Equal(ArcaConnectionStatus.AwaitingCertificate, connection.Status);
        Assert.Equal(["ws_sr_constancia_inscripcion", "wsfe"], connection.EnabledServices);
        Assert.Equal(1, connection.Revision);
    }

    [Fact]
    public void A_connection_with_the_company_certificate_signs_with_the_company()
    {
        var connection = Start([Invoicing]);

        Assert.Null(connection.PlatformCuit);
        Assert.Equal("30710001231", connection.SignerCuit);
    }

    [Fact]
    public void Starting_by_delegation_checks_the_company_cuit_and_the_services()
    {
        var unknown = ArcaServiceCode.Create("wscpe")!.Value;

        Assert.Equal(ArcaConnectionErrors.CuitInvalid,
            ArcaConnection.StartDelegated("30-71000123-2", "30719998883", ArcaEnvironment.Production, [Invoicing], Installed, Required).Error);
        Assert.Equal(ArcaConnectionErrors.ServiceNotInstalled,
            ArcaConnection.StartDelegated("30710001231", "30719998883", ArcaEnvironment.Production, [unknown], Installed, Required).Error);
    }

    [Fact]
    public void The_automated_delegation_keeps_the_same_model_with_its_own_mode()
    {
        var connection = ArcaConnection.StartDelegated("30-71000123-1", "30-71999888-3", ArcaEnvironment.Production, [Invoicing],
            Installed, Required, ArcaAccessMode.PlatformDelegationAutomated).Value;

        Assert.Equal(ArcaAccessMode.PlatformDelegationAutomated, connection.Mode);
        Assert.Equal("30719998883", connection.SignerCuit);
        Assert.Throws<ArgumentOutOfRangeException>(() => ArcaConnection.StartDelegated("30710001231", "30719998883",
            ArcaEnvironment.Production, [Invoicing], Installed, Required, ArcaAccessMode.CompanyCertificate));
    }

    [Fact]
    public void The_platform_cuit_arrives_already_checked_by_the_configuration()
    {
        Assert.Throws<ArgumentException>(() =>
            ArcaConnection.StartDelegated("30710001231", "30-71999888-4", ArcaEnvironment.Production, [Invoicing], Installed, Required));
    }

    private static ArcaConnection Start(ArcaServiceCode[] chosen) =>
        ArcaConnection.Start("30-71000123-1", ArcaEnvironment.Production, chosen, Installed, Required).Value;

    private static ArcaConnection Loaded()
    {
        var connection = Start([Invoicing]);
        connection.CertificateLoaded();
        return connection;
    }

    private static ArcaConnection Active()
    {
        var connection = Loaded();
        connection.RecordTest(passed: true, familiesComplete: true, Now);
        return connection;
    }
}
