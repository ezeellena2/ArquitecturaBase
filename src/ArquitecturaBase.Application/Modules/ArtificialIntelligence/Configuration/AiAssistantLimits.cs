namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

public sealed record AiAssistantLimits
{
    /// <summary>The longest reading a document may have: the default of <see cref="MaximumReadingCharacters"/>, which may only lower it.</summary>
    public const int ReadingCharactersCeiling = 20_000;
    /// <summary>The most output tokens a reading may ask for: the default of <see cref="DocumentOutputTokens"/>, which may only lower it.</summary>
    public const int DocumentOutputTokensCeiling = 8_192;

    public int QuestionCharacters { get; init; } = 4000;
    public int HistoryTurns { get; init; } = 12;
    public int HistoryCharacters { get; init; } = 24000;
    public int ContextBytes { get; init; } = 131072;
    // «Armar formulario con la IA» (forms-documents) may read catalogs, forms and standards one per step: three reads, the proposal and the closing step.
    public int ProviderSteps { get; init; } = 5;
    public int ToolCalls { get; init; } = 6;
    public int ToolResultBytes { get; init; } = 16384;
    public int ToolAggregateBytes { get; init; } = 65536;
    // A whole proposed form travels as the arguments of one call.
    public int OutputTokens { get; init; } = 4096;
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
    // Reading a document (ReadDocument) has its own quotas, outside the installation's: per person across every channel, and in all.
    public int DocumentActorDailyQuota { get; init; } = 10;
    public int DocumentInstallationDailyQuota { get; init; } = 50;
    // A reading takes longer than a step of the conversation, always within the turn's deadline (RunningSeconds).
    public int DocumentTimeoutSeconds { get; init; } = 60;
    // The conversation's OutputTokens include the reasoning and would cut a reading.
    public int DocumentOutputTokens { get; init; } = DocumentOutputTokensCeiling;
    // The model does not count characters with certainty: a longer reading is invalid, never cut by the engine.
    public int MaximumReadingCharacters { get; init; } = ReadingCharactersCeiling;
}
