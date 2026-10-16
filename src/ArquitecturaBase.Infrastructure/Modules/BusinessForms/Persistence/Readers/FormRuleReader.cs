using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormRuleReader(ApplicationDbContext dbContext) : IFormRuleReader
{
    public async Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == definitionId)
            .Select(value => new { value.Id, value.NameJson, value.Culture, value.SchemaJson }).SingleOrDefaultAsync(cancellationToken);
        if (definition is null) return null;
        var schemas = await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.DefinitionId == definitionId
            && (dbContext.Set<BusinessFormDefinition>().Any(value => value.Id == definitionId && value.ActiveVersionId == version.Id)
                || dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.VersionId == version.Id && operation.State == FormPublicationState.Published)
                || dbContext.Set<BusinessFormRecord>().Any(record => record.VersionId == version.Id)))
            .OrderByDescending(version => version.Number).Select(version => new FormRuleSchemaRow(version.Number, version.SchemaJson)).ToArrayAsync(cancellationToken);
        return new(definition.Id, definition.NameJson, definition.Culture, definition.SchemaJson, schemas);
    }
    public Task<FormRuleSetStoredRow?> FindCurrentAsync(Guid definitionId, CancellationToken cancellationToken) => Stored(dbContext.Set<BusinessFormRuleSet>().AsNoTracking()
        .Where(value => value.DefinitionId == definitionId && dbContext.Set<BusinessFormDefinition>().Any(definition => definition.Id == definitionId && definition.ActiveRuleSetId == value.Id))).SingleOrDefaultAsync(cancellationToken);
    public Task<FormRuleSetStoredRow?> FindRevisionAsync(Guid definitionId, long revision, CancellationToken cancellationToken) => Stored(dbContext.Set<BusinessFormRuleSet>().AsNoTracking()
        .Where(value => value.DefinitionId == definitionId && value.Revision == revision)).SingleOrDefaultAsync(cancellationToken);
    public Task<PagedResult<FormRuleRevisionResponse>> ListRevisionsAsync(Guid definitionId, ListFormRuleRevisionsRequest request, CancellationToken cancellationToken)
    {
        var sortable = new Dictionary<string,Expression<Func<BusinessFormRuleSet,object?>>>(StringComparer.OrdinalIgnoreCase) { ["revision"] = value => value.Revision };
        return dbContext.Set<BusinessFormRuleSet>().AsNoTracking().Where(value => value.DefinitionId == definitionId)
            .ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("revision",true), value => value.Id)
            .Select(value => new FormRuleRevisionResponse(value.Id, value.Revision, value.CreatedAtUtc,
                value.CreatedBy == null ? null : new FormAuthorResponse(value.CreatedBy.Value,
                    dbContext.Users.Where(user => user.Id == value.CreatedBy).Select(user => user.DisplayName).FirstOrDefault()), value.HasApproval))
            .ToPagedResultAsync(request, cancellationToken);
    }
    public async Task<IReadOnlyList<FormRuleRoleResponse>> ListRolesAsync(CancellationToken cancellationToken) => await dbContext.Roles.AsNoTracking()
        .OrderBy(role => role.Name).ThenBy(role => role.Id).Select(role => new FormRuleRoleResponse(role.Id,role.Name!)).ToArrayAsync(cancellationToken);
    private IQueryable<FormRuleSetStoredRow> Stored(IQueryable<BusinessFormRuleSet> query) => query.Select(value => new FormRuleSetStoredRow(
        value.Id,value.DefinitionId,value.Revision,value.OriginalCulture,value.RulesJson,value.CreatedAtUtc,
        value.CreatedBy == null ? null : new FormAuthorResponse(value.CreatedBy.Value,
            dbContext.Users.Where(user => user.Id == value.CreatedBy).Select(user => user.DisplayName).FirstOrDefault())));
}
