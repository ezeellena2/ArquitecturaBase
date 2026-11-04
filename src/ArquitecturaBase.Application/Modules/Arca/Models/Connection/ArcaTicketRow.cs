namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>El ticket guardado: las credenciales van cifradas, como la clave de IA en su fila.</summary>
public sealed record ArcaTicketRow(string? ProtectedCredentials, DateTime? ExpiresAtUtc, DateTime? BlockedUntilUtc)
{
    public override string ToString() => $"ArcaTicketRow {{ credentials: [REDACTED], expiresAtUtc: {ExpiresAtUtc:O}, blockedUntilUtc: {BlockedUntilUtc:O} }}";
}
