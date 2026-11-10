using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
public sealed record ListFormDefinitionsQuery(int Page = PagedRequest.DefaultPage, int PageSize = PagedRequest.DefaultPageSize, string? Search = null, string? Sort = null, string PublicationFilter = "Active",
    Guid? FolderId = null, bool WithoutFolder = false)
{ public override string ToString() => "ListFormDefinitionsQuery { Content = [REDACTED] }"; }
