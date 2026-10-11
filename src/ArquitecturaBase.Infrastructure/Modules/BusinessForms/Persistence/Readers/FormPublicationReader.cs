using System.Linq.Expressions;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormPublicationReader(ApplicationDbContext dbContext) : IFormPublicationReader
{
    public async Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking().Where(value => value.State != FormPublicationState.Published
            && value.State != FormPublicationState.Failed && (value.LeaseExpiresAtUtc == null || value.LeaseExpiresAtUtc <= nowUtc)
            && (value.NextCheckAtUtc == null || value.NextCheckAtUtc <= nowUtc))
            .OrderBy(value => value.NextCheckAtUtc ?? value.AcceptedAtUtc).ThenBy(value => value.Id).Select(value => value.Id).Take(20).ToArrayAsync(cancellationToken);
    private IQueryable<FormPublicationStoredRow> Stored(Expression<Func<BusinessFormPublicationOperation, bool>> predicate) =>
        from operation in dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking().Where(predicate)
        join version in dbContext.Set<BusinessFormVersion>() on operation.VersionId equals version.Id
        select new FormPublicationStoredRow(operation.Id, operation.OperatorUserId, operation.RequestId, operation.DefinitionId,
            operation.VersionId, operation.ProtectedManifest, version.SchemaJson, operation.State.ToString());
    public Task<FormPublicationStoredRow?> FindStoredAsync(Guid operationId, CancellationToken cancellationToken) =>
        Stored(value => value.Id == operationId).SingleOrDefaultAsync(cancellationToken);
    public Task<FormPublicationStoredRow?> FindVersionAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken) =>
        Stored(value => value.DefinitionId == definitionId && value.VersionId == versionId).SingleOrDefaultAsync(cancellationToken);
    public Task<FormPublicationStoredRow?> FindActiveVersionAsync(Guid definitionId, CancellationToken cancellationToken) =>
        Stored(value => value.DefinitionId == definitionId && dbContext.Set<BusinessFormDefinition>()
            .Any(definition => definition.Id == definitionId && definition.ActiveVersionId == value.VersionId)).SingleOrDefaultAsync(cancellationToken);
    public Task<bool> ExistsOpenOperationAsync(Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking().AnyAsync(x => x.DefinitionId == definitionId
            && x.State != FormPublicationState.Published && x.State != FormPublicationState.Failed, cancellationToken);
    public Task<int> CountVersionsAsync(Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormVersion>().AsNoTracking().CountAsync(version => version.DefinitionId == definitionId, cancellationToken);
    public Task<FormPublicationStatusResponse?> FindOperationAsync(Guid definitionId, Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking()
            .Where(x => x.DefinitionId == definitionId && x.OperatorUserId == actor && x.RequestId == requestId)
            .Select(x => new FormPublicationStatusResponse(x.RequestId, x.Id, x.DefinitionId, x.VersionId, x.VersionNumber, x.DraftRevision,
                x.State.ToString(), x.Stage, x.AcceptedAtUtc, x.UpdatedAtUtc, x.NextCheckAtUtc, false, x.State == FormPublicationState.Uncertain))
            .SingleOrDefaultAsync(cancellationToken);
}
