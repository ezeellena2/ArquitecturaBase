using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;

internal sealed class AiUsageReader(ApplicationDbContext dbContext) : IAiUsageReader
{
    public async Task<IReadOnlyList<AiUsageRow>> ListMonthAsync(DateOnly month, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var kept = await dbContext.Set<AiUsageMonth>().AsNoTracking().Where(x => x.Month == month)
            .Select(x => new AiUsageRow(x.Provider, x.Model, x.Requests, x.UnknownUsageRequests, x.InputTokens, x.OutputTokens, x.AudioSeconds))
            .ToListAsync(cancellationToken);
        // PR12: a turn counts once it reached the provider, also one of a retired conversation that the filters hide until it is deleted.
        // SUM gives no null in EF, so how many reported each amount says whether it is known.
        var alive = await dbContext.Set<AiAssistantTurn>().AsNoTracking().IgnoreQueryFilters()
            .Where(x => x.SubmittedAtUtc >= startUtc && x.SubmittedAtUtc < endUtc && x.ProviderSteps > 0)
            .GroupBy(x => new { x.Provider, x.Model })
            .Select(g => new
            {
                g.Key.Provider, g.Key.Model, Requests = g.LongCount(), Unknown = g.LongCount(x => x.UsageUnknown && x.AudioSeconds == null),
                Input = g.Sum(x => x.InputTokens ?? 0), InputKnown = g.Count(x => x.InputTokens != null),
                Output = g.Sum(x => x.OutputTokens ?? 0), OutputKnown = g.Count(x => x.OutputTokens != null),
                Audio = g.Sum(x => x.AudioSeconds ?? 0), AudioKnown = g.Count(x => x.AudioSeconds != null),
            })
            .ToListAsync(cancellationToken);
        // I4: a provider a newer version saved reads Unsupported; it is not shown until a version that knows it.
        return AiUsagePolicy.Combine(kept.Concat(alive.Where(x => AiProviders.IsSupported(x.Provider)).Select(x => new AiUsageRow(x.Provider,
            x.Model, x.Requests, x.Unknown, x.InputKnown > 0 ? x.Input : null, x.OutputKnown > 0 ? x.Output : null, x.AudioKnown > 0 ? x.Audio : null))));
    }

    public async Task<IReadOnlyList<AiModelPriceRow>> ListPricesAsync(CancellationToken cancellationToken) =>
        await dbContext.Set<AiModelPrice>().AsNoTracking()
            .Select(x => new AiModelPriceRow(x.Provider, x.Model, x.InputUsdPerMillion, x.OutputUsdPerMillion, x.AudioUsdPerMinute, x.Revision))
            .ToListAsync(cancellationToken);
}
