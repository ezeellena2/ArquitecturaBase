namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

public sealed record ListAiConversationsQuery(int? Page, int? PageSize, string? Sort);
