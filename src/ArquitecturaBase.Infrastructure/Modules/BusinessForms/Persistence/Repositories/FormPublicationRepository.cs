using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;

internal sealed class FormPublicationRepository(ApplicationDbContext dbContext) : IFormPublicationRepository
{
    public Task<BusinessFormPublicationOperation?> GetOperationAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().SingleOrDefaultAsync(x => x.OperatorUserId == actor && x.RequestId == requestId, cancellationToken);
    public Task<BusinessFormPublicationOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormVersion>().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    public Task<bool> ExistsOpenOperationAsync(Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormPublicationOperation>().AnyAsync(x => x.DefinitionId == definitionId
            && x.State != FormPublicationState.Published && x.State != FormPublicationState.Failed, cancellationToken);
    public Task<int> CountVersionsAsync(Guid definitionId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormVersion>().CountAsync(x => x.DefinitionId == definitionId, cancellationToken);
    public async Task<IReadOnlyList<BusinessFormField>> ListFieldsAsync(Guid definitionId, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormField>().Where(x => x.DefinitionId == definitionId).ToListAsync(cancellationToken);
    public void AddField(BusinessFormField field) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormField>().Add(field); }
    public void AddVersion(BusinessFormVersion version) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormVersion>().Add(version); }
    public void AddOperation(BusinessFormPublicationOperation operation) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormPublicationOperation>().Add(operation); }
}
