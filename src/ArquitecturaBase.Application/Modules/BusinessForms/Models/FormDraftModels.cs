using ArquitecturaBase.Application.Common.Pagination;
using System.Text.Json.Serialization;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record SaveFormDraftRequest(Guid RequestId, IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftInput Draft, long? ExpectedRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OfferWhen = null)
{ public override string ToString() => "SaveFormDraftRequest { Content = [REDACTED] }"; }
public sealed record FormDraftInput(int Format, string Culture, IReadOnlyDictionary<string, string>? SubmitLabel, IReadOnlyList<FormFieldInput> Fields)
{ public override string ToString() => "FormDraftInput { Content = [REDACTED] }"; }
public sealed record FormFieldInput(string LocalId, Guid? FieldId, Guid? ReplacesFieldId, string Type,
    IReadOnlyDictionary<string, string> Label, IReadOnlyDictionary<string, string?>? Help, bool Required,
    int? MaxLength, IReadOnlyList<FormChoiceInput>? Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormQuestionSource? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormFileSettings? Files = null)
{ public override string ToString() => "FormFieldInput { Content = [REDACTED] }"; }
public sealed record FormChoiceInput(string LocalId, Guid? ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "FormChoiceInput { Content = [REDACTED] }"; }
public sealed record FormQuestionSource(Guid? Catalog);
public sealed record FormFileSettings(int MinFiles, int MaxFiles, string? PhotoSource = null);
public sealed record CanonicalFormChoice(Guid ChoiceId, IReadOnlyDictionary<string, string> Label)
{ public override string ToString() => "CanonicalFormChoice { Content = [REDACTED] }"; }
public sealed record CanonicalFormField(Guid FieldId, string Key, string Type, IReadOnlyDictionary<string, string> Label,
    IReadOnlyDictionary<string, string>? Help, bool Required, int? MaxLength, IReadOnlyList<CanonicalFormChoice>? Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormQuestionSource? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] FormFileSettings? Files = null)
{ public override string ToString() => "CanonicalFormField { Content = [REDACTED] }"; }
public sealed record FormQuestionSchema(int Format, IReadOnlyList<CanonicalFormField> Fields)
{ public override string ToString() => "FormQuestionSchema { Content = [REDACTED] }"; }
public sealed record FormDraftResponse(int Format, string Culture, IReadOnlyDictionary<string, string> SubmitLabel, IReadOnlyList<CanonicalFormField> Fields)
{ public override string ToString() => "FormDraftResponse { Content = [REDACTED] }"; }
public sealed record FormDefinitionResponse(Guid Id, IReadOnlyDictionary<string, string> Name, string Prefix, bool PrefixLocked, long Revision, FormDraftResponse Draft, string? OfferWhen = null)
{ public override string ToString() => "FormDefinitionResponse { Content = [REDACTED] }"; }
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
    FormPublicationOverviewResponse Publication);
public sealed record FormActiveVersionResponse(Guid VersionId, long VersionNumber, long DraftRevision, IReadOnlyList<string> Cultures);
public sealed record FormCurrentPublicationResponse(Guid VersionId, long VersionNumber, long DraftRevision, string State, string Stage,
    bool RequiresReview, bool CanRetry, Guid? RequestId);
public sealed record FormPublicationOverviewResponse(FormActiveVersionResponse? ActiveVersion, FormCurrentPublicationResponse? CurrentOperation, string DisplayState);
public sealed record FormDefinitionListResponse(Guid Id, IReadOnlyDictionary<string, string> Name, string Prefix, int QuestionCount, long? ActiveVersionNumber,
    string DisplayState, DateTime LastChangedAtUtc, FormAuthorResponse? LastChangedBy, bool CanEdit);
public sealed record FormDefinitionCounts(int All, int Published, int Drafts);
public sealed record ListFormDefinitionsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["name", "questionCount", "lastChangedAtUtc"];
    public string PublicationFilter { get; init; } = "All";
    public string DisplayCulture { get; init; } = "es";
}
public sealed record FormDefinitionStoredRow(Guid Id, string NameJson, string Prefix, bool PrefixLocked, long Revision,
    string Culture, string SubmitLabelJson, string SchemaJson, DateTime CreatedAtUtc, DateTime? ModifiedAtUtc, DateTime LastChangedAtUtc, FormAuthorResponse? Author, string? OfferWhen = null);
public sealed record FormChangeStoredRow(FormChangeProtectionContext Context, byte[]? ProtectedResult,
    DateTime ResultExpiresAtUtc, DateTime CommittedAtUtc, long CurrentRevision);
public sealed record FormDefinitionFacetsResponse(int All, int Published, int Drafts);
