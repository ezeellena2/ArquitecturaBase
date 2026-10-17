using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal sealed class WhatsAppRecordOriginReader(ApplicationDbContext dbContext, IPhoneNumberParser phones) : IWhatsAppRecordOriginReader
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds, bool canOpenConversation, CancellationToken cancellationToken)
    {
        var rows = await (from session in dbContext.Set<WhatsAppFlowSession>().AsNoTracking()
                    where sourceReceiptIds.Contains(session.Id)
                    join outgoing in dbContext.Set<WhatsAppOutgoingMessage>() on (Guid?)session.Id equals outgoing.FlowSessionId
                    join contact in dbContext.Set<WhatsAppContact>() on session.ContactId equals contact.Id
                    select new { session.Id, session.OperatorUserId, contact.WaId, session.ContactId, outgoing.RequestedAtUtc,
                        Sender = dbContext.Users.Where(user => user.Id == session.OperatorUserId).Select(user => user.DisplayName).FirstOrDefault() })
            .ToListAsync(cancellationToken);
        return rows.Select(row => {
            var phone = phones.Parse(null, row.WaId is null ? null : "+" + row.WaId);
            return new StructuredRecordOrigin(row.Id, "phone", row.OperatorUserId, row.Sender, row.RequestedAtUtc,
                canOpenConversation ? row.ContactId : null, phone.IsSuccess ? phones.Mask(phone.Value) : null, "Manual");
        }).ToArray();
    }
}
