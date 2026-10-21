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
        var pending = dbContext.Set<WhatsAppFlowSession>().AsNoTracking()
            .Where(session => session.CaptureState == WhatsAppFlowCaptureState.Pending);
        return pending.Where(session => definitionIds.Contains(session.DefinitionId))
            .Select(session => session.DefinitionId).Distinct()
            .Select(definitionId => new StructuredFormChannelStateRow(definitionId,
                pending.Where(session => session.DefinitionId == definitionId && session.ExpiresAtUtc > nowUtc)
                    .Select(session => (DateTime?)session.ExpiresAtUtc).Max(),
                pending.Where(session => session.DefinitionId == definitionId).Any(session =>
                    dbContext.Set<WhatsAppMessage>().AsNoTracking().Any(message =>
                        message.Direction == WhatsAppMessageDirection.Inbound && message.Kind == WhatsAppMessageKind.FlowReply
                        && message.ProcessedAtUtc == null && message.ProtectedFlowResponse != null
                        && (message.FlowSessionId == session.Id
                            || message.FlowSessionId == null && message.ContactId == session.ContactId
                                && message.OccurredAtUtc >= session.CreatedAtUtc && message.OccurredAtUtc < session.ExpiresAtUtc)))));
    }
}
