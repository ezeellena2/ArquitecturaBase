using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

/// <summary>H9: what waits in the durable outgoing, by the index on its state; three small queries once a minute.</summary>
internal sealed class WhatsAppOutgoingWorkloadReader(ApplicationDbContext db) : IWhatsAppOutgoingWorkloadReader
{
    public async Task<WhatsAppOutgoingWorkloadRow> FindAsync(CancellationToken cancellationToken)
    {
        var outgoing = db.Set<WhatsAppOutgoingMessage>().AsNoTracking();
        var pending = await outgoing.CountAsync(item => item.State == WhatsAppOutgoingMessageState.Pending, cancellationToken);
        var uncertain = await outgoing.CountAsync(item => item.State == WhatsAppOutgoingMessageState.Uncertain, cancellationToken);
        var oldest = await outgoing.Where(item => item.State == WhatsAppOutgoingMessageState.Pending)
            .MinAsync(item => (DateTime?)item.RequestedAtUtc, cancellationToken);
        return new(pending, uncertain, oldest);
    }
}
