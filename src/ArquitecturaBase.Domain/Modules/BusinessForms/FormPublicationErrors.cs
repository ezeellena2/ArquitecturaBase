using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormPublicationErrors
{
    public static readonly Error CheckExpired = Error.Conflict("Forms.Publication.CheckExpired", "Check publication again before accepting it.");
    public static readonly Error PayloadMismatch = Error.Conflict("Forms.Publication.PayloadMismatch", "The publication attempt already belongs to different content.");
    public static readonly Error InProgress = Error.Conflict("Forms.Publication.InProgress", "A publication is already in progress for this definition.");
    public static readonly Error StateConflict = Error.Conflict("Forms.Publication.StateConflict", "The publication state or reservation has changed.");
    public static readonly Error AccountUnavailable = Error.Validation("Forms.Publication.AccountUnavailable", "Publication is unavailable for the configured channel.");
    public static readonly Error NotFound = Error.NotFound("Forms.Publication.NotFound", "The publication attempt was not found.");
    public static readonly Error DraftMustBeSaved = Error.Conflict("Forms.Publication.DraftMustBeSaved", "Save the draft before checking publication.");
    public static readonly Error MissingTranslation = Error.Validation("Forms.Publication.MissingTranslation", "Complete the form texts in both languages before publication.");
    public static readonly Error ChannelUnavailable = Error.Validation("Forms.Publication.ChannelUnavailable", "No structured form channel is installed.");
    public static readonly Error CatalogInvalid = Error.Validation("Forms.Publication.CatalogInvalid", "A question references a catalog that is unavailable for publication.");
}
