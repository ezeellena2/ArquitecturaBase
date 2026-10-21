using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal sealed class WhatsAppFormChannelStateReader(ApplicationDbContext dbContext) : IStructuredFormChannelStateSource
{
    public async Task<IReadOnlyList<StructuredFormChannelStateRow>> ListAsync(IReadOnlyCollection<Guid> definitionIds,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definitionIds.Count == 0) return [];
        return await Rows(definitionIds, nowUtc).ToArrayAsync(cancellationToken);
    }
    private IQueryable<StructuredFormChannelStateRow> Rows(IReadOnlyCollection<Guid> definitionIds, DateTime nowUtc)
    {
        var pending = WhatsAppFlowSessionQuery.Pending(dbContext);
        var replies = WhatsAppFlowSessionQuery.WithPendingReplies(dbContext);
        var published = from binding in dbContext.Set<WhatsAppFlowBinding>().AsNoTracking()
            join operation in dbContext.Set<WhatsAppFlowOperation>().AsNoTracking() on binding.OperationId equals operation.Id
            where binding.State == WhatsAppFlowPublicationState.Published
            select new { operation.DefinitionId, binding.DeprecationState, binding.DeprecatedAtUtc };
        return pending.Select(session => session.DefinitionId).Union(published.Select(binding => binding.DefinitionId))
            .Where(definitionId => definitionIds.Contains(definitionId))
            .Select(definitionId => new StructuredFormChannelStateRow(definitionId,
                pending.Where(session => session.DefinitionId == definitionId && session.ExpiresAtUtc > nowUtc)
                    .Select(session => (DateTime?)session.ExpiresAtUtc).Max(),
                replies.Any(session => session.DefinitionId == definitionId))
            {
                DeprecationState = !published.Any(binding => binding.DefinitionId == definitionId) ? null
                    : published.Any(binding => binding.DefinitionId == definitionId && binding.DeprecationState == WhatsAppFlowDeprecationState.NeedsAttention)
                        ? StructuredFormDeprecationState.NeedsAttention
                    : published.Any(binding => binding.DefinitionId == definitionId && binding.DeprecationState == WhatsAppFlowDeprecationState.Uncertain)
                        ? StructuredFormDeprecationState.Uncertain
                    : published.Any(binding => binding.DefinitionId == definitionId && binding.DeprecationState != WhatsAppFlowDeprecationState.Deprecated)
                        ? StructuredFormDeprecationState.Pending : StructuredFormDeprecationState.Deprecated,
                DeprecatedAtUtc = published.Any(binding => binding.DefinitionId == definitionId)
                    && !published.Any(binding => binding.DefinitionId == definitionId && binding.DeprecationState != WhatsAppFlowDeprecationState.Deprecated)
                    ? published.Where(binding => binding.DefinitionId == definitionId).Select(binding => binding.DeprecatedAtUtc).Max() : null
            });
    }
}
