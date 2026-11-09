using ArquitecturaBase.Domain.Modules.Arca.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

public enum ArcaServiceCheckStatus
{
    Ready,
    NotAuthorized,
    NotDelegated,
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
    DateTime? TicketExpiresAtUtc, DateTime? RetryAfterUtc)
{
    public static ArcaServiceCheckResponse Passed(ArcaServiceCode service, DateTime ticketExpiresAtUtc) =>
        new(service.Value, ArcaServiceCheckStatus.Ready, null, ticketExpiresAtUtc, null);

    public static ArcaServiceCheckResponse Failed(ArcaServiceCode service, ArcaServiceCheckStatus status, string? failureCode,
        DateTime? ticketExpiresAtUtc = null) => new(service.Value, status, failureCode, ticketExpiresAtUtc, null);

    /// <summary>El resultado de un servicio cuyo ticket no llegó; con el ticket listo, el servicio queda en verde.</summary>
    public static ArcaServiceCheckResponse FromTicket(ArcaServiceCode service, ArcaTicketOutcome outcome) => outcome switch
    {
        ArcaTicketOutcome.Ready ready => Passed(service, ready.ExpiresAtUtc),
        ArcaTicketOutcome.Blocked blocked => new(service.Value, ArcaServiceCheckStatus.Blocked, null, null, blocked.UntilUtc),
        ArcaTicketOutcome.NotAuthorized => Failed(service, ArcaServiceCheckStatus.NotAuthorized, null),
        ArcaTicketOutcome.NotDelegated => Failed(service, ArcaServiceCheckStatus.NotDelegated, null),
        ArcaTicketOutcome.ClockSkew => Failed(service, ArcaServiceCheckStatus.ClockSkew, null),
        _ => Failed(service, ArcaServiceCheckStatus.Unavailable, null),
    };
}
