using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormMissingTopicMetadata(Guid Id, string Name, string Description, string OriginalCulture)
{ public override string ToString() => "FormMissingTopicMetadata { Content = [REDACTED] }"; }
public sealed record MissingFormRequestProposalPayload(string State, int ReadOrdinal, Guid? ExistingTopicId, string? Name, string? Description) : AssistantCapabilityPayload;
internal sealed record FormMissingRequestArguments(Guid? ExistingTopicId, string? Name, string? Description)
{ public override string ToString() => "FormMissingRequestArguments { Content = [REDACTED] }"; }
public sealed record ListMissingFormTopicsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["lastRequestedAtUtc"];
    public string? State { get; init; } = "Pending";
}
public sealed record DiscardMissingFormTopicRequest(long? ExpectedRevision);
public sealed record MissingFormTopicResponse(Guid Id, long Revision, string Name, string Description, string OriginalCulture,
    int RequestCount, int PersonCount, DateTime LastRequestedAtUtc, string State, FormAuthorResponse? DiscardedBy, DateTime? DiscardedAtUtc)
{ public override string ToString() => "MissingFormTopicResponse { Content = [REDACTED] }"; }
public sealed record MissingFormTopicStateCounts(int Pending, int Discarded);
public sealed record MissingFormTopicsPageResponse(IReadOnlyList<MissingFormTopicResponse> Items, int Page, int PageSize,
    int TotalCount, int TotalRequests, MissingFormTopicStateCounts StateCounts)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
