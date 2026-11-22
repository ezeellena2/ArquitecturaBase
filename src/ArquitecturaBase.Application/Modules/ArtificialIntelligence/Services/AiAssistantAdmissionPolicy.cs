using ArquitecturaBase.Application.Common.Time;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

/// <summary>
/// Whether a turn is admitted now: the admission locks (the connection's and the budget's, in that order) and the refusals by quota
/// and capacity. Readings count per person across every channel and per installation, apart from the quotas of interpretations and
/// audio. The day of the quotas is the business's, in the time zone of the settings, and a quota renews at its midnight (RC5,
/// 2026-11-22). It never saves.
/// </summary>
internal sealed class AiAssistantAdmissionPolicy(IAiConnectionRepository connections, IAiAssistantRepository repository, AiAssistantLimits limits,
    ISystemSettingsReader settings)
{
    public async Task LockAsync(CancellationToken ct)
    {
        await connections.LockAsync(ct);
        await repository.LockBudgetAsync(ct);
    }

    public async Task<Error?> RefusalAsync(Guid userId, string channelId, bool normalizes, bool reads, DateTime now, CancellationToken ct)
    {
        var day = BusinessDay.Of(now, BusinessDay.Zone((await settings.FindPresentationAsync(ct)).DefaultTimeZoneId));
        var budget = await repository.CountBudgetAsync(userId, day.StartUtc, day.EndUtc, channelId, normalizes, reads, ct);
        var (actorQuota, installationQuota) = reads ? (limits.DocumentActorDailyQuota, limits.DocumentInstallationDailyQuota)
            : (limits.ActorDailyQuota, limits.InstallationDailyQuota);
        if (budget.ActorAccepted >= actorQuota || budget.InstallationAccepted >= installationQuota)
            return AiAssistantErrors.QuotaExceeded((int)Math.Ceiling((day.EndUtc - now).TotalSeconds));
        return budget.Pending >= limits.PendingCapacity ? AiAssistantErrors.CapacityLimited(limits.PollingAfterSeconds) : null;
    }
}
