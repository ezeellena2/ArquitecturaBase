namespace ArquitecturaBase.Application.Modules.Arca.Models.Connection;

/// <summary>
/// Lo que pasó al pedir el ticket de un servicio. No es un <c>Error</c>: es el resultado que la prueba muestra por
/// servicio, y una familia sigue sólo con <see cref="Ready"/>.
/// </summary>
public abstract record ArcaTicketOutcome
{
    private ArcaTicketOutcome() { }

    public sealed record Ready(string Token, string Sign, DateTime ExpiresAtUtc) : ArcaTicketOutcome
    {
        public override string ToString() => $"Ready {{ credentials: [REDACTED], expiresAtUtc: {ExpiresAtUtc:O} }}";
    }

    /// <summary>ARCA ya dio un ticket que no tenemos; no acepta otro pedido hasta <paramref name="UntilUtc"/>.</summary>
    public sealed record Blocked(DateTime UntilUtc) : ArcaTicketOutcome;

    /// <summary>El certificado no tiene autorizado el servicio, o ARCA no confía en él.</summary>
    public sealed record NotAuthorized : ArcaTicketOutcome;

    /// <summary>
    /// En modo delegación, el ticket de la plataforma no incluye a la empresa en sus relaciones: no delegó el servicio, la
    /// plataforma no lo aceptó o se revocó.
    /// </summary>
    public sealed record NotDelegated : ArcaTicketOutcome;

    /// <summary>ARCA rechazó la ventana del pedido: el reloj del servidor está corrido.</summary>
    public sealed record ClockSkew : ArcaTicketOutcome;

    /// <summary>ARCA no respondió, respondió algo que no se entiende, o el ticket guardado no se pudo descifrar.</summary>
    public sealed record Unavailable : ArcaTicketOutcome;
}
