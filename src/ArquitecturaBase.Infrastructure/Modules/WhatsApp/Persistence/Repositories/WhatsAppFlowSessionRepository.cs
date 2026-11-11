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
    public void Add(WhatsAppFlowSession session) => dbContext.Set<WhatsAppFlowSession>().Add(session);
}
