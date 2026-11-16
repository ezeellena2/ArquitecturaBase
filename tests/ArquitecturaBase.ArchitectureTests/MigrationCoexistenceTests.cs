using System.Reflection;
using System.Text.RegularExpressions;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ArquitecturaBase.ArchitectureTests;

/// <summary>
/// While a deployment rolls out, the previous image runs against the new schema ([migration guide](docs/guides/migracion.md#convivencia-de-versiones)).
/// A migration after <c>BaselineMigration</c> may not do what that image cannot survive: a CHECK that mentions a column the image already
/// writes, a column that starts accepting nulls, a column dropped together with its replacement, or a rename. What is already applied is the baseline.
/// </summary>
public sealed partial class MigrationCoexistenceTests
{
    /// <summary>The last migration applied before the rule existed: later ones are checked.</summary>
    private const string BaselineMigration = "20261116130834";

    /// <summary>Migration id, then why the previous version still works. Empty on purpose: the next deployment is the way.</summary>
    private static readonly Dictionary<string, string> Acknowledged = [];

    [Fact]
    public void Migrations_after_the_baseline_tolerate_the_previous_version()
    {
        var violations = new List<string>();
        foreach (var type in typeof(ApplicationDbContext).Assembly.GetTypes().Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Migration))))
        {
            var id = type.GetCustomAttribute<MigrationAttribute>()!.Id;
            if (string.CompareOrdinal(id, BaselineMigration) <= 0 || Acknowledged.ContainsKey(id)) continue;
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
            violations.AddRange(Violations(migration.UpOperations).Select(value => id + ": " + value));
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void Acknowledged_migrations_say_why_and_exist()
    {
        var ids = typeof(ApplicationDbContext).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Migration))).Select(type => type.GetCustomAttribute<MigrationAttribute>()!.Id).ToHashSet();
        Assert.All(Acknowledged, pair => { Assert.Contains(pair.Key, ids); Assert.False(string.IsNullOrWhiteSpace(pair.Value)); });
    }

    [Fact]
    public void A_check_that_ties_a_new_column_to_an_old_one_is_found()
    {
        var violations = Violations([
            new AddColumnOperation { Table = "Documents", Name = "Reason" },
            new AddCheckConstraintOperation { Table = "Documents", Name = "CK_Documents_Reason", Sql = "\"NotSendable\" = (\"Reason\" IS NOT NULL)" }]);
        Assert.Single(violations);
    }

    [Fact]
    public void A_check_that_only_mentions_new_columns_or_a_new_table_is_allowed()
    {
        Assert.Empty(Violations([
            new AddColumnOperation { Table = "Documents", Name = "Row" },
            new AddCheckConstraintOperation { Table = "Documents", Name = "CK_Documents_Row", Sql = "\"Row\" IS NULL OR \"Row\" >= 1" },
            new CreateTableOperation { Name = "Notes" },
            new AddCheckConstraintOperation { Table = "Notes", Name = "CK_Notes_Text", Sql = "\"Id\" IS NOT NULL" }]));
    }

    [Fact]
    public void A_check_on_old_columns_alone_a_raw_check_a_new_null_a_replaced_column_and_a_rename_are_found()
    {
        Assert.Single(Violations([new AddCheckConstraintOperation { Table = "Documents", Name = "CK", Sql = "\"State\" IN ('A')" }]));
        Assert.Single(Violations([new SqlOperation { Sql = "ALTER TABLE \"Documents\" ADD CONSTRAINT \"CK\" CHECK (\"A\" > 0);" }]));
        Assert.Single(Violations([new AlterColumnOperation { Table = "Documents", Name = "Path", IsNullable = true, OldColumn = new AddColumnOperation { IsNullable = false } }]));
        Assert.Single(Violations([new DropColumnOperation { Table = "Documents", Name = "Old" }, new AddColumnOperation { Table = "Documents", Name = "New" }]));
        Assert.Single(Violations([new RenameColumnOperation { Table = "Documents", Name = "Old", NewName = "New" }]));
        Assert.Empty(Violations([new AlterColumnOperation { Table = "Documents", Name = "Path", IsNullable = false, OldColumn = new AddColumnOperation { IsNullable = true } }]));
    }

    private static IEnumerable<string> Violations(IReadOnlyList<MigrationOperation> operations)
    {
        var created = operations.OfType<CreateTableOperation>().Select(value => value.Name).ToHashSet();
        var added = operations.OfType<AddColumnOperation>().Where(value => !created.Contains(value.Table)).Select(value => (value.Table, value.Name)).ToHashSet();
        foreach (var check in operations.OfType<AddCheckConstraintOperation>().Where(value => !created.Contains(value.Table)))
        {
            var columns = Columns().Matches(check.Sql).Select(match => match.Groups[1].Value).ToHashSet();
            if (columns.Any(column => !added.Contains((check.Table, column))))
                yield return $"{check.Name} adds a CHECK on {check.Table} that mentions a column the previous version already writes; add it in the next deployment";
        }
        foreach (var sql in operations.OfType<SqlOperation>().Where(value => RawCheck().IsMatch(value.Sql)))
            yield return "raw SQL adds a CHECK; use AddCheckConstraint in the next deployment";
        foreach (var alter in operations.OfType<AlterColumnOperation>().Where(value => value.IsNullable && value.OldColumn.IsNullable == false))
            yield return $"{alter.Table}.{alter.Name} starts accepting nulls; the previous version must tolerate the null before a version writes it";
        foreach (var drop in operations.OfType<DropColumnOperation>().Where(drop => added.Any(column => column.Table == drop.Table)))
            yield return $"{drop.Table}.{drop.Name} is dropped together with a new column; drop it in the next deployment";
        foreach (var rename in operations.OfType<RenameColumnOperation>()) yield return $"{rename.Table}.{rename.Name} is renamed; add the new one and drop the old one in two deployments";
        foreach (var rename in operations.OfType<RenameTableOperation>()) yield return $"{rename.Name} is renamed; add the new one and drop the old one in two deployments";
    }

    [GeneratedRegex("\"([^\"]+)\"")] private static partial Regex Columns();
    [GeneratedRegex(@"ADD\s+CONSTRAINT[\s\S]*\bCHECK\s*\(", RegexOptions.IgnoreCase)] private static partial Regex RawCheck();
}
