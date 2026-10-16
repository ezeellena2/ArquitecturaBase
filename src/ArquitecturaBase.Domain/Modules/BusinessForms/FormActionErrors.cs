using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormActionErrors
{
    public static readonly Error MessageTooLong = Error.Validation("Forms.Action.MessageTooLong","The expanded notification exceeds the channel message limit.");
    public static readonly Error StateConflict = Error.Conflict("Forms.Action.StateConflict","The action execution state changed.");
    public static readonly Error NotFound = Error.NotFound("Forms.Action.NotFound","The action execution is unavailable.");
    public static readonly Error ApprovalAlreadyPending = Error.Conflict("Forms.Action.ApprovalAlreadyPending","An approval task is already open for this response.");
    public static readonly Error ChannelUnavailable = Error.Failure("Forms.Action.ChannelUnavailable","The notification channel is unavailable.");
    public static readonly Error RecipientUnavailable = Error.Failure("Forms.Action.RecipientUnavailable","The notification recipient is unavailable.");
    public static readonly Error EffectUnconfirmed = Error.Failure("Forms.Action.EffectUnconfirmed","The external action could not be confirmed.");
}
