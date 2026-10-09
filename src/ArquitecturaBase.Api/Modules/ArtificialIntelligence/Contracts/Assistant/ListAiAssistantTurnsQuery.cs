namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts.Assistant;

public sealed record ListAiAssistantTurnsQuery(int? Page, int? PageSize, string? Sort);
