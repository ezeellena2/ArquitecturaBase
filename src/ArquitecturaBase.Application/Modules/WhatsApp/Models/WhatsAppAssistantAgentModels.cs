using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// One save of the four tabs (AS1-A). <see cref="OriginalCulture"/> is the one the agent already has, or the language of the menu
/// for its first save; <see cref="ExpectedRevision"/> is 0 before that first save.
/// </summary>
public sealed record UpdateWhatsAppAssistantAgentRequest(long ExpectedRevision, string OriginalCulture, WhatsAppAssistantProfile Profile,
    IReadOnlyList<WhatsAppAssistantCase> Cases, WhatsAppAssistantAction Fallback, WhatsAppAssistantLimits Limits)
{
    public override string ToString() => "UpdateWhatsAppAssistantAgentRequest { content: [REDACTED] }";
}

/// <summary>The main agent as saved; null fields of an action are the ones it does not use.</summary>
public sealed record WhatsAppAssistantAgentRow(long Revision, string OriginalCulture, WhatsAppAssistantProfile Profile,
    IReadOnlyList<WhatsAppAssistantCase> Cases, WhatsAppAssistantAction Fallback, WhatsAppAssistantLimits Limits)
{
    public override string ToString() => "WhatsAppAssistantAgentRow { content: [REDACTED] }";
}

/// <summary>A published form the assistant can send, with «Cuándo lo ofrece» when the form has one (the cases of the forms block).</summary>
public sealed record WhatsAppAssistantFormResponse(Guid DefinitionId, string Name, string? OfferWhen);

/// <summary>
/// The screen «Asistente»: the main agent (revision 0 until its first save, with the defaults: «Cualquier otra cosa» in «No
/// responder»), the forms it can send, the business information as knowledge (read only) and whether the assistant can run.
/// </summary>
public sealed record WhatsAppAssistantAgentResponse(long Revision, string OriginalCulture, WhatsAppAssistantProfile Profile,
    IReadOnlyList<WhatsAppAssistantCase> Cases, WhatsAppAssistantAction Fallback, WhatsAppAssistantLimits Limits,
    IReadOnlyList<WhatsAppAssistantFormResponse> Forms, BusinessKnowledge Knowledge, AssistantAvailabilityResponse AssistantAvailability)
{
    public override string ToString() => "WhatsAppAssistantAgentResponse { content: [REDACTED] }";
}
