using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaConnectionErrors
{
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
}
