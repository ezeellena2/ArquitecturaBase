using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormPrefillRequest(Guid RecipientUserId, string Culture, AssistantObservedProposal Proposal,
    IReadOnlyList<Guid>? IncludedFieldIds = null, bool ForSending = false)
{
    public override string ToString() => "StructuredFormPrefillRequest { Content = [REDACTED] }";
}

public sealed record StructuredFormPrefillResponse(StructuredFormSelectionRow Form, long DefinitionRevision,
    IReadOnlyList<StructuredFormPrefillField> Fields, IReadOnlyList<StructuredFormCatalogOffer> Catalogs, string Reason)
{
    public override string ToString() => "StructuredFormPrefillResponse { Content = [REDACTED] }";
}

public sealed record StructuredFormPrefillField(Guid FieldId, string Key, string Type, string Label, string Value, string DisplayValue)
{ public override string ToString() => "StructuredFormPrefillField { Content = [REDACTED] }"; }
