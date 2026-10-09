namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

public sealed record AiAssistantLimits
{
    public int QuestionCharacters { get; init; } = 4000;
    public int HistoryTurns { get; init; } = 12;
    public int HistoryCharacters { get; init; } = 24000;
    public int ContextBytes { get; init; } = 131072;
    public int ProviderSteps { get; init; } = 4;
    public int ToolCalls { get; init; } = 6;
    public int ToolResultBytes { get; init; } = 16384;
    public int ToolAggregateBytes { get; init; } = 65536;
    public int OutputTokens { get; init; } = 1500;
    public int ProviderResponseBytes { get; init; } = 262144;
    public int ContinuationBytes { get; init; } = 131072;
    public int ToolTimeoutSeconds { get; init; } = 5;
    public int ProviderTimeoutSeconds { get; init; } = 30;
    public int RunningSeconds { get; init; } = 90;
    public int PendingSeconds { get; init; } = 120;
    public int LeaseGraceSeconds { get; init; } = 15;
    public int PendingCapacity { get; init; } = 50;
    public int RunningCapacity { get; init; } = 4;
    public int ActorDailyQuota { get; init; } = 10;
    public int InstallationDailyQuota { get; init; } = 100;
    public int RetentionDays { get; init; } = 30;
    public int MaintenanceBatchSize { get; init; } = 100;
    public int PollingAfterSeconds { get; init; } = 2;
}
