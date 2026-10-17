using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;

/// <summary>Initial pilot limits remain explicit configuration, rather than inferred model capabilities.</summary>
public sealed class AiAssistantOptions
{
    public AiAssistantLimits Limits { get; set; } = new();
    public AiAssistantModelProfile[] ModelProfiles { get; set; } = [];
    public bool ProcessInBackground { get; set; } = true;
    public int WorkerPollingSeconds { get; set; } = 1;
    public int MaintenanceIntervalMinutes { get; set; } = 60;
    public int CompletionPersistenceSeconds { get; set; } = 5;

    public bool IsValid()
    {
        var limits = Limits;
        return limits.QuestionCharacters is > 0 and <= 4000
            && limits.HistoryTurns is > 0 and <= 12 && limits.HistoryCharacters is > 0 and <= 24000
            && limits.ContextBytes is > 0 and <= 131072
            && limits.ProviderSteps is > 0 and <= 4 && limits.ToolCalls is > 0 and <= 6
            && limits.ToolResultBytes is > 0 and <= 16384 && limits.ToolAggregateBytes >= limits.ToolResultBytes
            && limits.ToolAggregateBytes <= 65536 && limits.OutputTokens is > 0 and <= 1500
            && limits.ProviderResponseBytes is > 0 and <= 262144 && limits.ContinuationBytes is > 0 and <= 131072
            && limits.ToolTimeoutSeconds is > 0 and <= 5 && limits.ProviderTimeoutSeconds is > 0 and <= 30
            && limits.RunningSeconds is > 0 and <= 90 && limits.PendingSeconds is > 0 and <= 120
            && limits.LeaseGraceSeconds is > 0 and <= 15
            && limits.PendingCapacity is > 0 and <= 50 && limits.RunningCapacity is > 0 and <= 4
            && limits.ActorDailyQuota > 0 && limits.InstallationDailyQuota >= limits.ActorDailyQuota
            && limits.RetentionDays is > 0 and <= 30 && limits.MaintenanceBatchSize is > 0 and <= 100
            && limits.PollingAfterSeconds is > 0 and <= 30
            && WorkerPollingSeconds is > 0 and <= 60 && MaintenanceIntervalMinutes is > 0 and <= 60
            && CompletionPersistenceSeconds is > 0 and <= 5
            && ModelProfiles.All(profile => Enum.IsDefined(profile.Provider)
                && !string.IsNullOrWhiteSpace(profile.Id) && profile.Id.Length <= 80 && profile.Version > 0
                && !string.IsNullOrWhiteSpace(profile.Model) && profile.Model.Length <= 150
                && profile.ContextBytes >= limits.ContextBytes && profile.SupportsTools && profile.SupportsTerminalAnswer)
            && ModelProfiles.Select(profile => (profile.Provider, profile.Model, profile.Id, profile.Version)).Distinct().Count() == ModelProfiles.Length;
    }
}
