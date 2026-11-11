using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;
internal sealed class WhatsAppFlowSessionRepository(ApplicationDbContext dbContext) : IWhatsAppFlowSessionRepository
{
    public Task<WhatsAppFlowSession?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppFlowSession>().SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
    public async Task<IReadOnlyList<WhatsAppFlowSession>> ListAwaitingLocationAsync(Guid contactId, DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppFlowSession>()
            .Where(session => session.ContactId == contactId && session.LocationState == WhatsAppFlowLocationState.Requested
                && session.CaptureState == WhatsAppFlowCaptureState.Pending && session.ExpiresAtUtc > nowUtc)
            .OrderBy(session => session.CreatedAtUtc).ThenBy(session => session.Id)
            .ToListAsync(cancellationToken);
    public void Add(WhatsAppFlowSession session) => dbContext.Set<WhatsAppFlowSession>().Add(session);
}
