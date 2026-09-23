using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Persistence.Repositories;

internal sealed class WhatsAppMessageRepository(ApplicationDbContext dbContext) : IWhatsAppMessageRepository
{
    private const string LockPrefix = "whatsapp-message:";

    public Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync(waMessageIds.Select(id => LockPrefix + id), cancellationToken);

    public async Task<IReadOnlySet<string>> ListExistingIdsAsync(
        IReadOnlyCollection<string> waMessageIds,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.WhatsAppMessages
            .Where(message => waMessageIds.Contains(message.WaMessageId))
            .Select(message => message.WaMessageId)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(
        IReadOnlyCollection<string> waMessageIds,
        CancellationToken cancellationToken) =>
        await dbContext.WhatsAppMessages
            .Where(message => message.Direction == WhatsAppMessageDirection.Outbound && waMessageIds.Contains(message.WaMessageId))
            .ToListAsync(cancellationToken);

    public void Add(WhatsAppMessage message) => dbContext.WhatsAppMessages.Add(message);
}
