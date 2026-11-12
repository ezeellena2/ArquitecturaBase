using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppAssistantAgentRepository(ApplicationDbContext db) : IWhatsAppAssistantAgentRepository
{
    public Task LockAsync(CancellationToken cancellationToken) => db.AcquireAdvisoryLocksAsync([WhatsAppLockKeys.AssistantAgent], cancellationToken);

    public Task<WhatsAppAssistantAgent?> GetMainAsync(CancellationToken cancellationToken) =>
        db.Set<WhatsAppAssistantAgent>().SingleOrDefaultAsync(agent => agent.IsMain, cancellationToken);

    public void Add(WhatsAppAssistantAgent agent) => db.Set<WhatsAppAssistantAgent>().Add(agent);
}
