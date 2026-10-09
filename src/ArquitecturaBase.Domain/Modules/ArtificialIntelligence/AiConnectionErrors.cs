using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public static class AiConnectionErrors
{
    public static readonly Error Invalid = Error.Validation("Ai.Connection.Invalid", "The connection is invalid.");
    public static readonly Error CredentialRequired = Error.Validation("Ai.Connection.CredentialRequired", "A credential is required.");
    public static readonly Error ProviderChanged = Error.Validation("Ai.Connection.ProviderChanged", "Changing provider requires replacing or removing the credential.");
    public static readonly Error RevisionConflict = Error.Conflict("Ai.Connection.RevisionConflict", "The connection changed. Review its current revision.");
    public static readonly Error RequestConflict = Error.Conflict("Ai.Connection.RequestConflict", "The request identifier was used with a different payload.");
    public static readonly Error ChangeNotFound = Error.NotFound("Ai.Connection.ChangeNotFound", "The change receipt was not found.");
    public static readonly Error SessionRequired = Error.Unauthorized("Ai.Connection.SessionRequired", "An authenticated operator is required.");
    public static readonly Error CredentialUnavailable = Error.Failure("Ai.Connection.CredentialUnavailable", "The stored credential cannot be decrypted.");
}
