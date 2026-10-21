using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record DecideFormApprovalHttpRequest(long? ExpectedRevision, string? Decision, string? Reason = null)
{ public override string ToString() => "DecideFormApprovalHttpRequest { Content = [REDACTED] }"; }
public sealed record FormApprovalsQuery(int Page = 1, int PageSize = 20, Guid? DefinitionId = null, bool OnlyMine = true);
public sealed record FormApprovalFilterQuery(Guid? DefinitionId = null, bool OnlyMine = true);
