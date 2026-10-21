namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>Local channel state. Pending also applies to active forms and does not authorize retirement.</summary>
public enum StructuredFormDeprecationState { Pending, Uncertain, Deprecated, NeedsAttention }

public sealed record StructuredFormChannelStateRow(Guid DefinitionId, DateTime? LastOpenSessionExpiresAtUtc, bool HasPendingReplies)
{
    public StructuredFormDeprecationState? DeprecationState { get; init; }
    public DateTime? DeprecatedAtUtc { get; init; }
}
