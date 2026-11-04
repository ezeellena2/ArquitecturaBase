using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// What the authoring assistant may read: names and fields of what already exists, never answers, catalog options or people.
public sealed record FormAuthoringQuestionRow(string Label, string Type);
public sealed record FormAuthoringFormRow(string Name, string Prefix, string State, IReadOnlyList<FormAuthoringQuestionRow> Questions);
public sealed record FormAuthoringFormsPayload(bool IsComplete, IReadOnlyList<FormAuthoringFormRow> Forms) : AssistantCapabilityPayload;
public sealed record FormAuthoringCatalogFieldRow(string Name, string Type);
public sealed record FormAuthoringCatalogRow(Guid Id, string Name, string Kind, string ValueType, bool IsActive,
    IReadOnlyList<FormAuthoringCatalogFieldRow> Fields);
public sealed record FormAuthoringCatalogsPayload(bool IsComplete, IReadOnlyList<FormAuthoringCatalogRow> Catalogs) : AssistantCapabilityPayload;

// A proposal validated with the authoring rules and normalized: the card, the summary and the creation use exactly this.
public sealed record FormAuthoringProposedCatalog(Guid Id, string Name);
public sealed record FormAuthoringProposedQuestion(string Label, string Type, bool Required, IReadOnlyList<string> Options,
    FormAuthoringProposedCatalog? Catalog);
public sealed record FormAuthoringFormProposal(string Culture, string Name, string Prefix, string SubmitLabel,
    IReadOnlyList<FormAuthoringProposedQuestion> Questions) : AssistantCapabilityPayload;
public sealed record FormAuthoringProposedField(string Name, string Type);

// What the model has to fix, by the path of its own arguments; a rejected proposal is never selectable.
public sealed record FormAuthoringIssue(string Path, string Code);
public sealed record FormAuthoringRejection(IReadOnlyList<FormAuthoringIssue> Issues) : AssistantCapabilityPayload;
public sealed record FormAuthoringCatalogProposal(string Culture, string Name, string Description, string ValueType, string Code,
    string Option, IReadOnlyList<FormAuthoringProposedField> Fields) : AssistantCapabilityPayload;

public sealed record FormAuthoringDefinitionStoredRow(string NameJson, string Culture, string Prefix, bool IsPublished, bool IsInactive,
    string SchemaJson)
{ public override string ToString() => "FormAuthoringDefinitionStoredRow { Content = [REDACTED] }"; }
public sealed record FormAuthoringCatalogStoredRow(Guid Id, string NameJson, string OriginalCulture, BusinessFormCatalogKind Kind,
    BusinessFormCatalogValueType ValueType, bool IsActive, string? FieldsJson)
{ public override string ToString() => "FormAuthoringCatalogStoredRow { Content = [REDACTED] }"; }
