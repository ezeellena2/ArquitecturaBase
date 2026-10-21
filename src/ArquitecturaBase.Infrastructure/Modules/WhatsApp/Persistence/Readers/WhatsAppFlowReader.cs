using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppFlowReader(ApplicationDbContext dbContext) : IWhatsAppFlowReader
{
    public async Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<WhatsAppFlowOperation>().AsNoTracking().Where(x => !x.Completed
            && (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= nowUtc) && (x.NextCheckAtUtc == null || x.NextCheckAtUtc <= nowUtc))
            .OrderBy(x => x.NextCheckAtUtc ?? x.CreatedAtUtc).ThenBy(x => x.Id).Select(x => x.NeutralOperationId).Take(20).ToListAsync(cancellationToken);
    public Task<WhatsAppFlowOperationRow?> FindAsync(Guid neutralOperationId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppFlowOperation>().AsNoTracking().Where(x => x.NeutralOperationId == neutralOperationId)
            .Select(x => new WhatsAppFlowOperationRow(x.Id, x.NeutralOperationId, x.DefinitionId, x.VersionId, x.ActorUserId, x.ProtectedManifest,
                x.NextCheckAtUtc, dbContext.Set<WhatsAppFlowBinding>().Where(binding => binding.OperationId == x.Id).OrderBy(binding => binding.Culture)
                    .Select(binding => new WhatsAppFlowBindingRow(binding.Id, binding.Culture, binding.TechnicalName, binding.ProtectedProjection,
                        binding.MetaFlowId, binding.State.ToString(), binding.Step.ToString(), binding.FailureCode, binding.DiagnosticsJson)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    public async Task<IReadOnlyList<Guid>> ListDueDeprecationBindingsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await DueDeprecationRows(nowUtc).OrderBy(binding => binding.DeprecationNextCheckAtUtc ?? binding.CreatedAtUtc)
            .ThenBy(binding => binding.Id).Select(binding => binding.Id).Take(20).ToArrayAsync(cancellationToken);

    private IQueryable<WhatsAppFlowBinding> DueDeprecationRows(DateTime nowUtc) => dbContext.Set<WhatsAppFlowBinding>().AsNoTracking()
        .Where(binding => binding.State == WhatsAppFlowPublicationState.Published && binding.MetaFlowId != null
            && binding.DeprecationState != WhatsAppFlowDeprecationState.Deprecated
            && (binding.DeprecationLeaseExpiresAtUtc == null || binding.DeprecationLeaseExpiresAtUtc <= nowUtc)
            && (binding.DeprecationNextCheckAtUtc == null || binding.DeprecationNextCheckAtUtc <= nowUtc)
            && !WhatsAppFlowSessionQuery.OpenWork(dbContext, nowUtc).Any(session => session.VersionId == binding.VersionId));

    public Task<WhatsAppFlowDeprecationCandidateRow?> FindDeprecationAsync(Guid bindingId, CancellationToken cancellationToken) =>
        (from binding in dbContext.Set<WhatsAppFlowBinding>().AsNoTracking()
         join operation in dbContext.Set<WhatsAppFlowOperation>().AsNoTracking() on binding.OperationId equals operation.Id
         where binding.Id == bindingId
         select new WhatsAppFlowDeprecationCandidateRow(binding.Id, operation.Id, operation.NeutralOperationId, operation.DefinitionId, operation.VersionId))
            .SingleOrDefaultAsync(cancellationToken);
    public Task<bool> ExistsOpenVersionWorkAsync(Guid versionId, DateTime nowUtc, CancellationToken cancellationToken) =>
        WhatsAppFlowSessionQuery.OpenWork(dbContext, nowUtc).AnyAsync(session => session.VersionId == versionId, cancellationToken);
}
