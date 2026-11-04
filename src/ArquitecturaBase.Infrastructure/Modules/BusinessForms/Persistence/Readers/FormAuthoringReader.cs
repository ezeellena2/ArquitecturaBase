using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormAuthoringReader(ApplicationDbContext dbContext) : IFormAuthoringReader
{
    public async Task<IReadOnlyList<FormAuthoringDefinitionStoredRow>> ListDefinitionsAsync(int take, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormDefinition>().AsNoTracking()
            .OrderByDescending(definition => definition.CreatedAtUtc).ThenByDescending(definition => definition.Id).Take(take)
            .Select(definition => new FormAuthoringDefinitionStoredRow(definition.NameJson, definition.Culture, definition.Prefix,
                definition.ActiveVersionId != null, definition.DeactivatedAtUtc != null, definition.SchemaJson))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(int take, CancellationToken cancellationToken) =>
        await Project(Existing().OrderByDescending(catalog => catalog.CreatedAtUtc).ThenByDescending(catalog => catalog.Id).Take(take))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormAuthoringCatalogStoredRow>> ListCatalogsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        ids.Count == 0 ? [] : await Project(Existing().Where(catalog => ids.Contains(catalog.Id))).ToListAsync(cancellationToken);

    // A discarded proposal is not something that exists; a pending one is, inactive.
    private IQueryable<BusinessFormCatalog> Existing() => dbContext.Set<BusinessFormCatalog>().AsNoTracking()
        .Where(catalog => catalog.ProposalState == null || catalog.ProposalState != BusinessFormCatalogProposalState.Discarded);

    private static IQueryable<FormAuthoringCatalogStoredRow> Project(IQueryable<BusinessFormCatalog> catalogs) =>
        catalogs.Select(catalog => new FormAuthoringCatalogStoredRow(catalog.Id, catalog.NameJson, catalog.OriginalCulture, catalog.Kind,
            catalog.ValueType, catalog.IsActive, catalog.FieldsJson));
}
