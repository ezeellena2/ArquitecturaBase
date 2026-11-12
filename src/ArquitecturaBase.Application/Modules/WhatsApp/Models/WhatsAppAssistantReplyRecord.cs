using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>Where the action of a reply came from: an own case, a form's «when to offer» or «Cualquier otra cosa».</summary>
public enum WhatsAppAssistantCaseSource { Own, Form, Fallback }

/// <summary>
/// «Por qué respondió esto» (Casos del asistente): the action, the case it followed (its «when», as the business wrote it), why,
/// what it used and its place in the reply («1 de 3»); for a handoff, the stable code of the reason and the case's own reason.
/// The outgoing message keeps it and the message after it goes out; an action that sent nothing keeps it in its reference. It is
/// deleted with the text.
/// </summary>
public sealed record WhatsAppAssistantReplyRecord(WhatsAppAssistantActionKind Kind, WhatsAppAssistantCaseSource CaseSource, Guid? CaseId,
    string? CaseTitle, string Reason, IReadOnlyList<string> Used, int Ordinal, int Count, string? HandoffReasonCode = null, string? HandoffReason = null,
    string? Button = null)
{
    public override string ToString() => "WhatsAppAssistantReplyRecord { Content = [REDACTED] }";
}

/// <summary>
/// The same «Por qué respondió esto» in the chat, with <c>whatsapp.read</c>. <c>Button</c> is the title the person tapped when
/// the action answered a button of a case («Tocó «Sí»»).
/// </summary>
public sealed record WhatsAppAssistantReplyResponse(WhatsAppAssistantActionKind Kind, WhatsAppAssistantCaseSource CaseSource, string? CaseTitle,
    string Reason, IReadOnlyList<string> Used, int Ordinal, int Count, string? HandoffReasonCode, string? HandoffReason, string? Button = null)
{
    public static WhatsAppAssistantReplyResponse From(WhatsAppAssistantReplyRecord record) => new(record.Kind, record.CaseSource, record.CaseTitle,
        record.Reason, record.Used, record.Ordinal, record.Count, record.HandoffReasonCode, record.HandoffReason, record.Button);

    public override string ToString() => "WhatsAppAssistantReplyResponse { Content = [REDACTED] }";
}
