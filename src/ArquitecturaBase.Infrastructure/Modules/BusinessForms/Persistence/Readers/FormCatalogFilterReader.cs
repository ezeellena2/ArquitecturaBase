using System.Globalization;
using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

internal sealed class FormCatalogFilterReader(ApplicationDbContext dbContext) : IFormCatalogFilterReader
{
    private const int PreviewLimit = 200;
    private const int PreviewReadLimit = PreviewLimit + 1;
    private const int PreviewCommandTimeoutSeconds = FormCatalogFilterSql.CommandTimeoutSeconds;

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string[] Roles { get; init; } = [];
        public Guid[] RoleIds { get; init; } = [];
        public bool Active { get; init; }
    }

    public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken cancellationToken) =>
        FormCatalogFilterSql.WithConnectionAsync(dbContext, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT "Id", "Name"
                FROM "AspNetRoles"
                WHERE "Name" IS NOT NULL
                ORDER BY "Name", "Id"
                """;
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var roles = new List<FormCatalogFilterChoiceResponse>();
            while (await reader.ReadAsync(cancellationToken))
                roles.Add(new(reader.GetGuid(0).ToString("D", CultureInfo.InvariantCulture), reader.GetString(1)));
            return (IReadOnlyList<FormCatalogFilterChoiceResponse>)roles;
        }, cancellationToken);

    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken)
    {
        var users = dbContext.Database.SqlQueryRaw<UserRow>(FormCatalogFilterSql.UsersSql).Where(user => user.Active);
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var pattern = LikePatterns.Contains(request.Search.Trim());
            users = users.Where(user => EF.Functions.ILike(user.Name, pattern, LikePatterns.EscapeCharacter));
        }

        var sortable = new Dictionary<string, Expression<Func<UserRow, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = user => user.Name,
        };
        return users.ApplySort(SortDescriptor.Parse(request.Sort), sortable, new("name", false), user => user.Id)
            .Select(user => new FormCatalogFilterSampleUserResponse(user.Id, user.Name))
            .ToPagedResultAsync(request, cancellationToken);
    }

    public Task<bool> ExistsActiveSampleUserAsync(Guid userId, CancellationToken cancellationToken) =>
        FormCatalogFilterSql.WithConnectionAsync(dbContext, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM "AspNetUsers"
                    WHERE "Id" = @userId AND "IsActive" AND NOT "IsDeleted"
                      AND NULLIF(BTRIM(COALESCE("DisplayName", '')), '') IS NOT NULL)
                """;
            FormCatalogFilterSql.AddParameter(command, "userId", userId);
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }, cancellationToken);

    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(
        Guid sampleUserId, FormCatalogFilterDefinition filters, CancellationToken cancellationToken) =>
        FormCatalogFilterSql.WithConnectionAsync(dbContext, async connection =>
        {
            var (conditions, parameters) = FormCatalogFilterSql.Conditions(filters, sampleUserId);
            var where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
            var sortExpression = FormCatalogFilterSql.SortExpression(filters.SortColumn!);
            var direction = filters.SortDescending ? "DESC" : "ASC";
            var textExpression = FormCatalogFilterSql.DisplayExpression(filters.TextColumn!);
            var detailExpression = filters.DetailColumn is null ? "NULL::text" : FormCatalogFilterSql.DisplayExpression(filters.DetailColumn);
            var sql = $"""
                SELECT users."Id", {textExpression} AS "Text", {detailExpression} AS "Detail"
                FROM ({FormCatalogFilterSql.UsersSql}) AS users{where}
                ORDER BY {sortExpression} {direction}, users."Id" ASC
                LIMIT {PreviewReadLimit}
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            foreach (var parameter in parameters) FormCatalogFilterSql.AddParameter(command, parameter.Name, parameter.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<FormCatalogFilterOptionResponse>(PreviewReadLimit);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));

            if (rows.Count > PreviewLimit)
                return new FormCatalogFilterPreviewResponse([], PreviewReadLimit, ExceedsLimit: true);
            return new FormCatalogFilterPreviewResponse(rows, rows.Count, ExceedsLimit: false);
        }, cancellationToken);
}
