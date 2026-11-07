using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormEntryReader(ApplicationDbContext dbContext) : IFormEntryReader
{
    public async Task<FormEntryDefinitionRow?> FindDefinitionAsync(Guid definitionId, Guid? versionId, CancellationToken cancellationToken)
    {
        var definition = await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(value => value.Id == definitionId)
            .Select(value => new { value.Prefix, value.ActiveVersionId, Deactivated = value.DeactivatedAtUtc != null }).SingleOrDefaultAsync(cancellationToken);
        if (definition is null) return null;
        var target = versionId ?? definition.ActiveVersionId;
        var version = target is null ? null : await FormOfferableVersionQuery.PublishedVersions(dbContext)
            .Where(value => value.Id == target && value.DefinitionId == definitionId)
            .Select(value => new FormEntryVersionRow(value.Id, value.Number, value.NameJson!, value.SubmitLabelJson!, value.Culture!, value.SchemaJson))
            .SingleOrDefaultAsync(cancellationToken);
        return new(definitionId, definition.Prefix, definition.ActiveVersionId, definition.Deactivated, version);
    }

    public async Task<PagedResult<FormCatalogStoredItemRow>> ListItemsAsync(FormEntryItemsQuery query, CancellationToken cancellationToken)
    {
        var items = dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == query.CatalogId && item.IsActive);
        if (query.Containment is not null) items = items.Where(item => EF.Functions.JsonContains(item.FieldValuesJson, query.Containment));
        if (query.WithoutField is { } key)
        {
            // LD1-A: the options without a value in the field chosen first: the key is missing, null or an empty text.
            var nullValue = "{\"" + Guid.Parse(key).ToString("D") + "\":null}";
            var emptyValue = "{\"" + Guid.Parse(key).ToString("D") + "\":\"\"}";
            items = items.Where(item => !EF.Functions.JsonExists(item.FieldValuesJson, key)
                || EF.Functions.JsonContains(item.FieldValuesJson, nullValue) || EF.Functions.JsonContains(item.FieldValuesJson, emptyValue));
        }
        if (query.Ids is { } ids) items = items.Where(item => ids.Contains(item.Id));
        if (query.Search is not null)
        {
            var pattern = LikePatterns.Contains(query.Search);
            items = items.Where(item => EF.Functions.ILike(item.Code, pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(item.TextEs!, pattern, LikePatterns.EscapeCharacter) || EF.Functions.ILike(item.TextEn!, pattern, LikePatterns.EscapeCharacter)
                || EF.Functions.ILike(item.DetailEs!, pattern, LikePatterns.EscapeCharacter) || EF.Functions.ILike(item.DetailEn!, pattern, LikePatterns.EscapeCharacter));
        }
        var total = await items.CountAsync(cancellationToken);
        var rows = await items.OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn,
                item.DetailEs, item.DetailEn, item.Position, item.IsActive, item.Value, item.FieldValuesJson))
            .ToListAsync(cancellationToken);
        return new(rows, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<FormEntryGroupRow>> ListGroupsAsync(Guid catalogId, string? containment, Guid fieldId,
        CancellationToken cancellationToken) =>
        (await dbContext.Database.SqlQueryRaw<GroupSqlRow>("""
            SELECT NULLIF(item."FieldValuesJson" ->> {2}, '') AS "Value", COUNT(*)::integer AS "Count"
            FROM "BusinessFormCatalogItems" AS item
            WHERE item."CatalogId" = {0} AND item."IsActive" AND item."FieldValuesJson" @> {1}::jsonb
            GROUP BY 1
            """.ReplaceLineEndings("\n"), catalogId, containment ?? "{}", fieldId.ToString("D")).ToListAsync(cancellationToken))
        .Select(row => new FormEntryGroupRow(row.Value, row.Count)).ToArray();

    public Task<PagedResult<FormCatalogFilterOptionResponse>> ListPeopleAsync(Guid recipientUserId, FormCatalogFilterDefinition filters,
        FormEntryItemsQuery query, CancellationToken cancellationToken) =>
        FormCatalogFilterSql.WithConnectionAsync(dbContext, async connection =>
        {
            var (conditions, parameters) = FormCatalogFilterSql.Conditions(filters, recipientUserId);
            var textExpression = FormCatalogFilterSql.DisplayExpression(filters.TextColumn!);
            var detailExpression = filters.DetailColumn is null ? "NULL::text" : FormCatalogFilterSql.DisplayExpression(filters.DetailColumn);
            if (query.Ids is { } ids)
            {
                conditions.Add("users.\"Id\" = ANY(@ids)");
                parameters.Add(("ids", ids.ToArray()));
            }
            if (query.Search is not null)
            {
                conditions.Add($"({textExpression} ILIKE @search ESCAPE E'\\\\' OR {detailExpression} ILIKE @search ESCAPE E'\\\\')");
                parameters.Add(("search", LikePatterns.Contains(query.Search)));
            }
            var where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
            var direction = filters.SortDescending ? "DESC" : "ASC";

            await using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM ({FormCatalogFilterSql.UsersSql}) AS users{where}";
            count.CommandTimeout = FormCatalogFilterSql.CommandTimeoutSeconds;
            foreach (var parameter in parameters) FormCatalogFilterSql.AddParameter(count, parameter.Name, parameter.Value);
            var total = Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            await using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT users."Id", {textExpression} AS "Text", {detailExpression} AS "Detail"
                FROM ({FormCatalogFilterSql.UsersSql}) AS users{where}
                ORDER BY {FormCatalogFilterSql.SortExpression(filters.SortColumn!)} {direction}, users."Id" ASC
                OFFSET @offset LIMIT @limit
                """;
            command.CommandTimeout = FormCatalogFilterSql.CommandTimeoutSeconds;
            foreach (var parameter in parameters) FormCatalogFilterSql.AddParameter(command, parameter.Name, parameter.Value);
            FormCatalogFilterSql.AddParameter(command, "offset", (query.Page - 1) * query.PageSize);
            FormCatalogFilterSql.AddParameter(command, "limit", query.PageSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<FormCatalogFilterOptionResponse>(query.PageSize);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            return new PagedResult<FormCatalogFilterOptionResponse>(rows, query.Page, query.PageSize, total);
        }, cancellationToken);

    public Task<FormEntryOutcomeRow?> FindOutcomeAsync(Guid recordId, CancellationToken cancellationToken) =>
        dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.Id == recordId)
            .Select(record => new FormEntryOutcomeRow(record.SubmittedAtUtc, record.State.ToString(),
                dbContext.Set<BusinessFormActionRun>().Any(run => run.RecordId == record.Id && run.Kind == "RequestApproval"
                    && (run.State == FormActionRunState.Waiting || run.State == FormActionRunState.Running || run.State == FormActionRunState.Completed))))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<FormEntryOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormRecord>().AsNoTracking().Where(record => record.EnteredInApp && sourceReceiptIds.Contains(record.SourceReceiptId))
            .Select(record => new FormEntryOriginRow(record.SourceReceiptId, record.ActorUserId,
                dbContext.Users.Where(user => user.Id == record.ActorUserId).Select(user => user.DisplayName).FirstOrDefault(), record.SubmittedAtUtc))
            .ToListAsync(cancellationToken);

    private sealed class GroupSqlRow
    {
        public string? Value { get; init; }
        public int Count { get; init; }
    }
}
