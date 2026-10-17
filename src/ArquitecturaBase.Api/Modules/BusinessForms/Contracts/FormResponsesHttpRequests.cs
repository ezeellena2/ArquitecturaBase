using ArquitecturaBase.Application.Common.Pagination;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
public sealed record FormNavigationQuery(int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null)
{ public override string ToString() => "FormNavigationQuery { Content = [REDACTED] }"; }
public sealed record FormRecordsQuery(int Page = PagedRequest.DefaultPage, int PageSize = PagedRequest.DefaultPageSize, string? Sort = null,
    Guid? SortFieldId = null, IReadOnlyList<Guid>? VersionIds = null, string? State = null);
public sealed record FormRecordsSearchHttpRequest(int Page = PagedRequest.DefaultPage, int PageSize = PagedRequest.DefaultPageSize, string? Sort = null,
    Guid? SortFieldId = null, IReadOnlyList<Guid>? VersionIds = null, IReadOnlyList<Guid>? ActorIds = null,
    DateTime? ReceivedFromUtc = null, DateTime? ReceivedToUtc = null, string? Search = null, IReadOnlyList<FormRecordConditionHttpRequest>? Conditions = null, string? State = null)
{ public override string ToString() => "FormRecordsSearchHttpRequest { Content = [REDACTED] }"; }
public sealed record FormRecordConditionHttpRequest(Guid FieldId, string Operator, IReadOnlyList<string>? Values)
{ public override string ToString() => "FormRecordConditionHttpRequest { Values = [REDACTED] }"; }
public sealed record FormRecordFacetHttpRequest(FormRecordsSearchHttpRequest Filter, string Dimension, Guid? FieldId = null,
    int Page = 1, int PageSize = 20, string? Sort = null, string? Search = null)
{ public override string ToString() => "FormRecordFacetHttpRequest { Content = [REDACTED] }"; }

public sealed record FormExportHttpRequest(FormRecordsSearchHttpRequest Filter, IReadOnlyList<Guid>? Columns)
{ public override string ToString() => "FormExportHttpRequest { Content = [REDACTED] }"; }
