using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
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

    // A deleted draft keeps its proposal, like its prefix: the same proposal never creates a second draft.
    public Task<FormAuthoringCreatedRow?> FindDefinitionByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().AsNoTracking().IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .Where(definition => definition.ProposalId == proposalId)
            .Select(definition => new FormAuthoringCreatedRow(definition.Id, definition.ProposalEvidenceJson!))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<FormAuthoringCreatedRow?> FindCatalogByProposalAsync(Guid proposalId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalog.ProposalId == proposalId)
            .Select(catalog => new FormAuthoringCreatedRow(catalog.Id, catalog.ProposalEvidenceJson!))
            .SingleOrDefaultAsync(cancellationToken);

    // The name as it reads in that language (the original when it has no translation), without telling case apart.
    public Task<bool> ExistsDefinitionNameAsync(string culture, string name, CancellationToken cancellationToken)
    {
        var column = DisplayName(culture);
        var pattern = LikePatterns.Exact(name.Trim());
        return dbContext.Set<BusinessFormDefinition>().AsNoTracking()
            .AnyAsync(definition => EF.Functions.ILike(EF.Property<string>(definition, column), pattern, LikePatterns.EscapeCharacter), cancellationToken);
    }

    public Task<bool> ExistsCatalogNameAsync(string culture, string name, CancellationToken cancellationToken)
    {
        var column = DisplayName(culture);
        var pattern = LikePatterns.Exact(name.Trim());
        return Existing().AnyAsync(catalog => EF.Functions.ILike(EF.Property<string>(catalog, column), pattern, LikePatterns.EscapeCharacter), cancellationToken);
    }

    // A discarded proposal is not something that exists; a pending one is, inactive.
    private IQueryable<BusinessFormCatalog> Existing() => dbContext.Set<BusinessFormCatalog>().AsNoTracking()
        .Where(catalog => catalog.ProposalState == null || catalog.ProposalState != BusinessFormCatalogProposalState.Discarded);

    private static string DisplayName(string culture) => culture == "en" ? "DisplayNameEn" : "DisplayNameEs";

    private static IQueryable<FormAuthoringCatalogStoredRow> Project(IQueryable<BusinessFormCatalog> catalogs) =>
        catalogs.Select(catalog => new FormAuthoringCatalogStoredRow(catalog.Id, catalog.NameJson, catalog.OriginalCulture, catalog.Kind,
            catalog.ValueType, catalog.IsActive, catalog.FieldsJson));
}
