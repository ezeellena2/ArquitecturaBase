using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.Arca.Connection;

/// <summary>
/// El ticket de acceso del WSAA para un certificado y un servicio, compartido por todos los hosts. Lo renueva uno solo
/// por vez, con una reserva (dueño y vencimiento) que se confirma con el mismo dueño: el pedido a ARCA va fuera de la
/// transacción (ADR 0023). Si ARCA dice que ya dio un ticket, queda bloqueado hasta que pase su ventana.
/// </summary>
public sealed class ArcaAccessTicket : Entity
{
    private ArcaAccessTicket() { }

    public Guid CertificateId { get; private set; }
    public string ServiceCode { get; private set; } = string.Empty;
    public string? ProtectedCredentials { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public Guid? LeaseOwner { get; private set; }
    public DateTime? LeaseUntilUtc { get; private set; }
    public DateTime? BlockedUntilUtc { get; private set; }

    public static ArcaAccessTicket Create(Guid certificateId, ArcaServiceCode service) =>
        new() { CertificateId = certificateId, ServiceCode = service.Value };

    public ArcaTicketLease TryLease(Guid owner, DateTime nowUtc, TimeSpan margin, TimeSpan leaseDuration)
    {
        if (ExpiresAtUtc is { } expires && expires - nowUtc > margin) return ArcaTicketLease.AlreadyValid;
        if (BlockedUntilUtc is { } blocked && blocked > nowUtc) return ArcaTicketLease.Blocked;
        if (LeaseOwner is { } current && current != owner && LeaseUntilUtc > nowUtc) return ArcaTicketLease.Busy;
        LeaseOwner = owner;
        LeaseUntilUtc = nowUtc + leaseDuration;
        return ArcaTicketLease.Acquired;
    }

    public bool Store(Guid owner, string protectedCredentials, DateTime expiresAtUtc)
    {
        if (LeaseOwner != owner) return false;
        (ProtectedCredentials, ExpiresAtUtc, BlockedUntilUtc) = (protectedCredentials, expiresAtUtc, null);
        EndLease();
        return true;
    }

    public bool Block(Guid owner, DateTime untilUtc)
    {
        if (LeaseOwner != owner) return false;
        (ProtectedCredentials, ExpiresAtUtc, BlockedUntilUtc) = (null, null, untilUtc);
        EndLease();
        return true;
    }

    /// <summary>Suelta la reserva sin guardar nada, por ejemplo si ARCA no respondió.</summary>
    public bool Release(Guid owner)
    {
        if (LeaseOwner != owner) return false;
        EndLease();
        return true;
    }

    public override string ToString() => $"ArcaAccessTicket {{ certificate: {CertificateId}, service: {ServiceCode}, credentials: [REDACTED] }}";

    private void EndLease() => (LeaseOwner, LeaseUntilUtc) = (null, null);
}
