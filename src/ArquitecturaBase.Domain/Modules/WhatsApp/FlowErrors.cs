using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class FlowErrors
{
    public static readonly Error CatalogPreparationBlocked = Error.Conflict("WhatsApp.Flow.CatalogPreparationBlocked", "The recipient catalog options cannot be prepared.");
    public static readonly Error RecipientContextChanged = Error.Conflict("WhatsApp.Flow.RecipientContextChanged", "The linked recipient or their eligibility changed.");
    public static readonly Error FormUnavailable = Error.Conflict("WhatsApp.Flow.FormUnavailable", "The selected published form is unavailable.");
    public static readonly Error StateConflict = Error.Conflict("WhatsApp.Flow.StateConflict", "The Flow reservation or state changed.");
    public static readonly Error PayloadMismatch = Error.Conflict("WhatsApp.Flow.PayloadMismatch", "The Flow operation already belongs to another snapshot.");
    public static readonly Error ProviderRejected = Error.Validation("WhatsApp.Flow.ProviderRejected", "The provider rejected the Flow operation.");
    public static readonly Error ValidationFailed = Error.Validation("WhatsApp.Flow.ValidationFailed", "The provider reported Flow validation errors.");
    public static readonly Error AuthorizationChanged = Error.Validation("WhatsApp.Flow.AuthorizationChanged", "Publication authorization changed.");
    public static readonly Error NotFound = Error.NotFound("WhatsApp.Flow.NotFound", "The Flow publication was not found.");
    public static readonly Error PreviewUnavailable = Error.Validation("WhatsApp.Flow.PreviewUnavailable", "The official preview is unavailable.");
    public static readonly Error AccountUnavailable = Error.Validation("WhatsApp.Flow.AccountUnavailable", "The account is unavailable for Flow publication.");
    public static readonly Error AccountMismatch = Error.Validation("WhatsApp.Flow.AccountMismatch", "The configured phone does not belong to the Flow account.");
    public static readonly Error UnsupportedConfiguration = Error.Validation("WhatsApp.Flow.UnsupportedConfiguration", "The configuration cannot be represented by the channel profile.");
    public static readonly Error TextTooLong = Error.Validation("WhatsApp.Flow.TextTooLong", "The text exceeds the channel profile budget.");
    public static readonly Error MissingTranslation = Error.Validation("WhatsApp.Flow.MissingTranslation", "The required text is missing in the exact target culture.");
    public static readonly Error ComponentBudgetExceeded = Error.Validation("WhatsApp.Flow.ComponentBudgetExceeded", "The screen component budget is exceeded.");
    public static readonly Error ResponseTooLarge = Error.Validation("WhatsApp.Flow.ResponseTooLarge", "The worst-case form response exceeds the WhatsApp response budget. Reduce the maximum length of one or more text fields.");
    public static readonly Error CaptureFailed = Error.Validation("WhatsApp.Flow.CaptureFailed", "The Flow reply could not be saved after bounded retries.");
    public static readonly Error InvalidValue = Error.Validation("WhatsApp.Flow.InvalidValue", "The value does not match the fixed field codec.");
}
