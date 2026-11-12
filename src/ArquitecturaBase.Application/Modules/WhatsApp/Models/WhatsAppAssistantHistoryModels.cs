using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>A message of the conversation for the assistant's history: the text (a transcript for an audio) and who wrote it.</summary>
public sealed record WhatsAppAssistantHistoryMessageRow(DateTime OccurredAtUtc, WhatsAppMessageDirection Direction, WhatsAppMessageOrigin Origin,
    WhatsAppMessageKind Kind, string? Body)
{
    public override string ToString() => "WhatsAppAssistantHistoryMessageRow { Content = [REDACTED] }";
}

/// <summary>
/// What the assistant did in the conversation and how it ended: a form (its name and whether it was completed), a reply, a
/// question with buttons or a handoff, with the why it kept.
/// </summary>
public sealed record WhatsAppAssistantHistoryActionRow(DateTime AtUtc, WhatsAppOutgoingMessageKind Kind, WhatsAppOutgoingMessageState State,
    string? FormNameJson, string? FormCulture, WhatsAppFlowCaptureState? CaptureState, DateTime? FormExpiresAtUtc, bool IsHandoff, string? AssistantReplyJson)
{
    public override string ToString() => "WhatsAppAssistantHistoryActionRow { Content = [REDACTED] }";
}
