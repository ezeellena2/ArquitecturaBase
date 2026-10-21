using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal static class WhatsAppFlowSessionQuery
{
    internal static IQueryable<WhatsAppFlowSession> Pending(ApplicationDbContext db) => db.Set<WhatsAppFlowSession>().AsNoTracking()
        .Where(session => session.CaptureState == WhatsAppFlowCaptureState.Pending);
    internal static IQueryable<WhatsAppFlowSession> WithPendingReplies(ApplicationDbContext db) => Pending(db).Where(session =>
        db.Set<WhatsAppMessage>().AsNoTracking().Any(message => message.Direction == WhatsAppMessageDirection.Inbound
            && message.Kind == WhatsAppMessageKind.FlowReply && message.ProcessedAtUtc == null && message.ProtectedFlowResponse != null
            && (message.FlowSessionId == session.Id || message.FlowSessionId == null && message.ContactId == session.ContactId
                && message.OccurredAtUtc >= session.CreatedAtUtc && message.OccurredAtUtc < session.ExpiresAtUtc)));
    internal static IQueryable<WhatsAppFlowSession> OpenWork(ApplicationDbContext db, DateTime nowUtc) => Pending(db)
        .Where(session => session.ExpiresAtUtc > nowUtc || WithPendingReplies(db).Any(reply => reply.Id == session.Id));
}
