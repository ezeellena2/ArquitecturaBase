namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

public enum ArcaServiceCheckStatus
{
    Ready,
    NotAuthorized,
    Blocked,
    ClockSkew,
    Unavailable,
    Failed,
}

/// <summary>
/// El resultado de probar un servicio. <paramref name="FailureCode"/> es un motivo estable de la familia cuando el estado
/// es <see cref="ArcaServiceCheckStatus.Failed"/>; <paramref name="RetryAfterUtc"/>, hasta cuándo ARCA no acepta otro
/// pedido de ticket.
/// </summary>
public sealed record ArcaServiceCheckResponse(string Service, ArcaServiceCheckStatus Status, string? FailureCode,
    DateTime? TicketExpiresAtUtc, DateTime? RetryAfterUtc);
