using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Repositories;
internal sealed class FormDraftRepository(ApplicationDbContext dbContext) : IFormDraftRepository
{
    public Task LockRequestAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Request(actor, requestId)], cancellationToken);
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) => dbContext.AcquireAdvisoryLocksAsync([FormAdvisoryLockKeys.Definitions], cancellationToken);
    public Task<BusinessFormDefinition?> GetAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormDefinition>().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    public Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDraftChange>().SingleOrDefaultAsync(x => x.OperatorUserId == actor && x.RequestId == requestId, cancellationToken);
    public Task<bool> ExistsPrefixAsync(string prefix, Guid exceptId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().AnyAsync(x => x.Prefix == prefix && x.Id != exceptId, cancellationToken);
    public async Task<FormRetiredIdentities> FindRetiredIdentitiesAsync(Guid definitionId, IReadOnlyCollection<Guid> fieldIds,
        IReadOnlyCollection<Guid> choiceIds, CancellationToken cancellationToken)
    {
        if (fieldIds.Count == 0 && choiceIds.Count == 0) return FormRetiredIdentities.Empty;
        var identities = await dbContext.Set<BusinessFormRetiredIdentity>()
            .Where(identity => identity.DefinitionId == definitionId
                && ((identity.Kind == BusinessFormIdentityKind.Field && fieldIds.Contains(identity.IdentityId))
                    || (identity.Kind == BusinessFormIdentityKind.Choice && choiceIds.Contains(identity.IdentityId))))
            .Select(identity => new { identity.IdentityId, identity.Kind }).ToListAsync(cancellationToken);
        return new(identities.Where(identity => identity.Kind == BusinessFormIdentityKind.Field).Select(identity => identity.IdentityId).ToArray(),
            identities.Where(identity => identity.Kind == BusinessFormIdentityKind.Choice).Select(identity => identity.IdentityId).ToArray());
    }
    public void AddRetiredIdentities(Guid definitionId, FormRetiredIdentities identities)
    {
        dbContext.RequireTransaction();
        dbContext.Set<BusinessFormRetiredIdentity>().AddRange(
            identities.Fields.Select(id => BusinessFormRetiredIdentity.Create(definitionId, id, BusinessFormIdentityKind.Field))
                .Concat(identities.Choices.Select(id => BusinessFormRetiredIdentity.Create(definitionId, id, BusinessFormIdentityKind.Choice))));
    }
    public void Add(BusinessFormDefinition definition) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormDefinition>().Add(definition); }
    public void AddChange(BusinessFormDraftChange change) { dbContext.RequireTransaction(); dbContext.Set<BusinessFormDraftChange>().Add(change); }
    public async Task UpdateExpiredResultsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var expired = await dbContext.Set<BusinessFormDraftChange>().Where(x => x.ProtectedResult != null && x.ResultExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.ResultExpiresAtUtc).ThenBy(x => x.Id).Take(100).ToListAsync(cancellationToken);
        foreach (var change in expired) change.ExpireResult(nowUtc);
    }
}
