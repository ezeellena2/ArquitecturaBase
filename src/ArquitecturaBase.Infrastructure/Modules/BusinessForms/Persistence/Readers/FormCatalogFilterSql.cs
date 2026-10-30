using System.Data;
using System.Data.Common;
using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

/// <summary>The fixed SQL of a Filters catalog over Identity: its people, the allowed conditions and the shown columns.</summary>
internal static class FormCatalogFilterSql
{
    public const int CommandTimeoutSeconds = 3;

    public const string UsersSql = """
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

    /// <summary>The catalog's conditions as SQL with their parameters; excluding the recipient compares with <paramref name="recipientUserId"/>.</summary>
    public static (List<string> Conditions, List<(string Name, object Value)> Parameters) Conditions(FormCatalogFilterDefinition filters, Guid recipientUserId)
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
            parameters.Add(("sampleUserId", recipientUserId));
        }
        return (conditions, parameters);
    }

    public static (string Sql, object Value) CompileCondition(FormCatalogFilterCondition condition) =>
        (condition.Column, condition.Operator) switch
        {
            ("name", "contains") => ("users.\"Name\" ILIKE @value ESCAPE E'\\\\'", LikePatterns.Contains(condition.Value!)),
            ("name", "equals") => ("users.\"Name\" = @value", condition.Value!),
            ("role", "has") => ("@value = ANY(users.\"RoleIds\")", Guid.Parse(condition.Value!)),
            ("active", "equals") => ("users.\"Active\" = @value", bool.Parse(condition.Value!)),
            _ => throw new InvalidOperationException("The catalog filter was not validated before compilation."),
        };

    public static string DisplayExpression(string column) => column switch
    {
        "name" => "users.\"Name\"",
        "role" => "array_to_string(users.\"Roles\", ', ') ",
        "active" => "users.\"Active\"::text",
        _ => throw new InvalidOperationException("The catalog display column was not validated before compilation."),
    };

    public static string SortExpression(string column) => column switch
    {
        "name" => "users.\"Name\"",
        "role" => "array_to_string(users.\"Roles\", ', ') ",
        "active" => "users.\"Active\"",
        _ => throw new InvalidOperationException("The catalog sort column was not validated before compilation."),
    };

    public static async Task<T> WithConnectionAsync<T>(ApplicationDbContext dbContext, Func<DbConnection, Task<T>> operation, CancellationToken cancellationToken)
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

    public static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
