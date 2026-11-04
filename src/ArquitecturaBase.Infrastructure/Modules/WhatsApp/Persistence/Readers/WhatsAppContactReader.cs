using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppContactReader(ApplicationDbContext dbContext) : IWhatsAppContactReader
{
    public async Task<WhatsAppProfileChatRow> FindProfileChatAsync(Guid userId, string waId, CancellationToken cancellationToken)
    {
        var contacts = dbContext.Set<WhatsAppContact>().AsNoTracking();
        // Una cuenta tiene a lo sumo un contacto (índice único sobre UserId).
        var linked = await contacts.Where(contact => contact.UserId == userId)
            .Select(contact => new { contact.WaId, contact.LastInboundAtUtc })
            .SingleOrDefaultAsync(cancellationToken);
        var numberHasChat = await contacts.AnyAsync(contact => contact.WaId == waId, cancellationToken);

        return new WhatsAppProfileChatRow(linked?.WaId, linked?.LastInboundAtUtc, numberHasChat);
    }
}
