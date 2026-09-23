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

    // Las dos consultas de los pendientes van por el índice filtrado de WhatsAppMessageConfiguration, que tiene solo los
    // entrantes sin procesar: por más historial que se junte, el procesador mira una tabla chica.
    public async Task<IReadOnlyList<Guid>> ListContactsWithPendingInboundAsync(
        IReadOnlyCollection<Guid> excluded,
        int limit,
        CancellationToken cancellationToken) =>
        await PendingInbound()
            .Where(message => !excluded.Contains(message.ContactId!.Value))
            .GroupBy(message => message.ContactId!.Value)
            .Select(group => new { ContactId = group.Key, OldestAtUtc = group.Min(message => message.OccurredAtUtc) })
            .OrderBy(contact => contact.OldestAtUtc)
            .ThenBy(contact => contact.ContactId)
            .Take(limit)
            .Select(contact => contact.ContactId)
            .ToListAsync(cancellationToken);

    // El Id desempata dos del mismo segundo: los timestamps de Meta van en segundos.
    public async Task<IReadOnlyList<WhatsAppMessage>> ListPendingInboundAsync(Guid contactId, CancellationToken cancellationToken) =>
        await PendingInbound()
            .Where(message => message.ContactId == contactId)
            .OrderBy(message => message.OccurredAtUtc)
            .ThenBy(message => message.Id)
            .ToListAsync(cancellationToken);

    public void Add(WhatsAppMessage message) => dbContext.WhatsAppMessages.Add(message);

    private IQueryable<WhatsAppMessage> PendingInbound() =>
        dbContext.WhatsAppMessages.Where(message =>
            message.Direction == WhatsAppMessageDirection.Inbound && message.ProcessedAtUtc == null && message.ContactId != null);
}
