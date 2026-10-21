namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredFormDeprecationState { Pending, Uncertain, Deprecated }

public sealed record StructuredFormChannelStateRow(Guid DefinitionId, DateTime? LastOpenSessionExpiresAtUtc, bool HasPendingReplies)
{
    public StructuredFormDeprecationState? DeprecationState { get; init; }
    public DateTime? DeprecatedAtUtc { get; init; }
}
