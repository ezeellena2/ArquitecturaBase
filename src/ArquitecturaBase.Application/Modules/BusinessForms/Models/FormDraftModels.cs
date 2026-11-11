using ArquitecturaBase.Application.Common.Pagination;
using System.Text.Json.Serialization;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record SaveFormDraftRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftInput Draft, long? ExpectedRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OfferWhen = null)
{
    // Set only by the creation from an assistant proposal; never comes from HTTP.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormDraftProposalOrigin? Proposal { get; init; }
    // Set only by that same creation: whose proposal it was, also for a channel's person, who has no web session.
    [JsonIgnore] public Guid? ActorUserId { get; init; }
    // Set only by «Agregar» from the library (BI1-A): the standard the draft comes from; never comes from HTTP.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormDraftStandardOrigin? Standard { get; init; }
    public override string ToString() => "SaveFormDraftRequest { Content = [REDACTED] }";
}
// AppOnly is «Dónde se completa» (SA1-A): true for «Sólo en la app», null otherwise, so every existing draft keeps its bytes.
public sealed record FormDraftInput(int Format, string Culture, IReadOnlyDictionary<string, string>? SubmitLabel, IReadOnlyList<FormFieldInput> Fields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormSection>? Sections = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AppOnly = null)
{ public override string ToString() => "FormDraftInput { Content = [REDACTED] }"; }
// A section title (CW15-B) is not a question: Start is how many questions go before it, and its title follows the language
// rules of a label. Without titles the list is null, so drafts, versions and their fingerprints keep their bytes.
public sealed record FormSection(int Start, IReadOnlyDictionary<string, string> Title)
{ public override string ToString() => "FormSection { Content = [REDACTED] }"; }
public sealed record FormFieldInput(string LocalId, Guid? FieldId, Guid? ReplacesFieldId, string Type,
    IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string?>? Help, bool Required,
    int? MaxLength, IReadOnlyList<FormChoiceInput>? Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormQuestionSource? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormFileSettings? Files = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormSelectionSettings? Selection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormRowSettings? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Repeated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormCalculation? Calculation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormShowWhen? ShowWhen = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormQuestionToggle>? Toggles = null)
{ public override string ToString() => "FormFieldInput { Content = [REDACTED] }"; }
public sealed record FormChoiceInput(string LocalId, Guid? ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "FormChoiceInput { Content = [REDACTED] }"; }
// GroupBy (LD1-A) is a field of the same catalog whose value is chosen first: the list then offers only the options with that
// value. Default (IV1-A) is the option of the catalog a new answer starts with, which a library standard declares by its code for
// a bound question of choosing one. Without them the properties are omitted, so drafts, versions and their fingerprints keep their bytes.
public sealed record FormQuestionSource(Guid? Catalog,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormCatalogMatch>? Match = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormCatalogPresentation? Presentation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? GroupBy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Default = null)
{ public override string ToString() => "FormQuestionSource { Content = [REDACTED] }"; }

