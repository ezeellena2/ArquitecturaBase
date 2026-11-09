using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaConnectionErrors
{
    private const string RetryAfterKey = "retryAfter";

    public static readonly Error CuitInvalid = Error.Validation("Arca.Connection.CuitInvalid", "The CUIT is not valid.");
    public static readonly Error AliasInvalid = Error.Validation("Arca.Connection.AliasInvalid", "The certificate alias is not valid.");
    public static readonly Error ServiceNotInstalled = Error.Validation("Arca.Connection.ServiceNotInstalled", "The ARCA service is not installed.");
    public static readonly Error CertificateUnreadable = Error.Validation("Arca.Connection.CertificateUnreadable", "The certificate cannot be read.");
    public static readonly Error CertificateKeyMismatch = Error.Validation("Arca.Connection.CertificateKeyMismatch", "The certificate does not match the generated key.");
    public static readonly Error CertificateCuitMismatch = Error.Validation("Arca.Connection.CertificateCuitMismatch", "The certificate belongs to another CUIT.");
    public static readonly Error CertificateEnvironmentMismatch = Error.Validation("Arca.Connection.CertificateEnvironmentMismatch", "The certificate belongs to the other environment.");
    public static readonly Error CertificateExpired = Error.Validation("Arca.Connection.CertificateExpired", "The certificate has expired.");
    public static readonly Error NoPendingRequest = Error.Validation("Arca.Connection.NoPendingRequest", "There is no pending certificate request.");
    public static readonly Error RevisionConflict = Error.Conflict("Arca.Connection.RevisionConflict", "The connection changed. Review its current revision.");
    public static readonly Error NotConnected = Error.NotFound("Arca.Connection.NotConnected", "There is no ARCA connection.");
    public static readonly Error CertificateNotLoaded = Error.Validation("Arca.Connection.CertificateNotLoaded", "Upload the certificate first.");
    public static readonly Error ServiceNotAuthorized = Error.Validation("Arca.Connection.ServiceNotAuthorized", "ARCA did not authorize the service for the certificate.");
    public static readonly Error ClockSkew = Error.Failure("Arca.Connection.ClockSkew", "ARCA rejected the time window of the request.");
    public static readonly Error Unavailable = Error.Failure("Arca.Connection.Unavailable", "ARCA is unavailable.");
    public static readonly Error ServiceRejected = Error.Failure("Arca.Connection.ServiceRejected", "ARCA rejected the request.");
    public static readonly Error PlatformNotConfigured = Error.Validation("Arca.Connection.PlatformNotConfigured", "The installation has no platform CUIT to delegate to.");
    public static readonly Error ModeUnavailable = Error.Validation("Arca.Connection.ModeUnavailable", "That access mode is not available yet.");
    public static readonly Error ServiceNotDelegated = Error.Validation("Arca.Connection.ServiceNotDelegated", "The company has not delegated the service to the platform.");

    /// <summary>ARCA ya dio un ticket que no tenemos y no acepta otro pedido hasta dentro de <paramref name="retryAfterSeconds"/>.</summary>
    public static Error ServiceBlocked(int retryAfterSeconds) =>
        Error.TooManyRequests(
            "Arca.Connection.ServiceBlocked",
            "ARCA does not accept another ticket request yet.",
            new Dictionary<string, object?> { [RetryAfterKey] = retryAfterSeconds });
}
