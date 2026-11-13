using System.Text.Json.Serialization;
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

// What the model has to fix, by the path of its own arguments; a rejected proposal is never selectable. Name is the required
// field a mapping leaves out, as the catalogs read names it, which has no argument of its own.
public sealed record FormAuthoringIssue(string Path, string Code)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}
public sealed record FormAuthoringRejection(IReadOnlyList<FormAuthoringIssue> Issues) : AssistantCapabilityPayload;
public sealed record FormAuthoringCatalogProposal(string Culture, string Name, string Description, string ValueType, string Code,
    string Option, IReadOnlyList<FormAuthoringProposedField> Fields) : AssistantCapabilityPayload;

// Stage 2: what a proposal takes from the turn's spreadsheet, for its card (IA1-A). Each mapped column, ordered as the catalog
// (code, option, value, detail and fields), says what it fills (Target), its letter and its header: the text of the first row with
// content when it holds headers, since the prepared sheet skips empty rows. Field names the field and Key is the key of an existing
// catalog's field. RowCount counts the rows of data and each row of the sample (the first ones after the headers, up to three) has
// one text per column, empty without one.
public sealed record FormAuthoringMappedColumn(string Target, string? Field, string? Key, string Letter, string? Header)
{ public override string ToString() => "FormAuthoringMappedColumn { Content = [REDACTED] }"; }
public sealed record FormAuthoringSpreadsheetSummary(string FileName, string SheetName, int RowCount, bool HasHeaders,
    IReadOnlyList<FormAuthoringMappedColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Sample)
{ public override string ToString() => "FormAuthoringSpreadsheetSummary { Content = [REDACTED] }"; }

// forms.authoring.propose-catalog@2: version 1's catalog and, when it maps the spreadsheet, what it takes from it. Creating keeps
// version 1's shape: the spreadsheet is never kept with the catalog.
public sealed record FormAuthoringMappedCatalogProposal(string Culture, string Name, string Description, string ValueType, string Code,
    string Option, IReadOnlyList<FormAuthoringProposedField> Fields, FormAuthoringSpreadsheetSummary? Spreadsheet) : AssistantCapabilityPayload
{
    public FormAuthoringCatalogProposal ToCatalog() => new(Culture, Name, Description, ValueType, Code, Option, Fields);
}

// forms.authoring.propose-import@1: importing the turn's spreadsheet into a Manual catalog that exists. A person reviews the
// import and imports it; the assistant never does.
public sealed record FormAuthoringImportCatalog(Guid Id, string Name, string ValueType);
public sealed record FormAuthoringImportProposal(FormAuthoringImportCatalog Catalog, FormAuthoringSpreadsheetSummary Spreadsheet) : AssistantCapabilityPayload;

// forms.authoring.propose-form@2 (Etapa 3 de la IA): version 1's form and, with the turn's document, its reading on with «Cómo
// reconocerlo» and what each question read in it (Sample, for DOC6 to compare); Standard is a similar library standard, as the library
// read of the same turn showed it. Creating keeps version 1's form with the reading on: the samples and the standard are never kept.
public sealed record FormAuthoringDocumentQuestion(string Label, string Type, bool Required, IReadOnlyList<string> Options,
    FormAuthoringProposedCatalog? Catalog, string? Sample)
{ public override string ToString() => "FormAuthoringDocumentQuestion { Content = [REDACTED] }"; }
public sealed record FormAuthoringProposedReading(string Recognition)
{ public override string ToString() => "FormAuthoringProposedReading { Content = [REDACTED] }"; }
public sealed record FormAuthoringProposedStandard(string Key, string Name);
public sealed record FormAuthoringDocumentFormProposal(string Culture, string Name, string Prefix, string SubmitLabel,
    IReadOnlyList<FormAuthoringDocumentQuestion> Questions, FormAuthoringProposedReading? DocumentReading,
    FormAuthoringProposedStandard? Standard) : AssistantCapabilityPayload
{
    public const int MaxSampleLength = 160;

    public FormAuthoringFormProposal ToForm() => new(Culture, Name, Prefix, SubmitLabel, Questions.Select(question =>
        new FormAuthoringProposedQuestion(question.Label, question.Type, question.Required, question.Options, question.Catalog)).ToArray());
}

