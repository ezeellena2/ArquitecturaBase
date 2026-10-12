using System.Data;
using System.Data.Common;
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
    private const int PreviewCommandTimeoutSeconds = 3;

    private const string UsersSql = """
        SELECT users."Id",
               COALESCE(users."DisplayName", '') AS "Name",
               ARRAY(
                   SELECT roles."Name"
                   FROM "AspNetUserRoles" AS user_roles
                   JOIN "AspNetRoles" AS roles ON roles."Id" = user_roles."RoleId"
                   WHERE user_roles."UserId" = users."Id"
                   ORDER BY roles."Name", roles."Id")::text[] AS "Roles",
               ARRAY(
                   SELECT user_roles."RoleId"
                   FROM "AspNetUserRoles" AS user_roles
                   WHERE user_roles."UserId" = users."Id"
                   ORDER BY user_roles."RoleId")::uuid[] AS "RoleIds",
               users."IsActive" AND NOT users."IsDeleted" AS "Active"
        FROM "AspNetUsers" AS users
        WHERE NOT users."IsDeleted"
          AND NULLIF(BTRIM(COALESCE(users."DisplayName", '')), '') IS NOT NULL
        """;

    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string[] Roles { get; init; } = [];
        public Guid[] RoleIds { get; init; } = [];
        public bool Active { get; init; }
    }

    public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken cancellationToken) =>
        WithConnectionAsync(async connection =>
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
        var users = dbContext.Database.SqlQueryRaw<UserRow>(UsersSql).Where(user => user.Active);
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
        WithConnectionAsync(async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT EXISTS (
                    SELECT 1 FROM "AspNetUsers"
                    WHERE "Id" = @userId AND "IsActive" AND NOT "IsDeleted"
                      AND NULLIF(BTRIM(COALESCE("DisplayName", '')), '') IS NOT NULL)
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
                conditions.Add("users.\"Id\" <> @sampleUserId");
                parameters.Add(("sampleUserId", sampleUserId));
            }

            var where = conditions.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", conditions);
            var sortExpression = SortExpression(filters.SortColumn!);
            var direction = filters.SortDescending ? "DESC" : "ASC";
            var textExpression = DisplayExpression(filters.TextColumn!);
            var detailExpression = filters.DetailColumn is null ? "NULL::text" : DisplayExpression(filters.DetailColumn);
            var sql = $"""
                SELECT users."Id", {textExpression} AS "Text", {detailExpression} AS "Detail"
                FROM ({UsersSql}) AS users{where}
                ORDER BY {sortExpression} {direction}, users."Id" ASC
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
            ("name", "contains") => ("users.\"Name\" ILIKE @value ESCAPE E'\\\\'", LikePatterns.Contains(condition.Value!)),
            ("name", "equals") => ("users.\"Name\" = @value", condition.Value!),
            ("role", "has") => ("@value = ANY(users.\"RoleIds\")", Guid.Parse(condition.Value!)),
            ("active", "equals") => ("users.\"Active\" = @value", bool.Parse(condition.Value!)),
            _ => throw new InvalidOperationException("The catalog filter was not validated before compilation."),
        };

    private static string DisplayExpression(string column) => column switch
    {
        "name" => "users.\"Name\"",
        "role" => "array_to_string(users.\"Roles\", ', ') ",
        "active" => "users.\"Active\"::text",
        _ => throw new InvalidOperationException("The catalog display column was not validated before compilation."),
    };

    private static string SortExpression(string column) => column switch
    {
        "name" => "users.\"Name\"",
        "role" => "array_to_string(users.\"Roles\", ', ') ",
        "active" => "users.\"Active\"",
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

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
