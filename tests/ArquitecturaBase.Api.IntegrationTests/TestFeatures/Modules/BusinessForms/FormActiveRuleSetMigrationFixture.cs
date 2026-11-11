using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

// A draft with its active rule set, written through the columns of the schema before DeferActiveRuleSetKey, and the questions of
// the key that keeps the active rule set inside its own definition.
internal sealed record FormActiveRuleSetMigrationFixture(Guid DefinitionId, Guid RuleSetId)
{
    public const string Before = "20261111134134_AddExternalCatalogs";
    public const string Key = "FK_FormDefinitions_BusinessFormRuleSets_Id_ActiveRuleSetId";

    public static async Task<FormActiveRuleSetMigrationFixture> InsertAsync(ApplicationDbContext db, string prefix, DateTime nowUtc, CancellationToken ct)
    {
        var definitionId = Guid.CreateVersion7(); var ruleSetId = Guid.CreateVersion7();
        const string names = """{"es":"Reglas activas"}""";
        const string submit = """{"es":"Enviar"}""";
        const string draft = """{"format":1,"fields":[]}""";
        const string schema = """{"format":1,"questions":[]}""";
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO "FormDefinitions" ("Id", "Name", "NameJson", "Prefix", "Culture", "SubmitLabelJson", "SchemaJson",
                "Revision", "QuestionCount", "PrefixLocked", "LastRecordNumber", "CreatedAtUtc", "IsDeleted", "AppOnly")
            VALUES ({definitionId}, 'Reglas activas', CAST({names} AS jsonb), {prefix}, 'es', CAST({submit} AS jsonb),
                CAST({draft} AS jsonb), 1, 0, FALSE, 0, {nowUtc}, FALSE, FALSE);
            INSERT INTO "BusinessFormRuleSets" ("Id", "DefinitionId", "Revision", "OriginalCulture", "RulesJson", "QuestionSchemaJson",
                "HasApproval", "CreatedAtUtc")
            VALUES ({ruleSetId}, {definitionId}, 1, 'es', CAST('[]' AS jsonb), CAST({schema} AS jsonb), FALSE, {nowUtc});
            UPDATE "FormDefinitions" SET "ActiveRuleSetId" = {ruleSetId} WHERE "Id" = {definitionId};
            """, ct);
        return new(definitionId, ruleSetId);
    }

    /// <summary>Whether the key exists, and whether Postgres checks it at commit.</summary>
    public static async Task<(bool Exists, bool Deferred)> KeyAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var rows = await db.Database.SqlQueryRaw<bool>($"""
            SELECT condeferrable AND condeferred AS "Value" FROM pg_constraint WHERE conname = '{Key}' AND contype = 'f'
            """).ToArrayAsync(ct);
        return (rows.Length == 1, rows.Length == 1 && rows[0]);
    }

    public static Task<bool> IndexExistsAsync(ApplicationDbContext db, CancellationToken ct) =>
        db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_FormDefinitions_Id_ActiveRuleSetId') AS "Value"
            """).SingleAsync(ct);

    // The isolated migrated database has no seed: whoever adds from the library and reads the rules, with the initial role.
    public static async Task<Guid> CreateDesignerAsync(IServiceProvider services, CancellationToken ct)
    {
        var result = await services.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
        {
            var roles = services.GetRequiredService<IRoleRepository>();
            await roles.CreateAsync(SystemRoles.User, null, [], token);
            var roleName = "standard-rules-" + Guid.NewGuid().ToString("N");
            await roles.CreateAsync(roleName, null, [BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage,
                BusinessFormsPermissions.RulesManage], token);
            var users = services.GetRequiredService<IUserRepository>();
            var user = await users.CreateAsync(Email.Create(TestEmails.Unique("standard-rules")).Value, null, false, null, "es", token);
            await users.SetRolesAsync(user.Id, [roleName], token);
            return Result.Success(user.Id);
        }, CommitPolicy.OnSuccess, ct);
        return result.Value;
    }
}
