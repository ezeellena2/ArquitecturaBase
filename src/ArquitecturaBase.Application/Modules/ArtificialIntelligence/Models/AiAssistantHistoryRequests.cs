using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record ListAiConversationsRequest : PagedRequest
{
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lastActivityAtUtc", "id" };
}
public sealed record ListAiAssistantTurnsRequest : PagedRequest
{
    public static IReadOnlySet<string> SortableFields { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ordinal", "id" };
}
public sealed record CancelAiAssistantTurnRequest(Guid RequestId);
