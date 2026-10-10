namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record CheckFormPublicationRequest(long ExpectedRevision, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftInput Draft)
{
    public override string ToString() => "CheckFormPublicationRequest { Content = [REDACTED] }";
}
public sealed record FormPublicationIssueResponse(string? ChannelId, Guid? FieldId, Guid? ChoiceId, string Path, string Code, string Message, int? Limit, string? Culture);
public sealed record FormPublicationChannelResponse(string ChannelId, string ProfileRevision, string Compatibility, string ExternalEvidence, IReadOnlyList<string> Cultures);
public sealed record FormPublicationDiffResponse(Guid? BaseVersionId, IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Changed, IReadOnlyList<Guid> Removed);
public sealed record FormPublicationCheckResponse(Guid DefinitionId, long DraftRevision, string Compatibility, bool CanAccept,
    string AcceptanceAvailability, IReadOnlyList<FormPublicationChannelResponse> Channels, IReadOnlyList<FormPublicationIssueResponse> Issues, FormPublicationDiffResponse Diff, string? CheckToken = null, DateTime? CheckExpiresAtUtc = null)
{
    public override string ToString() => "FormPublicationCheckResponse { Content = [REDACTED] }";
}