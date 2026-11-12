using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>A button of a case as the model reads it: its title in the conversation's language and where it leads.</summary>
public sealed record WhatsAppAssistantCaseButtonView(string Title, string Destination, Guid? FormDefinitionId);

/// <summary>An active own case, in its order, with what the model needs to choose it (the fixed texts go out from the server).</summary>
public sealed record WhatsAppAssistantCaseView(Guid Id, int Order, string When, string Input, string Action, Guid? FormDefinitionId,
    string? Instructions, IReadOnlyList<WhatsAppAssistantCaseButtonView>? Buttons);

/// <summary>«Cualquier otra cosa»: always the last one.</summary>
public sealed record WhatsAppAssistantFallbackView(string Action, Guid? FormDefinitionId, string? Instructions,
    IReadOnlyList<WhatsAppAssistantCaseButtonView>? Buttons);

/// <summary>whatsapp.agent@1: the profile, the own cases in order, «Cualquier otra cosa», the limits and the allowed actions.</summary>
public sealed record WhatsAppAssistantAgentPayload(string Name, string Introduction, string Tone, string Does, string DoesNot,
    IReadOnlyList<WhatsAppAssistantCaseView> Cases, WhatsAppAssistantFallbackView Fallback, IReadOnlyList<string> BlockedTopics,
    string CannotPromise, int MaxReplyLength, IReadOnlyList<string> AllowedActions) : AssistantCapabilityPayload;

/// <summary>A confirmed fact of the business information, with the id «Usó» names.</summary>
public sealed record WhatsAppAssistantFactView(string Id, string Zone, string Label, string Text);

/// <summary>whatsapp.agent-knowledge@1: the confirmed facts, cut by the budget in their order (Truncated says so).</summary>
public sealed record WhatsAppAssistantKnowledgePayload(bool Available, IReadOnlyList<WhatsAppAssistantFactView> Facts, bool Truncated) : AssistantCapabilityPayload;

/// <summary>A message of the last 24 hours of this conversation: when (business time), who and the text, as data.</summary>
public sealed record WhatsAppAssistantHistoryMessageView(DateTimeOffset At, string From, string Text);

/// <summary>What the assistant did and how it ended: a form completed, still open or expired; a reply, a question or a handoff.</summary>
public sealed record WhatsAppAssistantHistoryActionView(DateTimeOffset At, string Kind, string? Name, string Outcome);

/// <summary>
/// whatsapp.agent-history@1: the last 24 hours of this conversation and never of another, what the assistant did with its
/// outcome, how many replies and forms it already used today, and the offers and declines (the evidence of a decline).
/// </summary>
public sealed record WhatsAppAssistantHistoryPayload(IReadOnlyList<WhatsAppAssistantHistoryMessageView> Messages,
    IReadOnlyList<WhatsAppAssistantHistoryActionView> Actions, int RepliesToday, int DailyReplyLimit, StructuredFormOffersPayload Offers)
    : AssistantCapabilityPayload;

/// <summary>
/// whatsapp.reply@1, validated by the server: the case it follows and what goes out, taken from the saved configuration (the
/// exact text, the question with its buttons or the handoff message), never written by the model.
/// </summary>
public sealed record WhatsAppAssistantReplyPayload(string Kind, string CaseSource, Guid? CaseId, string? CaseTitle, string Reason,
    string? Text, IReadOnlyList<WhatsAppAssistantCaseButtonView>? Buttons, string? HandoffReasonCode, string? HandoffReason) : AssistantCapabilityPayload;

/// <summary>
/// whatsapp.draft-reply@1, validated by the server (ADR 0021): the case, the drafted text, why and what it used. A draft that does
/// not pass comes back to the model with its problems (empty when it passes) and is never selected.
/// </summary>
public sealed record WhatsAppAssistantDraftPayload(string CaseSource, Guid? CaseId, string? CaseTitle, string Text, string Reason,
    IReadOnlyList<string> Used, IReadOnlyList<string> Problems) : AssistantCapabilityPayload;
