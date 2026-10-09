using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
public static class AiBusinessInformationErrors
{
    public static readonly Error Invalid = Error.Validation("Ai.BusinessInformation.Invalid", "The business information is invalid.");
    public static readonly Error InvalidLocation = Error.Validation("Ai.BusinessInformation.InvalidLocation", "The business location is invalid.");
    public static readonly Error InvalidHours = Error.Validation("Ai.BusinessInformation.InvalidHours", "The business opening hours are invalid.");
    public static readonly Error InvalidFaq = Error.Validation("Ai.BusinessInformation.InvalidFaq", "The business questions and answers are invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Ai.BusinessInformation.RevisionConflict", "The business information changed. Review its current revision.");
    public static readonly Error RequestConflict = Error.Conflict("Ai.BusinessInformation.RequestConflict", "The request identifier was used with a different payload.");
    public static readonly Error ChangeNotFound = Error.NotFound("Ai.BusinessInformation.ChangeNotFound", "The business information change receipt was not found.");
    public static readonly Error SessionRequired = Error.Unauthorized("Ai.BusinessInformation.SessionRequired", "An authenticated operator is required.");
}
