using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormUnusedVersionReader(ApplicationDbContext dbContext) : IFormUnusedVersionReader
{
    public async Task<IReadOnlyList<StructuredFormVersionReference>> ListUnusedAsync(
        IReadOnlyCollection<StructuredFormVersionReference> candidates, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (candidates.Count == 0) return [];
        var expected = candidates.ToHashSet();
        var rows = await Unused(candidates.Select(candidate => candidate.VersionId).Distinct().ToArray())
            .Select(version => new StructuredFormVersionReference(version.DefinitionId, version.Id)).ToArrayAsync(cancellationToken);
        return rows.Where(expected.Contains).ToArray();
    }

    private IQueryable<BusinessFormVersion> Unused(IReadOnlyCollection<Guid> versionIds) =>
        from version in dbContext.Set<BusinessFormVersion>().AsNoTracking()
        join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking()
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter]) on version.DefinitionId equals definition.Id
        where versionIds.Contains(version.Id)
            && dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == definition.Id
                && operation.VersionId == version.Id && (operation.State == FormPublicationState.Published || operation.State == FormPublicationState.Failed))
            && !dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.DefinitionId == definition.Id
                && operation.State != FormPublicationState.Published && operation.State != FormPublicationState.Failed)
            && (definition.IsDeleted || definition.DeactivatedAtUtc != null || definition.ActiveVersionId != version.Id)
        select version;
}