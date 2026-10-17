using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormSuggestionsResponse(Guid SourceRecordId, Guid? SuggestionSetId, long Revision,
    string SourceName, string SourceNumber, IReadOnlyList<StructuredRecordReadValue> SourceValues,
    IReadOnlyList<StructuredFormSuggestionCandidate> Candidates)
{
    public override string ToString() => "StructuredFormSuggestionsResponse { Content = [REDACTED] }";
}
public sealed record StructuredFormSuggestionCandidate(Guid SuggestionId, string Mode, string? When,
    StructuredFormSelectionRow Form, long DefinitionRevision, string OriginalCulture,
    IReadOnlyList<StructuredFormQuestion> Questions, IReadOnlyList<StructuredFormCatalogOffer> Catalogs,
    IReadOnlyList<StructuredFormPrefillField> Fields, bool CreatesAutomaticLoop)
{
    public override string ToString() => "StructuredFormSuggestionCandidate { Content = [REDACTED] }";
}
public sealed record StructuredFormSuggestionPreparationRequest(Guid RecipientUserId, string Culture, Guid SourceRecordId,
    Guid SuggestionSetId, Guid SuggestionId, IReadOnlyList<Guid>? IncludedFieldIds = null,
    AssistantObservedProposal? Proposal = null, bool ForSending = false)
{
    // Server-only: null is automatic preparation for the linked Subject; manual preview/send set their actual actor.
    public Guid? ActorUserId { get; init; }
    public override string ToString() => "StructuredFormSuggestionPreparationRequest { Content = [REDACTED] }";
}
public sealed record StructuredFormSuggestionPreparationResponse(StructuredFormPrefillResponse Prefill,
    IReadOnlyList<StructuredFormPrefillField> ProposedFields, StructuredRecordSuggestionOrigin OriginSuggestion, bool CreatesAutomaticLoop)
{
    public override string ToString() => "StructuredFormSuggestionPreparationResponse { Content = [REDACTED] }";
}
