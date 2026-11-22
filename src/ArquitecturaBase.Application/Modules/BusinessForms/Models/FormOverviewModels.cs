using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record ListFormOverviewRequest : PagedRequest
{
    /// <summary>Every column with a datum (OT1-C, 2026-11-22). Without one, the list keeps its usual order: what awaits
    /// approval first, then the last response received and the name.</summary>
    public static readonly IReadOnlyCollection<string> SortableFields =
        ["name", "formState", "pendingApprovalCount", "responseCount", "lastResponseNumber", "lastSubmittedAtUtc", "lastResponseState"];
    public string State { get; init; } = "Active";
    public bool PendingOnly { get; init; }
}

public sealed record FormOverviewResponse(PagedResult<FormOverviewFormResponse> Forms, FormOverviewCounts FilterCounts);
public sealed record FormOverviewCounts(int Active, int Deactivated, int? DeactivatedPending);
public sealed record FormOverviewFormResponse(Guid Id, string Name, string Prefix, DateTime? DeactivatedAtUtc,
    int? PendingApprovalCount, long? ResponseCount, FormOverviewLastResponse? LastResponse);
public sealed record FormOverviewLastResponse(Guid Id, string Number, DateTime SubmittedAtUtc, string State);
