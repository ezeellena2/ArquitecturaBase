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
    public async Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken cancellationToken)
    {
        var active = await (from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == id)
                            join version in dbContext.Set<BusinessFormVersion>() on definition.ActiveVersionId equals version.Id
                            join operation in dbContext.Set<BusinessFormPublicationOperation>() on version.Id equals operation.VersionId
                            where version.DefinitionId == id && operation.DefinitionId == id
                            select new FormActiveVersionResponse(version.Id, version.Number, operation.DraftRevision)).SingleOrDefaultAsync(cancellationToken);
        var latest = await dbContext.Set<BusinessFormPublicationOperation>().AsNoTracking().Where(value => value.DefinitionId == id)
            .OrderByDescending(value => value.VersionNumber)
            .Select(value => new FormCurrentPublicationResponse(value.VersionId, value.VersionNumber, value.DraftRevision,
                value.State.ToString(), value.Stage, value.State == FormPublicationState.Uncertain, false,
                value.OperatorUserId == recoveryActor ? (Guid?)value.RequestId : null, null)).FirstOrDefaultAsync(cancellationToken);
        return new(active, latest, latest?.State ?? (active is null ? "Draft" : "Published"));
    }
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
        if (request.PublicationFilter == "Published") query = query.Where(value => value.ActiveVersionId != null);
        else if (request.PublicationFilter == "Drafts") query = query.Where(value => value.ActiveVersionId == null);
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), x => x.Id)
            .Select(x => new FormDefinitionListResponse(x.Id, ReadName(x.NameJson), x.Prefix, x.QuestionCount, dbContext.Set<BusinessFormVersion>().Where(version => version.Id == x.ActiveVersionId).Select(version => (long?)version.Number).SingleOrDefault(),
                DisplayState(dbContext.Set<BusinessFormPublicationOperation>().Where(operation => operation.DefinitionId == x.Id)
                    .OrderByDescending(operation => operation.VersionNumber).Select(operation => (FormPublicationState?)operation.State).FirstOrDefault(), x.ActiveVersionId != null), x.ModifiedAtUtc ?? x.CreatedAtUtc,
                dbContext.Users.Where(user => user.Id == (x.ModifiedBy ?? x.CreatedBy)).Select(user => new FormAuthorResponse(user.Id, user.DisplayName)).FirstOrDefault(), canEdit))
            .ToPagedResultAsync(request, cancellationToken);
    }
    public async Task<FormDefinitionCounts> CountFacetsAsync(string? search, string displayCulture, CancellationToken cancellationToken)
    {
        var counts = await Search(search, displayCulture).GroupBy(_ => 1).Select(group => new FormDefinitionCounts(group.Count(),
            group.Count(value => value.ActiveVersionId != null), group.Count(value => value.ActiveVersionId == null))).SingleOrDefaultAsync(cancellationToken);
        return counts ?? new(0, 0, 0);
    }
    private static Dictionary<string, string> ReadName(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The stored form name is invalid.");
    private static string DisplayState(FormPublicationState? latestState, bool hasActiveVersion) => latestState?.ToString() ?? (hasActiveVersion ? "Published" : "Draft");
    private IQueryable<BusinessFormDefinition> Search(string? search, string displayCulture)
    {
        var query = dbContext.Set<BusinessFormDefinition>().AsNoTracking();
        if (string.IsNullOrWhiteSpace(search)) return query;
        var nameColumn = displayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var pattern = LikePatterns.Contains(search.Trim());
        return query.Where(x => EF.Functions.ILike(EF.Property<string>(x, nameColumn), pattern, LikePatterns.EscapeCharacter) || EF.Functions.ILike(x.Prefix, pattern, LikePatterns.EscapeCharacter));
    }
}
