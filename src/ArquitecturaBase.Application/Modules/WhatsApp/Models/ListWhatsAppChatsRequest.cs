using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record ListWhatsAppChatsRequest : PagedRequest
{
    public static IReadOnlyCollection<string> SortableFields { get; } = ["lastInboundAtUtc", "profileName"];
    public bool UnreadOnly { get; init; }
    /// <summary>Derivar (AS4): only the conversations that need a person.</summary>
    public bool NeedsPersonOnly { get; init; }
}
