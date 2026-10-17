using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFormSuggestionsHttpRequest(long? ExpectedRevision,IReadOnlyList<FormSuggestionHttpRequest?>? Suggestions)
{public override string ToString()=>"SaveFormSuggestionsHttpRequest { Content = [REDACTED] }";}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSuggestionHttpRequest(Guid Id,Guid DestinationDefinitionId,string? Mode,string? When,bool IsActive,
    IReadOnlyList<FormRecordConditionHttpRequest?>? Conditions,IReadOnlyList<FormSuggestionMappingHttpRequest?>? Mappings)
{public override string ToString()=>"FormSuggestionHttpRequest { Content = [REDACTED] }";}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSuggestionMappingHttpRequest(FormSuggestionMappingSourceHttpRequest? Source,Guid DestinationFieldId)
{public override string ToString()=>"FormSuggestionMappingHttpRequest { Content = [REDACTED] }";}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormSuggestionMappingSourceHttpRequest(string Kind,Guid? FieldId=null)
{public override string ToString()=>"FormSuggestionMappingSourceHttpRequest { Content = [REDACTED] }";}
public sealed record FormSuggestionRevisionsQuery(int Page=1,int PageSize=20,string? Sort=null);
