using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppFormActionReader(ApplicationDbContext db) : IWhatsAppFormActionReader
{
    public async Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests, CancellationToken cancellationToken)
    {
        if (requests.Count == 0) return [];
        var requested = requests.ToHashSet(); var runs = requested.Select(request => request.RunId).Distinct().ToArray(); var users = requested.Select(request => request.RecipientUserId).Distinct().ToArray();
        var rows = await QueryObservations(runs, users).ToArrayAsync(cancellationToken);
        return rows.Where(row => requested.Contains(new(row.RunId, row.RecipientUserId))).ToArray();
    }
    private IQueryable<WhatsAppFormActionObservationRow> QueryObservations(IReadOnlyCollection<Guid> runIds, IReadOnlyCollection<Guid> userIds) =>
        from outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
        where outgoing.Origin == WhatsAppMessageOrigin.FormAction && runIds.Contains(outgoing.RequestId)
            && outgoing.RecipientUserId != null && userIds.Contains(outgoing.RecipientUserId.Value)
        join receipt in db.Set<WhatsAppDeliveryReceipt>().AsNoTracking() on outgoing.WaMessageId equals receipt.WaMessageId into receipts
        from receipt in receipts.DefaultIfEmpty()
        select new WhatsAppFormActionObservationRow(outgoing.RequestId, outgoing.RecipientUserId!.Value, outgoing.State, outgoing.FailureCode,
            !string.IsNullOrWhiteSpace(outgoing.Body), outgoing.LastFormActionControlRevision, outgoing.DeliveryReviewWasSent, outgoing.DeliveryReviewedByUserId, outgoing.DeliveryReviewedAtUtc,
            receipt == null ? null : receipt.DeliveredAtUtc ?? (receipt.Status == WhatsAppMessageStatus.Delivered ? (DateTime?)receipt.OccurredAtUtc : null),
            receipt == null ? null : receipt.ReadAtUtc ?? (receipt.Status == WhatsAppMessageStatus.Read ? (DateTime?)receipt.OccurredAtUtc : null));
}
