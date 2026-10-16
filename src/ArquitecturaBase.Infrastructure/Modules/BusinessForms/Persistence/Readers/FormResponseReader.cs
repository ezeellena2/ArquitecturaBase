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
internal sealed partial class FormResponseReader(ApplicationDbContext dbContext, IFormRecordReader records) : IFormResponseReader
{
    public Task<PagedResult<FormNavigationResponse>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken)
    {
        var query = from definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking()
                    join version in dbContext.Set<BusinessFormVersion>().AsNoTracking() on definition.ActiveVersionId equals version.Id
                    select version;
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            var column = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
            query = query.Where(version => EF.Functions.ILike(EF.Property<string>(version, column), pattern, LikePatterns.EscapeCharacter));
        }
        var nameColumn = request.DisplayCulture == "en" ? "DisplayNameEn" : "DisplayNameEs";
        var sortable = new Dictionary<string, Expression<Func<BusinessFormVersion, object?>>>(StringComparer.OrdinalIgnoreCase)
            { ["name"] = version => EF.Property<string>(version, nameColumn) };
        return query.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), version => version.DefinitionId)
            .Select(version => new FormNavigationResponse(version.DefinitionId, Name(version.NameJson!, request.DisplayCulture),
                dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == version.DefinitionId).Select(definition => definition.Prefix).Single(), version.Number))
            .ToPagedResultAsync(request, cancellationToken);
    }
    public async Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == id && value.ActiveVersionId != null)
            .Select(value => new { value.Id, value.Prefix, value.ActiveVersionId }).SingleOrDefaultAsync(cancellationToken);
        if (definition is null) return null;
        var versions = await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.DefinitionId == id
                && (version.Id == definition.ActiveVersionId || dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.VersionId == version.Id && operation.State == FormPublicationState.Published) || dbContext.Set<BusinessFormRecord>().Any(record => record.VersionId == version.Id)))
            .OrderBy(version => version.Number).Select(version => new FormResponseVersionRow(version.Id, version.Number, version.NameJson!, version.SchemaJson))
            .ToListAsync(cancellationToken);
        return new(definition.Id, definition.Prefix, definition.ActiveVersionId!.Value, versions);
    }
    public async Task<PagedResult<FormResponseStoredRow>> ListAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, CancellationToken cancellationToken)
    {
        var page = await Ordered(Filter(definition, request), definition, request)
            .Select(record => new { record.SourceReceiptId, record.SubmittedAtUtc, record.CreatedAtUtc, record.ActorUserId,
                DisplayName = dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() })
            .ToPagedResultAsync(request, cancellationToken);
        var hydrated = (await records.ListAsync(page.Items.Select(record => record.SourceReceiptId).ToArray(), cancellationToken)).ToDictionary(record => record.SourceReceiptId);
        return new(page.Items.Select(record => new FormResponseStoredRow(hydrated[record.SourceReceiptId], record.SubmittedAtUtc,
            new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc)).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    public async Task<FormResponseStoredRow?> FindRecordAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(value => value.Id == id)
            .Select(value => new { value.SourceReceiptId, value.SubmittedAtUtc, value.CreatedAtUtc, value.ActorUserId,
                DisplayName = dbContext.Users.Where(user => user.Id == value.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() }).SingleOrDefaultAsync(cancellationToken);
        if (record is null) return null;
        var stored = (await records.ListAsync([record.SourceReceiptId], cancellationToken)).Single();
        return new(stored, record.SubmittedAtUtc, new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc);
    }
    public async Task<IReadOnlyList<Guid>> ListExportIdsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, int limit, CancellationToken cancellationToken) =>
        await Ordered(Filter(definition, request), definition, request).Take(limit).Select(record => record.SourceReceiptId).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyList<FormResponseStoredRow>> ListByReceiptIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => ids.Contains(record.SourceReceiptId))
            .Select(record => new { record.SourceReceiptId, record.SubmittedAtUtc, record.CreatedAtUtc, record.ActorUserId,
                DisplayName = dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() }).ToArrayAsync(cancellationToken);
        var hydrated = (await records.ListAsync(ids, cancellationToken)).ToDictionary(record => record.SourceReceiptId);
        return rows.Select(record => new FormResponseStoredRow(hydrated[record.SourceReceiptId], record.SubmittedAtUtc,
            new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc)).ToArray();
    }
    public Task<long?> FindLatestNumberAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormDefinition>().AsNoTracking()
        .Where(value => value.Id == id && value.ActiveVersionId != null).Select(value => (long?)value.LastRecordNumber).SingleOrDefaultAsync(cancellationToken);
    private static string Name(string json, string culture) => JsonSerializer.Deserialize<Dictionary<string,string>>(json)![culture];
}
