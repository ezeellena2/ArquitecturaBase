using System.Text.Json.Serialization;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SaveFormRulesHttpRequest(long? ExpectedRevision, IReadOnlyList<FormRuleHttpRequest?>? Rules)
{ public override string ToString() => "SaveFormRulesHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRuleHttpRequest(Guid Id, string? Event, IReadOnlyList<FormRuleConditionHttpRequest?>? Conditions, IReadOnlyList<FormRuleActionHttpRequest?>? Actions)
{ public override string ToString() => "FormRuleHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRuleActionHttpRequest(Guid Id, string? Kind, Guid? RoleId = null, IReadOnlyDictionary<string,string?>? Text = null, bool IncludePdf = false)
{ public override string ToString() => "FormRuleActionHttpRequest { Content = [REDACTED] }"; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormRuleConditionHttpRequest(Guid FieldId, string? Operator, IReadOnlyList<string>? Values)
{ public override string ToString() => "FormRuleConditionHttpRequest { Values = [REDACTED] }"; }
public sealed record FormRuleRevisionsQuery(int Page = 1, int PageSize = 20, string? Sort = null);
