using ArquitecturaBase.Application.Modules.Arca.Models.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Connection;

// B4: the expiry of the certificate is worked out when reading: an active connection whose certificate expired reads as needing
// attention, without waiting for a test; one that was never active keeps its status.
public sealed class ArcaConnectionResponseTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_active_connection_whose_certificate_expired_reads_as_needing_attention()
    {
        Assert.Equal(ArcaConnectionStatus.NeedsAttention, ArcaConnectionResponse.From(Row(ArcaConnectionStatus.Active, Now.AddMinutes(-1)), [], null, Now).Status);
        Assert.Equal(ArcaConnectionStatus.Active, ArcaConnectionResponse.From(Row(ArcaConnectionStatus.Active, Now.AddDays(1)), [], null, Now).Status);
        Assert.Equal(ArcaConnectionStatus.Unverified, ArcaConnectionResponse.From(Row(ArcaConnectionStatus.Unverified, Now.AddMinutes(-1)), [], null, Now).Status);
    }

    // RC7-B: the screen warns as many days before as the connection says; without a connection, the default.
    [Fact]
    public void The_notice_days_come_from_the_connection_or_the_default_without_one()
    {
        Assert.Equal(45, ArcaConnectionResponse.From(Row(ArcaConnectionStatus.Active, Now.AddDays(1)), [], null, Now).CertificateNoticeDays);
        Assert.Equal(30, ArcaConnectionResponse.From(null, [], null, Now).CertificateNoticeDays);
    }

    private static ArcaConnectionRow Row(ArcaConnectionStatus status, DateTime notAfterUtc) => new(Guid.NewGuid(), ArcaAccessMode.CompanyCertificate,
        ArcaEnvironment.Testing, "30710001231", null, status, ["wsfe"], status == ArcaConnectionStatus.Active, Now.AddDays(-1), 3, 45,
        [new ArcaCertificateRow(Guid.NewGuid(), ArcaCertificateRole.Active, "facturacion", true, notAfterUtc.AddYears(-1), notAfterUtc, "ab12")]);
}
