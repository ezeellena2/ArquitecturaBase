using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormResponseAssistanceResponse(string SenderKind, FormAuthorResponse? Operator, string? Reason,
    IReadOnlyList<FormResponseAssistedValueResponse> Values)
{
    public FormResponseSuggestionResponse? Suggestion { get; init; }
    public override string ToString() => "FormResponseAssistanceResponse { Content = [REDACTED] }";
}
public sealed record FormResponseAssistedValueResponse(StructuredRecordReadValue Proposed, bool Included, bool Changed, bool Mapped)
{
    public override string ToString() => "FormResponseAssistedValueResponse { Content = [REDACTED] }";
}

public sealed record FormResponseSuggestionResponse(Guid OriginRecordId, Guid SuggestionId, string Mode)
{ public override string ToString() => "FormResponseSuggestionResponse { Content = [REDACTED] }"; }
