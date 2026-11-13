using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// Task 9 of the documents: the form a received document goes to, prepared by formularios and resolved for the send outside the unit of
/// work: the inbox's document, the send context, the prefill (what was read, as initial values), the mark of each datum, the copy the
/// server adds on capture when the person does not choose another, the proposal that recognized it, the reason the response shows and
/// the reply deadline the inbox registered, which the session takes as its expiry.
/// </summary>
internal sealed record WhatsAppAssistantDocumentSend(Guid DocumentId, WhatsAppFormSendContext Context, StructuredFormPrefillResponse Prefill,
    IReadOnlyList<StructuredDocumentFieldMark> Marks, WhatsAppFlowServerFile? File, StructuredRecordAssistantProposal Proposal, string Reason,
    DateTime ExpiresAtUtc)
{
    public override string ToString() => "WhatsAppAssistantDocumentSend { Content = [REDACTED] }";
}

/// <summary>
/// Review of task 9: whether a tap on a document question may still change something: <c>Open</c>, the person's automatic conversation
/// with the question's link before its content expires and no other tap on it applied; <c>Answered</c>, another tap on the same question
/// was applied (it is single use, like a clarification); <c>Closed</c>, an operator took over, the number was unlinked or it expired.
/// </summary>
public enum WhatsAppAssistantDocumentTapState { Open, Answered, Closed }

/// <summary>Task 9: the stable code and the reason of a handoff after a document, in the person's language.</summary>
internal sealed record WhatsAppAssistantDocumentHandoff(string ReasonCode, string Reason)
{
    public override string ToString() => "WhatsAppAssistantDocumentHandoff { Content = [REDACTED] }";
}

/// <summary>
/// Data the server adds on capture to what the person sent (task 9, generic so other sends can reuse it): a file of the channel's own
/// private copy for a question of a photo or a document, by its key, name, media type, size and SHA-256, checked when it is read.
/// </summary>
public sealed record WhatsAppFlowServerFile(Guid FieldId, string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "WhatsAppFlowServerFile { Content = [REDACTED] }";
}

/// <summary>
/// What the server sealed with a send besides the origin (task 9): the file it adds on capture and the mark of each datum the form
/// went with. Both are omitted when absent, so the contents sealed before keep their bytes.
/// </summary>
public sealed record WhatsAppFlowServerData(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] WhatsAppFlowServerFile? File,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<StructuredDocumentFieldMark>? Marks)
{
    /// <summary>Task 9b: the send is a load of a spreadsheet (VC1-A), so the notice atop its Flow names the spreadsheet; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Spreadsheet { get; init; }
    /// <summary>Task 9c: the send is the form again after a fixed validation, so the notice atop its Flow asks to correct what is marked; null otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Correction { get; init; }
    public override string ToString() => "WhatsAppFlowServerData { Content = [REDACTED] }";
}
