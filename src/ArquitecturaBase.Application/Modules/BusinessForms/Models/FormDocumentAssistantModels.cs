using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// The two documents reads (decision 8): in a channel, the forms with reading on the person who sent the document can complete
// (forms.documents.candidates@1, whose UserId is the Subject); in the inbox, only the form the server fixed (forms.documents.form@1,
// whose UserId is the reviewer, the Actor). Each form brings «Cómo reconocerlo» and the questions to read, with their type, whether
// they are required, their own options or the name of their catalog. A read that does not fit is partial and never proves absence.
public sealed record FormDocumentReadPayload(string State, Guid UserId, string Culture, bool IsComplete, IReadOnlyList<FormDocumentCandidate> Forms)
    : AssistantCapabilityPayload;
public sealed record FormDocumentCandidate(Guid DefinitionId, Guid VersionId, long DefinitionRevision, string Name, string? Recognition,
    IReadOnlyList<FormDocumentQuestion> Questions)
{
    public override string ToString() => "FormDocumentCandidate { Content = [REDACTED] }";
}
public sealed record FormDocumentQuestion(Guid FieldId, string Type, string Label, bool Required, int? MaxLength, IReadOnlyList<FormAssistantChoice> Choices,
    string? Catalog)
{
    public override string ToString() => "FormDocumentQuestion { Content = [REDACTED] }";
}

// forms.documents.propose-entry@1: the outcome (sure, doubt or unknown), one to three candidates of the forms one of the two reads
// showed in the turn, with the value read for each question (null when it was not read) and whether it was read with doubts, and the
// type of document in a few generic words. Its owner validates it without AI and never trims it.
internal sealed record FormDocumentEntryArguments(string Outcome, IReadOnlyList<FormDocumentEntryArgumentCandidate> Candidates, string DocumentType)
{
    public override string ToString() => "FormDocumentEntryArguments { Content = [REDACTED] }";
}
internal sealed record FormDocumentEntryArgumentCandidate(Guid DefinitionId, Guid VersionId, IReadOnlyList<FormDocumentEntryArgumentValue> Values)
{
    public override string ToString() => "FormDocumentEntryArgumentCandidate { Content = [REDACTED] }";
}
internal sealed record FormDocumentEntryArgumentValue(Guid FieldId, string? Value, bool Doubtful)
{
    public override string ToString() => "FormDocumentEntryArgumentValue { Content = [REDACTED] }";
}

// The proposal as its owner normalized it: each value canonical and typed by the question it observed, with the read it came from.
public sealed record FormDocumentEntryProposal(string State, int ReadOrdinal, string Outcome, IReadOnlyList<FormDocumentEntryCandidate> Candidates,
    string DocumentType) : AssistantCapabilityPayload
{
    public const string Sure = "sure";
    public const string Doubt = "doubt";
    public const string Unknown = "unknown";
    public const int MaxDocumentTypeLength = 60;
}
public sealed record FormDocumentEntryCandidate(Guid DefinitionId, Guid VersionId, long ExpectedDefinitionRevision, IReadOnlyList<FormDocumentEntryValue> Values)
{
    public override string ToString() => "FormDocumentEntryCandidate { Content = [REDACTED] }";
}
public sealed record FormDocumentEntryValue(Guid FieldId, string Type, string? Value, bool Doubtful)
{
    public override string ToString() => "FormDocumentEntryValue { Content = [REDACTED] }";
}

// A proposal that does not pass goes back to the model with the path and the reason of each problem, to propose it again corrected.
public sealed record FormDocumentEntryRejection(IReadOnlyList<FormDocumentEntryIssue> Issues) : AssistantCapabilityPayload;
public sealed record FormDocumentEntryIssue(string Path, string Code)
{
    public const string NotObserved = "notObserved";
    public const string UnknownQuestion = "unknownQuestion";
    public const string Invalid = "invalid";
    public const string NotOffered = "notOffered";
    public const string FilledByServer = "filledByServer";
    public const string Repeated = "repeated";
    public const string Count = "count";
    public const string PersonalData = "personalData";
    public static readonly string[] Codes = [NotObserved, UnknownQuestion, Invalid, NotOffered, FilledByServer, Repeated, Count, PersonalData];
}
