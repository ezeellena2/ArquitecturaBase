using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms.Documents;

/// <summary>
/// What happened to a received document, for its history. VC1-A: a row sent to its person as one of the loads of its spreadsheet («i/k» in
/// its detail), or past those loads. Stored as text: never renumber.
/// </summary>
public enum FormReceivedDocumentEventKind
{
    Received, Discarded, Reopened, ExtractionRequested, FormChanged, ExtractionFailed, Confirmed, ChoseOther, FormChosen, Sent, OverLimit,
}

/// <summary>
/// One step of the history of a received document (DOC5-A): when, what, who (none when its person or the server did it) and its detail,
/// protected by Application. An immutable fact: it is never changed, and it goes when its document is deleted.
/// </summary>
public sealed class FormReceivedDocumentEvent : Entity
{
    private FormReceivedDocumentEvent() { }

    public Guid DocumentId { get; private set; }
    public DateTime AtUtc { get; private set; }
    public FormReceivedDocumentEventKind Kind { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public byte[]? ProtectedDetail { get; private set; }

    public static Result<FormReceivedDocumentEvent> Record(Guid documentId, FormReceivedDocumentEventKind kind, Guid? actorUserId, DateTime atUtc,
        byte[]? protectedDetail = null) =>
        documentId == Guid.Empty || !Enum.IsDefined(kind) || actorUserId == Guid.Empty || atUtc.Kind != DateTimeKind.Utc || protectedDetail is { Length: 0 }
            ? FormReceivedDocumentErrors.ContentInvalid
            : new FormReceivedDocumentEvent { DocumentId = documentId, Kind = kind, ActorUserId = actorUserId, AtUtc = atUtc, ProtectedDetail = protectedDetail };

    public override string ToString() => "FormReceivedDocumentEvent { Content = [REDACTED] }";
}
