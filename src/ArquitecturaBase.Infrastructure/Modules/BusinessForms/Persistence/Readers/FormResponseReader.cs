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
    public async Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var definition = await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == id && value.ActiveVersionId != null)
            .Select(value => new { value.Id, value.Prefix, value.ActiveVersionId,
                HasApproval = dbContext.Set<BusinessFormRuleSet>().Any(rule => rule.DefinitionId == value.Id && rule.HasApproval
                    && (rule.Id == value.ActiveRuleSetId || dbContext.Set<BusinessFormRecord>().Any(record => record.RuleSetId == rule.Id))) })
            .SingleOrDefaultAsync(cancellationToken);
        if (definition is null) return null;
        var versionIds = await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => version.DefinitionId == id
                && (version.Id == definition.ActiveVersionId || dbContext.Set<BusinessFormPublicationOperation>().Any(operation => operation.VersionId == version.Id && operation.State == FormPublicationState.Published) || dbContext.Set<BusinessFormRecord>().Any(record => record.VersionId == version.Id)))
            .Select(version => version.Id).ToArrayAsync(cancellationToken);
        var versions = (await records.ListVersionsAsync(versionIds, cancellationToken)).Values.OrderBy(version => version.Number).ToArray();
        var result = new FormResponseDefinitionRow(definition.Id, definition.Prefix, definition.ActiveVersionId!.Value, versions, definition.HasApproval);
        var catalogIds = result.Schemas.Values.SelectMany(schema => schema.Questions)
            .Select(question => question.Source?.Catalog).OfType<Guid>().Distinct().ToArray();
        var catalogs = catalogIds.Length == 0 ? []
            : await dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalogIds.Contains(catalog.Id))
                .Select(catalog => new CatalogMetadataRow { Id = catalog.Id, ValueType = catalog.ValueType.ToString(), FieldsJson = catalog.FieldsJson })
                .ToArrayAsync(cancellationToken);
        var historical = catalogIds.Length == 0 ? [] : await FindHistoricalCatalogFields(id).ToArrayAsync(cancellationToken);
        var catalogFields = ReadCatalogFields(catalogs, historical);
        return result with { CatalogValueTypes = catalogs.ToDictionary(catalog => catalog.Id, catalog => catalog.ValueType), CatalogFields = catalogFields };
    }
    public async Task<PagedResult<FormResponseStoredRow>> ListAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, CancellationToken cancellationToken)
    {
        var page = await Ordered(Filter(definition, request), definition, request)
            .Select(record => new { record.SourceReceiptId, record.SubmittedAtUtc, record.CreatedAtUtc, record.ActorUserId, record.State,
                HasPdf = dbContext.Set<BusinessFormDocument>().Any(document => document.RecordId == record.Id),
                DisplayName = dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() })
            .ToPagedResultAsync(request, cancellationToken);
        var hydrated = (await records.FindBatchAsync(page.Items.Select(record => record.SourceReceiptId).ToArray(), definition.VersionsById, cancellationToken)).Rows.ToDictionary(record => record.SourceReceiptId);
        return new(page.Items.Select(record => new FormResponseStoredRow(hydrated[record.SourceReceiptId], record.SubmittedAtUtc,
            new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc, record.State.ToString(), record.HasPdf) { Version = definition.VersionsById[hydrated[record.SourceReceiptId].VersionId] }).ToArray(), page.Page, page.PageSize, page.TotalCount);
    }
    public async Task<FormResponseStoredRow?> FindRecordAsync(Guid id, CancellationToken cancellationToken)
    {
        var record = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(value => value.Id == id)
            .Select(value => new { value.SourceReceiptId, value.SubmittedAtUtc, value.CreatedAtUtc, value.ActorUserId, value.State, value.CaptureOriginJson,
                HasPdf = dbContext.Set<BusinessFormDocument>().Any(document => document.RecordId == value.Id),
                DisplayName = dbContext.Users.Where(user => user.Id == value.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() }).SingleOrDefaultAsync(cancellationToken);
        if (record is null) return null;
        FormAuthorResponse? captureOperator = null;
        if (record.CaptureOriginJson is { } originJson
            && JsonSerializer.Deserialize<FormRecordCaptureOriginSnapshot>(originJson, JsonSerializerOptions.Web)?.Origin.OperatorUserId is { } operatorId)
        {
            var displayName = await dbContext.Users.AsNoTracking().Where(user => user.Id == operatorId)
                .Select(user => user.DisplayName).SingleOrDefaultAsync(cancellationToken);
            captureOperator = new(operatorId, displayName);
        }
        var batch = await records.FindBatchAsync([record.SourceReceiptId], null, cancellationToken);
        var stored = batch.Rows.Single();
        return new(stored, record.SubmittedAtUtc, new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc, record.State.ToString(), record.HasPdf) { Version = batch.Versions[stored.VersionId], CaptureOriginJson = record.CaptureOriginJson, CaptureOperator = captureOperator };
    }
    public async Task<IReadOnlyList<Guid>> ListExportIdsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, int limit, CancellationToken cancellationToken) =>
        await Ordered(Filter(definition, request), definition, request).Take(limit).Select(record => record.SourceReceiptId).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyList<FormResponseStoredRow>> ListByReceiptIdsAsync(FormResponseDefinitionRow definition, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => ids.Contains(record.SourceReceiptId))
            .Select(record => new { record.SourceReceiptId, record.SubmittedAtUtc, record.CreatedAtUtc, record.ActorUserId, record.State,
                HasPdf = dbContext.Set<BusinessFormDocument>().Any(document => document.RecordId == record.Id),
                DisplayName = dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault() }).ToArrayAsync(cancellationToken);
        var hydrated = (await records.FindBatchAsync(ids, definition.VersionsById, cancellationToken)).Rows.ToDictionary(record => record.SourceReceiptId);
        return rows.Select(record => new FormResponseStoredRow(hydrated[record.SourceReceiptId], record.SubmittedAtUtc,
            new(record.ActorUserId, record.DisplayName), record.CreatedAtUtc,record.State.ToString(), record.HasPdf) { Version = definition.VersionsById[hydrated[record.SourceReceiptId].VersionId] }).ToArray();
    }
    public Task<long?> FindLatestNumberAsync(Guid id, CancellationToken cancellationToken) => dbContext.Set<BusinessFormDefinition>().AsNoTracking()
        .Where(value => value.Id == id && value.ActiveVersionId != null).Select(value => (long?)value.LastRecordNumber).SingleOrDefaultAsync(cancellationToken);
    private static string Text(IReadOnlyDictionary<string, string> translations, string culture) =>
        translations.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : translations[culture == "en" ? "es" : "en"];
}
