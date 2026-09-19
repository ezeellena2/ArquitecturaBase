using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Authentication;

/// <summary>Registro de un intento de ingreso, exitoso o fallido. Nunca guarda el código.</summary>
public sealed class LoginAudit : Entity
{
    public const int MaxUserAgentLength = 512;

    // Para EF Core.
    private LoginAudit()
    {
        Email = string.Empty;
    }

    private LoginAudit(
        string email,
        Guid? userId,
        LoginMethod method,
        bool succeeded,
        string? failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        Email = email;
        UserId = userId;
        Method = method;
        Succeeded = succeeded;
        FailureReason = failureReason;
        IpAddress = ipAddress;
        UserAgent = userAgent is { Length: > MaxUserAgentLength } ? userAgent[..MaxUserAgentLength] : userAgent;
        OccurredAtUtc = occurredAtUtc;
    }

    public string Email { get; private set; }

    public Guid? UserId { get; private set; }

    public LoginMethod Method { get; private set; }

    public bool Succeeded { get; private set; }

    public string? FailureReason { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static LoginAudit Success(
        string email, Guid userId, LoginMethod method, string? ipAddress, string? userAgent, DateTime occurredAtUtc) =>
        new(email, userId, method, succeeded: true, failureReason: null, ipAddress, userAgent, occurredAtUtc);

    public static LoginAudit Failure(
        string email,
        Guid? userId,
        LoginMethod method,
        string failureReason,
        string? ipAddress,
        string? userAgent,
        DateTime occurredAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);

        return new(email, userId, method, succeeded: false, failureReason, ipAddress, userAgent, occurredAtUtc);
    }
}
