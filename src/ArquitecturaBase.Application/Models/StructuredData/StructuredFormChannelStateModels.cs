namespace ArquitecturaBase.Application.Models.StructuredData;

public sealed record StructuredFormChannelStateRow(Guid DefinitionId, DateTime? LastOpenSessionExpiresAtUtc, bool HasPendingReplies);
