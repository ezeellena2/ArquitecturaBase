using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
public sealed record MissingFormTopicsQuery(int Page = 1,int PageSize = 20,string? Sort = null,string? Search = null,string? State = "Pending");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DiscardMissingFormTopicHttpRequest(long? ExpectedRevision);
