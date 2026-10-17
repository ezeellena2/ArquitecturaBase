using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormAssistantCandidateRow(Guid DefinitionId, Guid VersionId, long VersionNumber, string Prefix,
    string NameJson, string SubmitLabelJson, string Culture, int QuestionCount, long DefinitionRevision,
    string OfferWhen, string SchemaJson)
{
    public override string ToString() => "FormAssistantCandidateRow { Content = [REDACTED] }";
}

public sealed record FormAssistantChoice(Guid Id, string Text)
{ public override string ToString() => "FormAssistantChoice { Content = [REDACTED] }"; }
public sealed record FormAssistantQuestion(Guid FieldId, string Type, string Label, int? MaxLength,
    Guid? CatalogId, long? CatalogRevision, IReadOnlyList<FormAssistantChoice> Choices)
{ public override string ToString() => "FormAssistantQuestion { Content = [REDACTED] }"; }
public sealed record FormAssistantCandidate(Guid DefinitionId, Guid VersionId, long DefinitionRevision, string Name,
    string OfferWhen, string OriginalCulture, IReadOnlyList<FormAssistantQuestion> Questions)
{ public override string ToString() => "FormAssistantCandidate { Content = [REDACTED] }"; }
public sealed record FormAssistantCandidatesPayload(string State, Guid RecipientUserId, string Culture, bool IsComplete,
    IReadOnlyList<FormAssistantCandidate> Forms) : AssistantCapabilityPayload;
public sealed record FormAssistantRegistrationPayload(string State, int ReadOrdinal, Guid DefinitionId, Guid VersionId,
    long ExpectedDefinitionRevision, IReadOnlyList<FormAssistantValue> Values, string Reason) : AssistantCapabilityPayload;

internal sealed record FormPreparedAssistantCandidate(StructuredFormSelectionRow Form, long DefinitionRevision,
    string OfferWhen, string OriginalCulture, IReadOnlyList<StructuredFormQuestion> Questions, IReadOnlyList<StructuredFormCatalogOffer> Catalogs)
{ public override string ToString() => "FormPreparedAssistantCandidate { Content = [REDACTED] }"; }
public sealed record FormAssistantValue(Guid FieldId, string Type, string Value)
{ public override string ToString() => "FormAssistantValue { Content = [REDACTED] }"; }
