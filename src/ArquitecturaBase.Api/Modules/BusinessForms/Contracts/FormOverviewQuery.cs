using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormOverviewQuery
{
    public int Page { get; init; } = PagedRequest.DefaultPage;
    public int PageSize { get; init; } = PagedRequest.DefaultPageSize;
    public string? Search { get; init; }
    public string? Sort { get; init; }
    public string State { get; init; } = "Active";
    public bool PendingOnly { get; init; }
    // CP1-A: a folder with its subfolders, or the forms without a folder.
    public Guid? FolderId { get; init; }
    public bool WithoutFolder { get; init; }
    public override string ToString() => "FormOverviewQuery { Content = [REDACTED] }";
}