// A filter compares a catalog field with a fixed value, a datum of the person or the answer of another question (CW7). In a
// draft, QuestionId names a question of the same request by its FieldId or, when it is new, by its LocalId; sealed, by its FieldId.
public sealed record FormCatalogMatch(Guid FieldId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Value = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PersonAttributeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? QuestionId = null)
{ public override string ToString() => "FormCatalogMatch { Content = [REDACTED] }"; }
public sealed record FormSelectionSettings(int MinSelected, int MaxSelected);
// The block of rows is the "group" question; its questions follow it, without gaps, marked Repeated.
public sealed record FormRowSettings(int MinRows, int MaxRows);
// CA1-A: a read-only decimal the server computes with a fixed operation ("multiply", "sum" or "percent") over questions that
// go before it. In a draft an operand names a question by its FieldId or, when it is new, by its LocalId; sealed, by its FieldId.
public sealed record FormCalculation(string Operation, IReadOnlyList<Guid> Operands)
{
    public const string Multiply = "multiply", Sum = "sum", Percent = "percent";
    public const int MaxSumOperands = 20;
    public override string ToString() => "FormCalculation { Content = [REDACTED] }";
}
// CO1-A: the question shows only when an earlier question outside the block has one of these answers: the identity of an own
// option or of a catalog option, or "true"/"false" for yes or no. A new question or option is named by its LocalId; sealed, by
// its identity.
public sealed record FormShowWhen(Guid QuestionId, IReadOnlyList<string> Values)
{
    public const int MaxValues = 50;
    public override string ToString() => "FormShowWhen { Content = [REDACTED] }";
}
// IV2-A: a yes-or-no toggle a library standard declares on a bound question («El precio incluye IVA»): its key, its text and its
// (i) by language, and the value chosen; the standard's draft brings the default. Only a bound question has toggles, and only
// their value changes; the issuer receives each one as a datum of its own, "true" or "false". Without toggles the list is null.
public sealed record FormQuestionToggle(string Key, IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string> Help, bool Value)
{
    public const int MaxToggles = 10;
    public override string ToString() => "FormQuestionToggle { Content = [REDACTED] }";
}
public sealed record FormFileSettings(int MinFiles, int MaxFiles, string? PhotoSource = null);
public sealed record CanonicalFormChoice(Guid ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "CanonicalFormChoice { Content = [REDACTED] }"; }
public sealed record CanonicalFormField(Guid FieldId, string Key, string Type, IReadOnlyDictionary<string, string> Label,
    IReadOnlyDictionary<string, string>? Help, bool Required, int? MaxLength, IReadOnlyList<CanonicalFormChoice>? Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormQuestionSource? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormFileSettings? Files = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormSelectionSettings? Selection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormRowSettings? Rows = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Repeated = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormCalculation? Calculation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormShowWhen? ShowWhen = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormQuestionToggle>? Toggles = null)
{ public override string ToString() => "CanonicalFormField { Content = [REDACTED] }"; }
public sealed record FormQuestionSchema(int Format, IReadOnlyList<CanonicalFormField> Fields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormSection>? Sections = null)
{ public override string ToString() => "FormQuestionSchema { Content = [REDACTED] }"; }
public sealed record FormDraftResponse(int Format, string Culture, IReadOnlyDictionary<string, string> SubmitLabel, IReadOnlyList<CanonicalFormField> Fields,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<FormSection>? Sections = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AppOnly = null)
{ public override string ToString() => "FormDraftResponse { Content = [REDACTED] }"; }
public sealed record FormDefinitionResponse(Guid Id, IReadOnlyDictionary<string, string> Name, string Prefix, bool PrefixLocked, long Revision, FormDraftResponse Draft, string? OfferWhen = null)
{
    /// <summary>BI1-A: the standard of the library it came from and its bound questions; omitted for any other form.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FormDefinitionStandardResponse? Standard { get; init; }
    public override string ToString() => "FormDefinitionResponse { Content = [REDACTED] }";
}
public sealed record FormFieldIdentityMap(string LocalId, Guid FieldId, string Key, Guid? ReplacesFieldId);
public sealed record FormChoiceIdentityMap(string FieldLocalId, string LocalId, Guid FieldId, Guid ChoiceId);
public sealed record FormIdentityMap(IReadOnlyList<FormFieldIdentityMap> Fields, IReadOnlyList<FormChoiceIdentityMap> Choices);
public sealed record FormChangeRecovery(DateTime ResultExpiresAtUtc);
public sealed record FormDraftCommitResponse(Guid RequestId, FormDefinitionResponse Definition, FormIdentityMap IdentityMap, FormChangeRecovery Recovery)
{ public override string ToString() => "FormDraftCommitResponse { Content = [REDACTED] }"; }
public sealed record FormRecoveredChangeResponse(Guid RequestId, string Kind, string Status, DateTime CommittedAtUtc,
    long CurrentRevision, FormDraftCommitResponse Result)
{ public override string ToString() => "FormRecoveredChangeResponse { Content = [REDACTED] }"; }
public sealed record FormRetiredIdentities(IReadOnlyList<Guid> Fields, IReadOnlyList<Guid> Choices)
{ public static FormRetiredIdentities Empty { get; } = new([], []); }
public sealed record FormDraftResolution(FormDraftResponse Draft, FormIdentityMap IdentityMap, FormRetiredIdentities NewlyRetiredIdentities);
public sealed record FormChangeProtectionContext(Guid Id, Guid Actor, Guid RequestId, Guid DefinitionId, string Kind, int Format);
public sealed record FormProtectedChange(byte[] Fingerprint, FormDraftCommitResponse Result)
{ public override string ToString() => "FormProtectedChange { Content = [REDACTED] }"; }
public sealed record FormAuthorResponse(Guid UserId, string? DisplayName);
public sealed record FormAuditResponse(DateTime CreatedAtUtc, DateTime? ModifiedAtUtc, DateTime LastChangedAtUtc, FormAuthorResponse? LastChangedBy);
public sealed record FormCapabilitiesResponse(bool CanSave, bool CanPublish, bool CanPreview);
public sealed record FormDefinitionDetailResponse(FormDefinitionResponse Definition, FormCapabilitiesResponse Capabilities, FormAuditResponse Audit,
    FormPublicationOverviewResponse Publication, FormDefinitionLifecycleResponse Lifecycle);
