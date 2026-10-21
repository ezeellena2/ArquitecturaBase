using System.Collections;
using System.Reflection;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace ArquitecturaBase.ArchitectureTests;

public sealed class MigrationSqlPortabilityTests
{
    private static readonly Assembly InfrastructureAssembly = typeof(ApplicationDbContext).Assembly;

    [Fact]
    public void Snapshot_and_migration_models_have_portable_sql_values()
    {
        var violations = new List<string>();
        foreach (var type in InfrastructureAssembly.GetTypes().Where(type => !type.IsAbstract))
        {
            var instance = type.IsSubclassOf(typeof(ModelSnapshot)) || type.IsSubclassOf(typeof(Migration))
                ? Activator.CreateInstance(type) : null;
            var model = instance switch { ModelSnapshot snapshot => snapshot.Model, Migration migration => migration.TargetModel, _ => null };
            if (model is not null) violations.AddRange(ModelViolations(model).Select(path => type.Name + ":" + path));
        }
        Assert.Empty(violations);
    }

    [Fact]
    public void Current_model_matches_the_generated_snapshot_without_a_database()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();
        options.UseNpgsql();
        options.UseOpenIddict<Guid>();
        using var context = new ApplicationDbContext(options.Options);
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Migration_operations_have_portable_sql_values()
    {
        var violations = new List<string>();
        foreach (var type in InfrastructureAssembly.GetTypes().Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Migration))))
        {
            var migration = (Migration)Activator.CreateInstance(type)!;
            migration.ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";
            violations.AddRange(migration.UpOperations.Concat(migration.DownOperations)
                .SelectMany(OperationViolations).Select(path => type.Name + ":" + path));
        }
        Assert.Empty(violations);
    }

    [Theory]
    [InlineData("SELECT 1", false)]
    [InlineData("SELECT 1\n", false)]
    [InlineData("SELECT 1\r\n", true)]
    [InlineData("SELECT 1\r", true)]
    public void The_guard_observes_effective_sql_and_filter_values(string sql, bool invalid)
    {
        Assert.Equal(invalid, OperationViolations(new SqlOperation { Sql = sql }).Any());
        Assert.Equal(invalid, OperationViolations(new CreateIndexOperation { Name = "IX_Portable", Table = "Portable", Columns = ["Id"], Filter = sql }).Any());
        var builder = new ModelBuilder();
        builder.Entity("Portable", entity => { entity.Property<Guid>("Id"); entity.HasIndex("Id").HasFilter(sql); });
        Assert.Equal(invalid, ModelViolations(builder.Model).Any());
    }

    private static IEnumerable<string> ModelViolations(IReadOnlyModel model)
    {
        foreach (var entity in model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
                if (index.GetFilter()?.Contains('\r') == true) yield return entity.Name + ":Filter";
            foreach (var constraint in entity.GetCheckConstraints())
                if (constraint.Sql.Contains('\r')) yield return entity.Name + ":CheckConstraint";
            foreach (var property in entity.GetProperties())
                if (property.GetDefaultValueSql()?.Contains('\r') == true || property.GetComputedColumnSql()?.Contains('\r') == true)
                    yield return entity.Name + ":" + property.Name;
        }
    }

    private static IEnumerable<string> OperationViolations(MigrationOperation operation)
    {
        foreach (var property in operation.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.GetIndexParameters().Length == 0))
        {
            var value = property.GetValue(operation);
            if (value is string sql && (property.Name == "Filter" || property.Name.EndsWith("Sql", StringComparison.Ordinal)) && sql.Contains('\r'))
                yield return operation.GetType().Name + ":" + property.Name;
            if (value is MigrationOperation nested)
                foreach (var violation in OperationViolations(nested)) yield return violation;
            else if (value is IEnumerable collection && value is not string)
                foreach (var child in collection.OfType<MigrationOperation>())
                    foreach (var violation in OperationViolations(child)) yield return violation;
        }
    }
}
