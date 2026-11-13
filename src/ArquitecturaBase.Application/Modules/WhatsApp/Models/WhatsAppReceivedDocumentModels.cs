using System.Text.Json.Serialization;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// A received document as Meta describes it in the webhook: the media id, the type, the SHA-256 in lowercase hex (null when it is not one)
/// and the person's file name (a photo has none). Anything may be missing: the message decides whether it can be downloaded, and keeps
/// the name anyway.
/// </summary>
public sealed record WhatsAppWebhookDocumentDescriptor(string? MediaId, string? ContentType, string? Sha256, string? FileName)
{
    public override string ToString() => "WhatsAppWebhookDocumentDescriptor { Metadata = [REDACTED] }";
}

/// <summary>
/// A received document with its conversation: what downloading and reading it need, who may have it read, its caption (its request,
/// until the contiguous text or audio pairs with it), until when it waits for a request, whether a text of the person (or an audio with
/// its transcription) came after it, and what was fixed when its reading was requested.
/// </summary>
public sealed record WhatsAppReceivedDocumentSourceRow
{
    public DateTime? RequestAwaitedUntilUtc { get; init; }
    public bool FollowedByText { get; init; }
    /// <summary>
    /// Task 9: the contiguous text (or audio with its transcription) is its request, before or after it, as a spreadsheet pairs: less
    /// than ten minutes apart and without an assistant reply requested in between (WhatsAppAssistantSourcePolicy).
    /// </summary>
    public bool PairedRequest { get; init; }
    public Guid MessageId { get; init; }
    public Guid ContactId { get; init; }
    public DateTime OccurredAtUtc { get; init; }
    public WhatsAppConversationMode Mode { get; init; }
    public Guid? LinkedUserId { get; init; }
    public long LinkRevision { get; init; }
    public WhatsAppDocumentState State { get; init; }
    public string? Caption { get; init; }
    public string? MediaId { get; init; }
    public string? ContentType { get; init; }
    public string? Sha256 { get; init; }
    public string? FileName { get; init; }
    public string? StorageKey { get; init; }
    public long? Size { get; init; }
    public string? CopySha256 { get; init; }
    public int? Pages { get; init; }
    public bool HasReading { get; init; }
    public Guid? RequestId { get; init; }
    public Guid? ActorUserId { get; init; }
    public string? ContextJson { get; init; }
    public long? CapturedLinkRevision { get; init; }
    public DateTime? ContentExpiresAtUtc { get; init; }
    public override string ToString() => "WhatsAppReceivedDocumentSourceRow { Metadata and context = [REDACTED] }";
}

/// <summary>What the operator's chat reads of a received document: its name, Meta's type, its copy, its pages and why it was not read.</summary>
public sealed record WhatsAppReceivedDocumentMessageRow(string? FileName, string? ContentType, string? StorageKey, int? Pages,
    WhatsAppDocumentState? State, string? FailureCode)
{
    /// <summary>Task 9: what the assistant did with it, from the latest reference that carries it; null when it did nothing.</summary>
    public WhatsAppReceivedDocumentWhyRow? Why { get; init; }
    public override string ToString() => "WhatsAppReceivedDocumentMessageRow { Metadata = [REDACTED] }";
}

/// <summary>
/// Task 9: the latest reference that carried a received document and what it sent: a prefilled form (its session, sealed) or a document
/// question (its options), and the option the person chose, when they chose one.
/// </summary>
public sealed record WhatsAppReceivedDocumentWhyRow(WhatsAppOutgoingMessageKind Kind, string? QuickRepliesJson, string? FormNameJson, string? FormCulture,
    WhatsAppAssistantPresentationRow? Assistant, string? Chosen)
{
    /// <summary>Review of task 9: the form of a prefilled form sent, named when the chat is read in the reader's language.</summary>
    public Guid? DefinitionId { get; init; }
    /// <summary>Review of task 9: the id of the option chosen in a document question (its form, or «Otro»).</summary>
    public string? ChosenReplyId { get; init; }
    public override string ToString() => "WhatsAppReceivedDocumentWhyRow { Content = [REDACTED] }";
}

/// <summary>
/// A received document in the operator's chat (task 8): its normalized name (absent for a photo that came without one: the chat titles
/// it «Foto»), its kind (<c>pdf</c>, <c>jpeg</c>, <c>png</c> or <c>docx</c>, as its owner detected it or, before, as Meta declared it;
/// absent when it is none of them) and its pages (a PDF's; a photo is one and a Word has none). <c>notLinked</c> says it was not read
/// because the number has no linked account (DOC4).
/// </summary>
public sealed record WhatsAppReceivedDocumentResponse([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pages { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ReasonCode { get; init; }

    /// <summary>Task 9: «Por qué lo propuso» or «Por qué preguntó» under the document; absent when the assistant did nothing with it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WhatsAppReceivedDocumentWhyResponse? Why { get; init; }

    public override string ToString() => "WhatsAppReceivedDocumentResponse { Name = [REDACTED] }";
}

/// <summary>
/// Task 9, «Por qué lo propuso» (<c>proposed</c>: the form it sent prefilled, how many data it read with doubts and, only with
/// <c>formrecords.read</c>, what it read) or «Por qué preguntó» (<c>asked</c>: the forms it offered, without «Otro», and the option the
/// person chose, when they chose one).
/// </summary>
public sealed record WhatsAppReceivedDocumentWhyResponse(string Kind, IReadOnlyList<string> Forms)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Chosen { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Doubtful { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<WhatsAppAssistantPrefillResponse>? Fields { get; init; }

    public override string ToString() => "WhatsAppReceivedDocumentWhyResponse { Content = [REDACTED] }";
}
