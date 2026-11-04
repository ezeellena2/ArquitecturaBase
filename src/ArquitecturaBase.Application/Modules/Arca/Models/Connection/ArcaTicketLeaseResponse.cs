using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El resultado de reservar el ticket y cómo quedó la fila en ese momento; las credenciales van cifradas.</summary>
public sealed record ArcaTicketLeaseResponse(ArcaTicketLease Lease, string? ProtectedCredentials, DateTime? ExpiresAtUtc, DateTime? BlockedUntilUtc)
{
    public override string ToString() => $"ArcaTicketLeaseResponse {{ lease: {Lease}, credentials: [REDACTED] }}";
}
