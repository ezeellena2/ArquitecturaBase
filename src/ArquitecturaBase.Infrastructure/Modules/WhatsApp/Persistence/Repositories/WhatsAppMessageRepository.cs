using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppMessageRepository(ApplicationDbContext dbContext) : IWhatsAppMessageRepository
{
    public Task LockAsync(IReadOnlyCollection<string> waMessageIds, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync(waMessageIds.Select(WhatsAppLockKeys.WhatsAppMessage), cancellationToken);

    public async Task<IReadOnlySet<string>> ListExistingIdsAsync(
        IReadOnlyCollection<string> waMessageIds,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.Set<WhatsAppMessage>()
            .Where(message => waMessageIds.Contains(message.WaMessageId))
            .Select(message => message.WaMessageId)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<WhatsAppMessage>> ListOutboundAsync(
        IReadOnlyCollection<string> waMessageIds,
        CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppMessage>()
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

    public Task<WhatsAppMessage?> GetInboundAsync(string waMessageId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppMessage>().SingleOrDefaultAsync(message => message.WaMessageId == waMessageId
            && message.Direction == WhatsAppMessageDirection.Inbound, cancellationToken);

    public async Task<IReadOnlyList<WhatsAppMessage>> ListLocationAnswersAsync(Guid contactId, DateTime sinceUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppMessage>()
            .Where(LocationAnswer(contactId, sinceUtc))
            .OrderBy(message => message.InboundSequence).ThenBy(message => message.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

    /// <summary>W1-A: an inbound location or «Seguir sin ubicación» tap of the contact that has not resolved a send yet; W3 adds the
    /// contacts no send took.</summary>
    internal static System.Linq.Expressions.Expression<Func<WhatsAppMessage, bool>> LocationAnswer(Guid contactId, DateTime sinceUtc) => message =>
        message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound && message.FlowSessionId == null
        && message.OccurredAtUtc >= sinceUtc
        && (message.Kind == WhatsAppMessageKind.Location || message.Kind == WhatsAppMessageKind.Contact
            || message.Kind == WhatsAppMessageKind.ButtonReply && message.ReplyId != null && message.ReplyId.StartsWith(WhatsAppFlowLocationReply.Prefix));

    public void Add(WhatsAppMessage message) => dbContext.Set<WhatsAppMessage>().Add(message);
    public Task<WhatsAppDeliveryReceipt?> GetReceiptAsync(string waMessageId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppDeliveryReceipt>().SingleOrDefaultAsync(item => item.WaMessageId == waMessageId, cancellationToken);
    public void Add(WhatsAppDeliveryReceipt receipt) => dbContext.Set<WhatsAppDeliveryReceipt>().Add(receipt);

    private IQueryable<WhatsAppMessage> PendingInbound() =>
        dbContext.Set<WhatsAppMessage>().Where(message =>
            message.Direction == WhatsAppMessageDirection.Inbound && message.Kind != WhatsAppMessageKind.FlowReply
            && message.ProcessedAtUtc == null && message.ContactId != null);
}
