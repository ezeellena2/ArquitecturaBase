using ArquitecturaBase.Application.Common.Pagination;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormSuggestionMappingSource(string Kind, Guid? FieldId = null);
public sealed record FormSuggestionMapping(FormSuggestionMappingSource Source, Guid DestinationFieldId);
public sealed record FormSuggestion(Guid Id, Guid DestinationDefinitionId, string Mode, string? When,
    bool IsActive, IReadOnlyList<FormRecordCondition> Conditions, IReadOnlyList<FormSuggestionMapping> Mappings)
{
    public Guid DestinationVersionId { get; init; }
    public override string ToString() => "FormSuggestion { Content = [REDACTED] }";
}
public sealed record FormSuggestionInput(Guid Id, Guid DestinationDefinitionId, string? Mode, string? When,
    bool IsActive, IReadOnlyList<FormRecordCondition?>? Conditions, IReadOnlyList<FormSuggestionMapping?>? Mappings)
{
    public override string ToString() => "FormSuggestionInput { Content = [REDACTED] }";
}
public sealed record SaveFormSuggestionsRequest(long? ExpectedRevision, IReadOnlyList<FormSuggestionInput?>? Suggestions)
{
    public override string ToString() => "SaveFormSuggestionsRequest { Content = [REDACTED] }";
}
public sealed record FormSuggestionPublishedForm(Guid DefinitionId, Guid VersionId,
    IReadOnlyDictionary<string,string> Name, string OriginalCulture, IReadOnlyList<CanonicalFormField> Questions)
{
    public override string ToString() => "FormSuggestionPublishedForm { Content = [REDACTED] }";
}
public enum FormSuggestionDestinationState { Deactivated, Deleted }
public sealed record FormSuggestionUnavailableDestinationResponse(Guid DefinitionId, IReadOnlyDictionary<string, string> Name, FormSuggestionDestinationState State)
{
    public override string ToString() => "FormSuggestionUnavailableDestinationResponse { Content = [REDACTED] }";
}
public sealed record FormSuggestionOptionsResponse(FormSuggestionPublishedForm Source,
    IReadOnlyList<FormSuggestionPublishedForm> Destinations)
{
    public IReadOnlyList<FormSuggestionUnavailableDestinationResponse> UnavailableDestinations { get; init; } = [];
    public IReadOnlyDictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>> ConditionChoices { get; init; } = new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
    public override string ToString() => "FormSuggestionOptionsResponse { Content = [REDACTED] }";
}
public sealed record FormSuggestionSetResponse(Guid? Id, Guid DefinitionId, Guid? SourceVersionId, long Revision,
    IReadOnlyList<FormSuggestion> Suggestions, DateTime? CreatedAtUtc, FormAuthorResponse? Author)
{
    public override string ToString() => "FormSuggestionSetResponse { Content = [REDACTED] }";
}
public sealed record ListFormSuggestionRevisionsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["revision"];
}
public sealed record FormSuggestionChangesResponse(int AddedSuggestions, int UpdatedSuggestions,
    int RemovedSuggestions, bool OrderChanged);
public sealed record FormSuggestionRevisionResponse(Guid Id, long Revision, DateTime CreatedAtUtc,
    FormAuthorResponse? Author, FormSuggestionChangesResponse Changes);
public sealed record FormSuggestionSetStoredRow(Guid Id,Guid DefinitionId,Guid SourceVersionId,long Revision,string OriginalCulture,
    string SuggestionsJson,DateTime CreatedAtUtc,FormAuthorResponse? Author)
{
    public override string ToString() => "FormSuggestionSetStoredRow { Content = [REDACTED] }";
}
public sealed record FormSuggestionRevisionStoredRow(Guid Id,long Revision,DateTime CreatedAtUtc,FormAuthorResponse? Author,
    string SuggestionsJson,string? PreviousSuggestionsJson)
{
    public override string ToString() => "FormSuggestionRevisionStoredRow { Content = [REDACTED] }";
}
