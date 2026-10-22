using System.Data;
using System.Reflection;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormApprovalContinuationQueryTests
{
    [Theory]
    [InlineData("Next", ") > (", "ASC")]
    [InlineData("Previous", ") < (", "DESC")]
    public void Continuation_keeps_the_authorized_cursor_and_open_positions_in_one_snapshot(string direction, string comparison, string order)
    {
        using var db = Database();
        var actor = Guid.NewGuid(); var anchor = Guid.NewGuid(); var definition = Guid.NewGuid();
        using var command = Query(Reader(db), anchor, actor, new(definition, direction, 3)).CreateDbCommand();
        var sql = command.CommandText;
        Assert.Contains("ROW_NUMBER() OVER", sql, StringComparison.Ordinal);
        Assert.Contains("COUNT(*) OVER", sql, StringComparison.Ordinal);
        Assert.Contains("continuation_scope", sql, StringComparison.Ordinal);
        Assert.Contains("continuation_anchor", sql, StringComparison.Ordinal);
        Assert.Contains("continuation_batch", sql, StringComparison.Ordinal);
        Assert.Contains(comparison, sql, StringComparison.Ordinal);
        Assert.Contains("LEFT JOIN", sql, StringComparison.Ordinal);
        Assert.Contains("\"RequestedAtUtc\" " + order, sql, StringComparison.Ordinal);
        Assert.Contains("\"AspNetUserRoles\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"IsActive\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"AuthorUserId\" <>", sql, StringComparison.Ordinal);
        Assert.Contains("\"State\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("OFFSET", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("SchemaJson", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("NameJson", sql, StringComparison.Ordinal);
        Assert.Equal(anchor, command.Parameters["continuation_anchor_id"].Value);
        Assert.Equal(NpgsqlTypes.NpgsqlDbType.Uuid, Assert.IsType<NpgsqlParameter>(command.Parameters["continuation_anchor_id"]).NpgsqlDbType);
        Assert.Equal(3, command.Parameters["continuation_page_size"].Value);
        Assert.Equal(4, command.Parameters["continuation_take"].Value);
        Assert.Contains(command.Parameters.Cast<NpgsqlParameter>(), parameter => Equals(parameter.Value, actor));
        Assert.Contains(command.Parameters.Cast<NpgsqlParameter>(), parameter => Equals(parameter.Value, definition));
        Assert.DoesNotContain(actor.ToString(), sql, StringComparison.Ordinal);
        Assert.DoesNotContain(definition.ToString(), sql, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public void Continuation_bounds_the_batch_before_projecting_author_role_and_decision_metadata()
    {
        using var db = Database();
        using var command = Query(Reader(db), Guid.NewGuid(), Guid.NewGuid(), new(Guid.NewGuid(), "Next", 2)).CreateDbCommand();
        var sql = command.CommandText;
        var batch = sql.IndexOf("continuation_batch AS", StringComparison.Ordinal);
        var metadata = sql.IndexOf("continuation_metadata AS", StringComparison.Ordinal);
        Assert.True(batch >= 0);
        Assert.True(metadata > batch);
        Assert.True(sql.IndexOf("LIMIT @continuation_page_size", StringComparison.Ordinal) < metadata);
        Assert.True(sql.IndexOf("AS \"AuthorName\"", StringComparison.Ordinal) > metadata);
        Assert.Contains("SELECT \"Id\" AS \"Value\" FROM continuation_batch", sql, StringComparison.Ordinal);
    }

    private static ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
    private static FormApprovalReader Reader(ApplicationDbContext db)
    {
        var records = new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider()));
        return new(db, new FormResponseReader(db, records), records);
    }
    private static IQueryable Query(FormApprovalReader reader, Guid anchor, Guid actor, FormApprovalContinuationRequest request)
    {
        var method = typeof(FormApprovalReader).GetMethod("ContinuationQuery", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<IQueryable>(method.Invoke(reader, [anchor, actor, request]));
    }
}