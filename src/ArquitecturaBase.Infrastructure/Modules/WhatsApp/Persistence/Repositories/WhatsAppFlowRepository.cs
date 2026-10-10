using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppFlowRepository(ApplicationDbContext dbContext) : IWhatsAppFlowRepository
{
    public Task LockAsync(Guid neutralOperationId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([WhatsAppLockKeys.Flow(neutralOperationId)], cancellationToken);
    public Task<WhatsAppFlowOperation?> GetAsync(Guid neutralOperationId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppFlowOperation>().SingleOrDefaultAsync(x => x.NeutralOperationId == neutralOperationId, cancellationToken);
    public Task<WhatsAppFlowOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppFlowOperation>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public async Task<IReadOnlyList<WhatsAppFlowBinding>> ListBindingsAsync(Guid operationId, CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppFlowBinding>().Where(x => x.OperationId == operationId).OrderBy(x => x.Culture).ToListAsync(cancellationToken);
    public void Add(WhatsAppFlowOperation operation) { dbContext.RequireTransaction(); dbContext.Set<WhatsAppFlowOperation>().Add(operation); }
    public void AddBinding(WhatsAppFlowBinding binding) { dbContext.RequireTransaction(); dbContext.Set<WhatsAppFlowBinding>().Add(binding); }
}
