using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppAssistantErrors
{
    public static readonly Error SessionRequired = Error.Unauthorized("WhatsApp.Assistant.SessionRequired", "Sign in to use a manual suggestion.");
    public static readonly Error Forbidden = Error.Forbidden("WhatsApp.Assistant.Forbidden", "The current account cannot use this suggestion.");
    public static readonly Error StateConflict = Error.Conflict("WhatsApp.Assistant.StateConflict", "The suggestion or conversation changed.");
    public static readonly Error InvalidRequest = Error.Validation("WhatsApp.Assistant.InvalidRequest", "The assistant request is invalid.");
    public static readonly Error InvalidProposal = Error.Conflict("WhatsApp.Assistant.InvalidProposal", "The proposal is no longer valid for this recipient.");
    public static readonly Error Unavailable = Error.Conflict("WhatsApp.Assistant.Unavailable", "The assistant cannot prepare this suggestion.");

    /// <summary>O36: the conversation already got today's form offers (the agent's limit, TO1-A): the form does not go out and a person answers.</summary>
    public static readonly Error OfferLimitReached = Error.Conflict("WhatsApp.Assistant.OfferLimitReached", "The conversation reached today's limit of form offers.");
}