public sealed record FormActiveVersionResponse(Guid VersionId, long VersionNumber, long DraftRevision, IReadOnlyList<string> Cultures,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AppOnly = null);
public sealed record FormCurrentPublicationResponse(Guid VersionId, long VersionNumber, long DraftRevision, string State, string Stage,
    bool RequiresReview, bool CanRetry, Guid? RequestId);
public sealed record FormPublicationOverviewResponse(FormActiveVersionResponse? ActiveVersion, FormCurrentPublicationResponse? CurrentOperation, string DisplayState);
// AppOnly: the active version, or a draft never published, is completed only in the app (SA3-A). Omitted otherwise.
public sealed record FormDefinitionListResponse(Guid Id, IReadOnlyDictionary<string, string> Name, string Prefix, int QuestionCount, long? ActiveVersionNumber,
    string DisplayState, DateTime LastChangedAtUtc, FormAuthorResponse? LastChangedBy, bool CanEdit, FormDefinitionLifecycleResponse Lifecycle,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AppOnly = null);
public sealed record FormDefinitionListStoredRow(Guid Id, IReadOnlyDictionary<string, string> Name, string Prefix, int QuestionCount, long? ActiveVersionNumber,
    string DisplayState, DateTime LastChangedAtUtc, FormAuthorResponse? LastChangedBy, bool AppOnly = false);
public sealed record FormDefinitionCounts(int Active, int Published, int Drafts, int Deactivated);
public sealed record ListFormDefinitionsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["name", "questionCount", "lastChangedAtUtc"];
    public string PublicationFilter { get; init; } = "Active";
    public string DisplayCulture { get; init; } = "es";
    // CP1-A: a folder with its subfolders, or the items without a folder (WithoutFolder).
    public Guid? FolderId { get; init; }
    public bool WithoutFolder { get; init; }
}
public sealed record FormDefinitionStoredRow(Guid Id, string NameJson, string Prefix, bool PrefixLocked, long Revision,
    string Culture, string SubmitLabelJson, string SchemaJson, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc, DateTime LastChangedAtUtc, FormAuthorResponse? Author, string? OfferWhen = null,
    bool AppOnly = false)
{
    // BI1-A: the standard it came from, its version and its bound questions (datum key → field id).
    public string? StandardKey { get; init; }
    public int? StandardVersion { get; init; }
    public string? StandardBindingsJson { get; init; }
}
public sealed record FormChangeStoredRow(FormChangeProtectionContext Context, byte[]? ProtectedResult,
    DateTime ResultExpiresAtUtc, DateTime CommittedAtUtc, long CurrentRevision);
public sealed record FormDefinitionFacetsResponse(int Active, int Published, int Drafts, int Deactivated);
