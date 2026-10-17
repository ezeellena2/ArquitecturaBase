using ArquitecturaBase.Application.Common.Pagination;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;
public sealed record FormRule(Guid Id, string Event, IReadOnlyList<FormRecordCondition> Conditions, IReadOnlyList<FormRuleAction> Actions)
{ public override string ToString() => "FormRule { Content = [REDACTED] }"; }
public sealed record FormRuleAction(Guid Id, string Kind, Guid? RoleId, IReadOnlyDictionary<string,string>? Text, bool IncludePdf)
{ public override string ToString() => "FormRuleAction { Content = [REDACTED] }"; }
public sealed record FormRuleInput(Guid Id, string? Event, IReadOnlyList<FormRecordCondition?>? Conditions, IReadOnlyList<FormRuleActionInput?>? Actions)
{ public override string ToString() => "FormRuleInput { Content = [REDACTED] }"; }
public sealed record FormRuleActionInput(Guid Id, string? Kind, Guid? RoleId = null, IReadOnlyDictionary<string,string?>? Text = null, bool IncludePdf = false)
{ public override string ToString() => "FormRuleActionInput { Content = [REDACTED] }"; }
public sealed record SaveFormRulesRequest(long? ExpectedRevision, IReadOnlyList<FormRuleInput?>? Rules)
{ public override string ToString() => "SaveFormRulesRequest { Content = [REDACTED] }"; }
public sealed record FormRuleSetResponse(Guid? Id, Guid DefinitionId, long Revision, string OriginalCulture, IReadOnlyList<FormRule> Rules, DateTime? CreatedAtUtc, FormAuthorResponse? Author)
{ public override string ToString() => "FormRuleSetResponse { Content = [REDACTED] }"; }
public sealed record FormRuleRevisionResponse(Guid Id, long Revision, DateTime CreatedAtUtc, FormAuthorResponse? Author, bool HasApproval, FormRuleChangesResponse Changes)
{ public override string ToString() => "FormRuleRevisionResponse { Content = [REDACTED] }"; }
public sealed record FormRuleRoleResponse(Guid Id, string Name);
public sealed record FormRuleOptionsResponse(Guid DefinitionId, IReadOnlyDictionary<string,string> Name, string OriginalCulture, IReadOnlyList<CanonicalFormField> Questions, IReadOnlyList<FormRuleRoleResponse> Roles)
{
    public IReadOnlyDictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>> ConditionChoices { get; init; } = new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
    public override string ToString() => "FormRuleOptionsResponse { Content = [REDACTED] }";
}
public sealed record FormRuleConditionChoiceResponse(Guid Id,string Label);
public sealed record ListFormRuleRevisionsRequest : PagedRequest
{ public static readonly IReadOnlyCollection<string> SortableFields = ["revision"]; }
public sealed record FormRuleDefinitionRow(Guid Id, string NameJson, string OriginalCulture, string SchemaJson, IReadOnlyList<FormRuleSchemaRow> HistoricalSchemas)
{ public override string ToString() => "FormRuleDefinitionRow { Content = [REDACTED] }"; }
public sealed record FormRuleSchemaRow(long Number, string SchemaJson);
public sealed record FormRuleSetStoredRow(Guid Id, Guid DefinitionId, long Revision, string OriginalCulture, string RulesJson, DateTime CreatedAtUtc, FormAuthorResponse? Author)
{ public override string ToString() => "FormRuleSetStoredRow { Content = [REDACTED] }"; }

public sealed record FormRuleChangesResponse(int AddedRules, int UpdatedRules, int RemovedRules, bool OrderChanged);
public sealed record FormRuleRevisionStoredRow(Guid Id, long Revision, DateTime CreatedAtUtc, FormAuthorResponse? Author, bool HasApproval, string RulesJson, string? PreviousRulesJson)
{ public override string ToString() => "FormRuleRevisionStoredRow { Content = [REDACTED] }"; }
