using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
internal sealed class FormDefinitionReader(ApplicationDbContext dbContext) : IFormDefinitionReader
{
    private static readonly Dictionary<string, Expression<Func<BusinessFormDefinition, object?>>> Sortable = new(StringComparer.OrdinalIgnoreCase)
        { ["name"] = x => x.Name, ["questionCount"] = x => x.QuestionCount, ["lastChangedAtUtc"] = x => x.ModifiedAtUtc ?? x.CreatedAtUtc };
    public Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(x => x.Id == id)
            .Select(x => new FormDefinitionStoredRow(x.Id, x.NameJson, x.Prefix, x.PrefixLocked, x.Revision, x.Culture, x.SubmitLabelJson,
                x.SchemaJson, x.CreatedAtUtc, x.ModifiedAtUtc, dbContext.Users.Where(user => user.Id == (x.ModifiedBy ?? x.CreatedBy))
                    .Select(user => new FormAuthorResponse(user.Id, user.DisplayName)).FirstOrDefault())).SingleOrDefaultAsync(cancellationToken);
    public Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormDraftChange>().AsNoTracking().Where(x => x.OperatorUserId == actor && x.RequestId == requestId)
            .Select(x => new FormChangeStoredRow(new(x.Id, x.OperatorUserId, x.RequestId, x.DefinitionId, x.Kind, x.ResultFormat),
                x.ProtectedResult, x.ResultExpiresAtUtc, x.CreatedAtUtc,
                dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == x.DefinitionId).Select(definition => definition.Revision).Single())).SingleOrDefaultAsync(cancellationToken);
    public Task<PagedResult<FormDefinitionListResponse>> ListAsync(ListFormDefinitionsRequest request, bool canEdit, CancellationToken cancellationToken)
    {
        var nameColumn = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var sortable = new Dictionary<string, Expression<Func<BusinessFormDefinition, object?>>>(Sortable, StringComparer.OrdinalIgnoreCase)
            { ["name"] = x => EF.Property<string>(x, nameColumn) };
        var query = Search(request.Search, request.DisplayCulture);
        if (request.PublicationFilter == "Published") query = query.Where(_ => false);
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), x => x.Id)
            .Select(x => new FormDefinitionListResponse(x.Id, ReadName(x.NameJson), x.Prefix, x.QuestionCount, null,
                "Draft", x.ModifiedAtUtc ?? x.CreatedAtUtc,
                dbContext.Users.Where(user => user.Id == (x.ModifiedBy ?? x.CreatedBy)).Select(user => new FormAuthorResponse(user.Id, user.DisplayName)).FirstOrDefault(), canEdit))
            .ToPagedResultAsync(request, cancellationToken);
    }
    public async Task<FormDefinitionCounts> CountFacetsAsync(string? search, string displayCulture, CancellationToken cancellationToken)
    {
        var counts = await Search(search, displayCulture).GroupBy(_ => 1).Select(group => new FormDefinitionCounts(group.Count(),
            0, group.Count())).SingleOrDefaultAsync(cancellationToken);
        return counts ?? new(0, 0, 0);
    }
    private static Dictionary<string, string> ReadName(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The stored form name is invalid.");
    private IQueryable<BusinessFormDefinition> Search(string? search, string displayCulture)
    {
        var query = dbContext.Set<BusinessFormDefinition>().AsNoTracking();
        if (string.IsNullOrWhiteSpace(search)) return query;
        var nameColumn = displayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var pattern = LikePatterns.Contains(search.Trim());
        return query.Where(x => EF.Functions.ILike(EF.Property<string>(x, nameColumn), pattern, LikePatterns.EscapeCharacter) || EF.Functions.ILike(x.Prefix, pattern, LikePatterns.EscapeCharacter));
    }
}
