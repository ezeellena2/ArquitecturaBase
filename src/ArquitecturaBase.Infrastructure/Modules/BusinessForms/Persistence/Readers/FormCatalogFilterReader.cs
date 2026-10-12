using System.Data;
using System.Data.Common;
using System.Globalization;
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
    private const int PreviewCommandTimeoutSeconds = 3;

    public Task<IReadOnlyList<string>> ListRoleNamesAsync(CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT DISTINCT roles.role_name AS "RoleName"
                FROM "BusinessFormCatalogUsers" AS users
                CROSS JOIN LATERAL unnest(users."Roles") AS roles(role_name)
                ORDER BY roles.role_name
                """;
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var names = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) names.Add(reader.GetString(0));
            return (IReadOnlyList<string>)names;
        }, cancellationToken);

    public async Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken)
    {
        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        return await WithConnectionAsync(async connection =>
        {
            var countSql = "SELECT COUNT(*) FROM \"BusinessFormCatalogUsers\" WHERE \"Active\" AND \"Name\" <> ''";
            var pageSql = "SELECT \"Id\", \"Name\" FROM \"BusinessFormCatalogUsers\" WHERE \"Active\" AND \"Name\" <> ''";
            if (search is not null)
            {
                countSql += " AND \"Name\" ILIKE @search ESCAPE E'\\\\'";
                pageSql += " AND \"Name\" ILIKE @search ESCAPE E'\\\\'";
            }

            await using var countCommand = connection.CreateCommand();
            countCommand.CommandText = countSql;
            AddSearchParameter(countCommand, search);
            var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            await using var pageCommand = connection.CreateCommand();
            pageCommand.CommandText = pageSql + " ORDER BY \"Name\", \"Id\" LIMIT @limit OFFSET @offset";
            AddSearchParameter(pageCommand, search);
            AddParameter(pageCommand, "limit", request.PageSize);
            AddParameter(pageCommand, "offset", (request.Page - 1) * request.PageSize);
            await using var reader = await pageCommand.ExecuteReaderAsync(cancellationToken);
            var users = new List<FormCatalogFilterSampleUserResponse>();
            while (await reader.ReadAsync(cancellationToken))
                users.Add(new(reader.GetGuid(0), reader.GetString(1)));
            return new PagedResult<FormCatalogFilterSampleUserResponse>(users, request.Page, request.PageSize, count);
        }, cancellationToken);
    }

    public Task<bool> ExistsActiveSampleUserAsync(Guid userId, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM "BusinessFormCatalogUsers"
                    WHERE "Id" = @userId AND "Active")
                """;
            AddParameter(command, "userId", userId);
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
        }, cancellationToken);

    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(
        Guid sampleUserId, FormCatalogFilterDefinition filters, CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
        {
            var conditions = new List<string>();
            var parameters = new List<(string Name, object Value)>();
            var parameterIndex = 0;
            foreach (var condition in filters.Conditions!)
            {
                var parameter = "value" + (parameterIndex++).ToString(CultureInfo.InvariantCulture);
                var (conditionSql, value) = CompileCondition(condition!);
                conditions.Add(conditionSql.Replace("@value", "@" + parameter, StringComparison.Ordinal));
                parameters.Add((parameter, value));
            }

            if (filters.ExcludeRecipient)
            {
                conditions.Add("\"Id\" <> @sampleUserId");
                parameters.Add(("sampleUserId", sampleUserId));
            }

            var where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
            var sortExpression = SortExpression(filters.SortColumn!);
            var direction = filters.SortDescending ? "DESC" : "ASC";
            var textExpression = DisplayExpression(filters.TextColumn!);
            var detailExpression = filters.DetailColumn is null ? "NULL::text" : DisplayExpression(filters.DetailColumn);
            var sql = $"""
                SELECT "Id", {textExpression} AS "Text", {detailExpression} AS "Detail"
                FROM "BusinessFormCatalogUsers"{where}
                ORDER BY {sortExpression} {direction}, "Id" ASC
                LIMIT {PreviewReadLimit}
                """;

            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = PreviewCommandTimeoutSeconds;
            foreach (var parameter in parameters) AddParameter(command, parameter.Name, parameter.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<FormCatalogFilterOptionResponse>(PreviewReadLimit);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));

            if (rows.Count > PreviewLimit)
                return new FormCatalogFilterPreviewResponse([], PreviewReadLimit, ExceedsLimit: true);
            return new FormCatalogFilterPreviewResponse(rows, rows.Count, ExceedsLimit: false);
        }, cancellationToken);

    private static (string Sql, object Value) CompileCondition(FormCatalogFilterCondition condition) =>
        (condition.Column, condition.Operator) switch
        {
            ("name", "contains") => ("\"Name\" ILIKE @value ESCAPE E'\\\\'", LikePatterns.Contains(condition.Value!)),
            ("name", "equals") => ("\"Name\" = @value", condition.Value!),
            ("role", "has") => ("@value = ANY(\"Roles\")", condition.Value!),
            ("active", "equals") => ("\"Active\" = @value", bool.Parse(condition.Value!)),
            _ => throw new InvalidOperationException("The catalog filter was not validated before compilation."),
        };

    private static string DisplayExpression(string column) => column switch
    {
        "name" => "COALESCE(\"Name\", '')",
        "role" => "array_to_string(\"Roles\", ', ') ",
        "active" => "\"Active\"::text",
        _ => throw new InvalidOperationException("The catalog display column was not validated before compilation."),
    };

    private static string SortExpression(string column) => column switch
    {
        "name" => "COALESCE(\"Name\", '')",
        "role" => "array_to_string(\"Roles\", ', ') ",
        "active" => "\"Active\"",
        _ => throw new InvalidOperationException("The catalog sort column was not validated before compilation."),
    };

    private async Task<T> WithConnectionAsync<T>(Func<DbConnection, Task<T>> operation, CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere) await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            return await operation(connection);
        }
        finally
        {
            if (openedHere) await dbContext.Database.CloseConnectionAsync();
        }
    }

    private static void AddSearchParameter(DbCommand command, string? search)
    {
        if (search is not null) AddParameter(command, "search", LikePatterns.Contains(search));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
