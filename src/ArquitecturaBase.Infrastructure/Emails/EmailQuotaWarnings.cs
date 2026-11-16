using ArquitecturaBase.Application.Interfaces.Integrations.Emails;

namespace ArquitecturaBase.Infrastructure.Emails;

/// <summary>
/// N7: one warning of each kind a day, in the memory of this copy (a singleton). Each one keeps the moment from which it can
/// warn again, a day after the last one; two requests at once let only one through.
/// </summary>
internal sealed class EmailQuotaWarnings : IEmailQuotaWarnings
{
    private long _nearAgainAtTicks;
    private long _reachedAgainAtTicks;

    public bool TryWarnNear(DateTime nowUtc) => TryTake(ref _nearAgainAtTicks, nowUtc);

    public bool TryWarnReached(DateTime nowUtc) => TryTake(ref _reachedAgainAtTicks, nowUtc);

    private static bool TryTake(ref long againAtTicks, DateTime nowUtc)
    {
        var againAt = Interlocked.Read(ref againAtTicks);

        return nowUtc.Ticks >= againAt
            && Interlocked.CompareExchange(ref againAtTicks, nowUtc.Ticks + TimeSpan.TicksPerDay, againAt) == againAt;
    }
}
