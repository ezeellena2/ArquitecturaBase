using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;
internal sealed class WhatsAppFlowSessionRepository(ApplicationDbContext dbContext) : IWhatsAppFlowSessionRepository
{
    public Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppFlowSession>().SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
    public async Task<IReadOnlyList<WhatsAppFlowLocationWait>> ListAwaitingLocationAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken) =>
        (await (from session in dbContext.Set<WhatsAppFlowSession>()
                join request in dbContext.Set<WhatsAppOutgoingMessage>() on session.Id equals request.FlowSessionId
                where session.ContactId == contactId && session.LocationState == WhatsAppFlowLocationState.Requested
                    && session.CaptureState == WhatsAppFlowCaptureState.Pending && session.ExpiresAtUtc > nowUtc
                    && request.Kind == WhatsAppOutgoingMessageKind.LocationRequest
                orderby session.CreatedAtUtc, session.Id
                select new { session, request }).ToListAsync(cancellationToken))
        .Select(row => new WhatsAppFlowLocationWait(row.session, row.request)).ToArray();
    public async Task<IReadOnlyList<WhatsAppFlowOpenSend>> ListOpenAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken) =>
        (await (from session in dbContext.Set<WhatsAppFlowSession>()
                join binding in dbContext.Set<WhatsAppFlowBinding>() on new { session.BindingId, session.VersionId } equals new { BindingId = binding.Id, binding.VersionId }
                join contact in dbContext.Set<WhatsAppContact>() on session.ContactId equals contact.Id
                where session.ContactId == contactId && session.CaptureState == WhatsAppFlowCaptureState.Pending && session.ExpiresAtUtc > nowUtc
                    && session.LocationState != WhatsAppFlowLocationState.Requested
                    // A2: of the account and the link the number has today.
                    && (Guid?)session.UserId == contact.UserId && session.LinkRevision == contact.LinkRevision
                    // A2: its form went out by WhatsApp: Meta accepted its Flow, or a person reviewed it as sent. One still Pending, Sending
                    // or Uncertain may never have reached the person.
                    && dbContext.Set<WhatsAppOutgoingMessage>().Any(flow => flow.FlowSessionId == session.Id && flow.Kind == WhatsAppOutgoingMessageKind.Flow
                        && !flow.IsResend && (flow.State == WhatsAppOutgoingMessageState.Accepted || flow.State == WhatsAppOutgoingMessageState.Reviewed))
                    // A7: no reply, also one the reply worker attached and has not captured yet.
                    && !dbContext.Set<WhatsAppMessage>().Any(reply => reply.FlowSessionId == session.Id && reply.Direction == WhatsAppMessageDirection.Inbound
                        && reply.Kind == WhatsAppMessageKind.FlowReply)
                // The first message of the send: its Flow, or the location request that went before it.
                let first = dbContext.Set<WhatsAppOutgoingMessage>().Where(outgoing => outgoing.FlowSessionId == session.Id && !outgoing.IsResend
                        && (outgoing.Kind == WhatsAppOutgoingMessageKind.Flow || outgoing.Kind == WhatsAppOutgoingMessageKind.LocationRequest))
                    .OrderBy(outgoing => outgoing.RequestedAtUtc).ThenBy(outgoing => outgoing.Id).FirstOrDefault()
                where first != null
                orderby session.CreatedAtUtc, session.Id
                select new { session, first, binding.OperationId, binding.ProtectedProjection }).Take(50).ToListAsync(cancellationToken))
        .Select(row => new WhatsAppFlowOpenSend(row.session, row.first!, row.OperationId, row.ProtectedProjection)).ToArray();
    public void Add(WhatsAppFlowSession session) => dbContext.Set<WhatsAppFlowSession>().Add(session);
}
