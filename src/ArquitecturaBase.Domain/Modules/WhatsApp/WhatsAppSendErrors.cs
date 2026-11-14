using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

/// <summary>
/// H9: why a send of the chat or of the assistant did not go out, one stable code per kind of refusal, so the operator knows which to
/// repeat. The notices of the rules keep their own (<see cref="WhatsAppFormActionErrors"/>).
/// </summary>
public static class WhatsAppSendErrors
{
    public static readonly Error NotAllowed = Error.Conflict("WhatsApp.Send.NotAllowed", "The number is not allowed to receive messages from this account.");
    public static readonly Error WindowClosed = Error.Conflict("WhatsApp.Send.WindowClosed", "The customer service window closed before the message went out.");
    public static readonly Error Undeliverable = Error.Conflict("WhatsApp.Send.Undeliverable", "Meta could not deliver the message to this number.");
    public static readonly Error RateLimited = Error.Conflict("WhatsApp.Send.RateLimited", "Meta held the message back by its message limit.");
    public static readonly Error TokenInvalid = Error.Conflict("WhatsApp.Send.TokenInvalid", "The WhatsApp access token is not valid.");
    public static readonly Error PermissionMissing = Error.Conflict("WhatsApp.Send.PermissionMissing", "The WhatsApp access token lacks a permission.");
    public static readonly Error Unavailable = Error.Conflict("WhatsApp.Send.Unavailable", "WhatsApp could not be reached, not even with its retries.");
    public static readonly Error Rejected = Error.Conflict("WhatsApp.Send.Rejected", "Meta refused the message.");

    /// <summary>
    /// The stable code of a refusal Meta reported with its own code, also later in a status of a message it had accepted (131026,
    /// 131047 or 131049 often come that way); none without a code.
    /// </summary>
    public static Error? FromMetaCode(int? metaCode) => metaCode switch
    {
        null => null,
        131030 => NotAllowed,
        131047 => WindowClosed,
        131026 => Undeliverable,
        130429 or 131056 => RateLimited,
        0 or 190 => TokenInvalid,
        3 or 10 or (>= 200 and <= 299) => PermissionMissing,
        131000 or 131016 or 2 or 133004 => Unavailable,
        _ => Rejected,
    };
}
