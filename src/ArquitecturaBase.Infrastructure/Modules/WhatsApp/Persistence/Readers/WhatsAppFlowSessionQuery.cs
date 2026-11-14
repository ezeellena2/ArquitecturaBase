using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal static class WhatsAppFlowSessionQuery
{
    internal static IQueryable<WhatsAppFlowSession> Pending(ApplicationDbContext db) => db.Set<WhatsAppFlowSession>().AsNoTracking()
        .Where(session => session.CaptureState == WhatsAppFlowCaptureState.Pending);
    // Without AsNoTracking, which EF applies to the whole query wherever it appears: the retention embeds it in a query whose sends it
    // changes (H13). The readers embed it in queries that skip tracking on their own.
    internal static IQueryable<WhatsAppFlowSession> WithPendingReplies(ApplicationDbContext db) => db.Set<WhatsAppFlowSession>().Where(session =>
        session.CaptureState == WhatsAppFlowCaptureState.Pending && db.Set<WhatsAppMessage>().Any(message => message.Direction == WhatsAppMessageDirection.Inbound
            && message.Kind == WhatsAppMessageKind.FlowReply && message.ProcessedAtUtc == null && message.ProtectedFlowResponse != null
            && (message.FlowSessionId == session.Id || message.FlowSessionId == null && message.ContactId == session.ContactId
                && message.OccurredAtUtc >= session.CreatedAtUtc && message.OccurredAtUtc < session.ExpiresAtUtc)));
    internal static IQueryable<WhatsAppFlowSession> OpenWork(ApplicationDbContext db, DateTime nowUtc) => Pending(db)
        .Where(session => session.ExpiresAtUtc > nowUtc || WithPendingReplies(db).Any(reply => reply.Id == session.Id));
}
