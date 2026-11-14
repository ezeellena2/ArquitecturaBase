using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppMessageReader(ApplicationDbContext dbContext) : IWhatsAppMessageReader
{
    /// <summary>
    /// Va por el índice único del id de Meta. Los mensajes no tienen borrado lógico: no hay filtro global.
    /// </summary>
    public Task<WhatsAppMessageStatus?> FindOutboundStatusAsync(string waMessageId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppMessage>()
            .AsNoTracking()
            .Where(message => message.Direction == WhatsAppMessageDirection.Outbound && message.WaMessageId == waMessageId)
            .Select(message => message.Status)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Por el índice de contacto y secuencia entrante.</summary>
    public Task<string?> FindLatestInboundTraceAsync(Guid contactId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppMessage>()
            .AsNoTracking()
            .Where(message => message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound)
            .OrderByDescending(message => message.InboundSequence)
            .Select(message => message.TraceParent)
            .FirstOrDefaultAsync(cancellationToken);
}
