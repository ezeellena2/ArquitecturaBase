using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// VC1-A: the rows of a spreadsheet a person with a linked and active account sent (decision 12): who and when, the channel's copy of the
/// spreadsheet (an xlsx or a CSV, without pages) and the interpretation's authentic proposal of forms.documents.propose-rows@1 on its sample,
/// with the SHA-256 of that sample, which must be the copy's; without an interpretation, why (only the day's quota). The channel and its
/// opaque source id register it once; the reply deadline is when the person stops being waited for in each load.
/// </summary>
public sealed record StructuredDocumentRowsRegistrationRequest(string ChannelId, string SourceId, Guid SenderUserId, string Culture,
    DateTime ReceivedAtUtc, StructuredDocumentCopy Copy)
{
    public AssistantObservedProposal? Proposal { get; init; }
    public string? SampleSha256 { get; init; }
    public StructuredDocumentNotRead? NotRead { get; init; }
    public DateTime? ReplyExpiresAtUtc { get; init; }
    public override string ToString() => "StructuredDocumentRowsRegistrationRequest { Content = [REDACTED] }";
}

/// <summary>
/// What became of a row (VC1-A): a load sent to its person, one whose form cannot be sent to them (CE4-A), one past the three loads of its
/// spreadsheet, a repeated one, one that fits no form, or one the model did not see.
/// </summary>
public enum StructuredDocumentRowKind { Load, NotSendable, OverLimit, Repeated, Unmatched, Unseen }

/// <summary>
/// A row with its entry of the inbox: its sheet (only when the book has more than one) and number, what became of it, the form it was mapped
/// to with its name in the person's language, which load it is (from 1, of <see cref="StructuredDocumentRowsRegistration.Loads"/>), its summary
/// (its first two data, as the person reads them) and, for a repeated one, the response with the same data or the earlier row it is equal to.
/// </summary>
public sealed record StructuredDocumentRowRegistration(Guid DocumentId, string? Sheet, int Number, StructuredDocumentRowKind Kind)
{
    public StructuredDocumentFormChoice? Form { get; init; }
    public int? Position { get; init; }
    public IReadOnlyList<string> Summary { get; init; } = [];
    public StructuredDocumentDuplicate? Duplicate { get; init; }
    public StructuredDocumentRowReference? SameAs { get; init; }
    public override string ToString() => $"StructuredDocumentRowRegistration {{ Kind = {Kind}, Content = [REDACTED] }}";
}

/// <summary>A row of the same spreadsheet: its sheet (only when the book has more than one) and its number.</summary>
public sealed record StructuredDocumentRowReference(string? Sheet, int Number);

/// <summary>
/// What to tell the person about a spreadsheet (VC1-A): <c>Sure</c>, its rows, in the order of the spreadsheet; <c>Unknown</c>, none fits and
/// it is one entry, with the forms the person can choose («No lo reconoce», DOC2); <c>Duplicate</c>, the same spreadsheet again, one entry
/// discarded with when the first one arrived; <c>Quota</c>; or <c>Unavailable</c>. The deadline is the first registration's, never recomputed.
/// </summary>
public sealed record StructuredDocumentRowsRegistration(StructuredDocumentReplyKind Kind)
{
    public IReadOnlyList<StructuredDocumentRowRegistration> Rows { get; init; } = [];
    /// <summary>The single entry of a spreadsheet without rows in the inbox; null with rows.</summary>
    public Guid? DocumentId { get; init; }
    public IReadOnlyList<StructuredDocumentFormChoice> Forms { get; init; } = [];
    public StructuredDocumentDuplicate? Duplicate { get; init; }
    public bool WasAlreadyRegistered { get; init; }
    public DateTime? ReplyExpiresAtUtc { get; init; }
    /// <summary>How many loads go to the person.</summary>
    public int Loads => Rows.Count(row => row.Kind == StructuredDocumentRowKind.Load);
    public override string ToString() => "StructuredDocumentRowsRegistration { Content = [REDACTED] }";
}
