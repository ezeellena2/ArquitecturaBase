using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>Registro de un intento de ingreso, exitoso o fallido. Nunca guarda el código.</summary>
public sealed class LoginAudit : Entity
{
    public const int MaxUserAgentLength = 512;

    // Para EF Core.
    private LoginAudit()
    {
    }

    private LoginAudit(
        string identifier,
        Guid? userId,
        LoginMethod method,
        bool succeeded,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        Identifier = identifier;
        UserId = userId;
        Method = method;
        Succeeded = succeeded;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > MaxUserAgentLength } ? userAgent[..MaxUserAgentLength] : userAgent;
        OccurredAtUtc = occurredAtUtc;
    }

    /// <summary>
    /// Con qué se presentó la persona: el correo o el número en formato internacional (sección 6.7 del spec del
    /// ingreso con WhatsApp). La retención lo olvida en un intento fallido (N16): queda null.
    /// </summary>
    public string? Identifier { get; private set; }

    public Guid? UserId { get; private set; }

    public LoginMethod Method { get; private set; }

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static LoginAudit Success(
        string identifier, Guid userId, LoginMethod method, string? ipAddress, string? userAgent, DateTime occurredAtUtc) =>
        new(identifier, userId, method, succeeded: true, failureReason: null, ipAddress, userAgent, occurredAtUtc);

    /// <summary>
    /// N16: la retención deja el ingreso sin la IP ni el navegador a los 12 meses, y un intento fallido también sin lo que
    /// la persona escribió, que puede ser el correo o el número de otro. Quedan la cuenta, cuándo, por dónde y cómo le fue,
    /// y el identificador de un ingreso exitoso: el último ingreso de cada cuenta no se pierde.
    /// </summary>
    public void ForgetRequestInfo()
    {
        (IpAddress, UserAgent) = (null, null);

        if (!Succeeded) Identifier = null;
    }

    public static LoginAudit Failure(
        string identifier,
        Guid? userId,
        LoginMethod method,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new(identifier, userId, method, succeeded: false, failureReason, ipAddress, userAgent, occurredAtUtc);
    }
}