// forms.library.standards@1 (DOC6): the library standards that read documents, with their name and description in the conversation
// language and the data each one reads, by the names of its questions, so a proposal names a similar one by its key.
public sealed record FormLibraryStandardRow(string Key, string Name, string Description, IReadOnlyList<string> Reads);
public sealed record FormLibraryStandardsPayload(bool IsComplete, IReadOnlyList<FormLibraryStandardRow> Standards) : AssistantCapabilityPayload;

// Why the assistant cannot build what was asked (IA3-A): a channel says it with a fixed text and, for a form or catalog that
// already exists, its link. Name is that one's name, empty without one.
public sealed record FormAuthoringDecline(string Reason, Guid? DefinitionId, Guid? CatalogId, string Name) : AssistantCapabilityPayload;

public sealed record FormAuthoringDefinitionStoredRow(string NameJson, string Culture, string Prefix, bool IsPublished, bool IsInactive,
    string SchemaJson)
{ public override string ToString() => "FormAuthoringDefinitionStoredRow { Content = [REDACTED] }"; }
public sealed record FormAuthoringCatalogStoredRow(Guid Id, string NameJson, string OriginalCulture, BusinessFormCatalogKind Kind,
    BusinessFormCatalogValueType ValueType, bool IsActive, string? FieldsJson)
{ public override string ToString() => "FormAuthoringCatalogStoredRow { Content = [REDACTED] }"; }

// Creating what a Home proposal describes: the turn's request and the proposal; the answer says what was created, if anything.
public sealed record CreateFromAssistantProposalRequest(Guid? RequestId, Guid? ProposalId);
public sealed record FormDraftAssistantProposalResponse(Guid ProposalId, Guid? DefinitionId);
public sealed record FormCatalogAssistantProposalResponse(Guid ProposalId, Guid? CatalogId);

// Where a draft created from a proposal came from, kept once with it. RequestedIn is "app" when the person asked in the app.
// DocumentReading is true when a version 2 proposal turned the reading on: «Cómo reconocerlo» came from a document the model read,
// so its text lives only in the draft, where it is edited, and the samples are never kept. Without a reading the key is omitted.
public sealed record FormDraftProposalEvidence(string OriginalCulture, Guid RequestedByUserId, DateTime RequestedAtUtc, Guid TurnId,
    string CapabilityId, int CapabilityVersion, AssistantOperationProvenance Provenance, FormAuthoringFormProposal OriginalProposal,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RequestedIn = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DocumentReading = null)
{ public override string ToString() => "FormDraftProposalEvidence { Content = [REDACTED] }"; }

/// <summary>The proposal a new draft records; only the service that creates from a proposal sets it.</summary>
public sealed record FormDraftProposalOrigin(Guid ProposalId, string EvidenceJson)
{
    /// <summary>
    /// What a proposal from received documents (DOC6) leaves in the same save once it has the new definition: the draft its request created
    /// (<c>FormDocumentProposalRecorder</c>). The draft's save only runs it.
    /// </summary>
    [JsonIgnore]
    public Func<BusinessFormDefinition, CancellationToken, Task<ArquitecturaBase.Domain.Results.Result>>? RecordAsync { get; init; }
    public override string ToString() => "FormDraftProposalOrigin { Content = [REDACTED] }";
}

public sealed record FormAuthoringNamedRow(Guid Id, string NameJson, string Culture)
{ public override string ToString() => "FormAuthoringNamedRow { Content = [REDACTED] }"; }
public sealed record FormAuthoringCreatedRow(Guid Id, string EvidenceJson)
{ public override string ToString() => "FormAuthoringCreatedRow { Content = [REDACTED] }"; }
