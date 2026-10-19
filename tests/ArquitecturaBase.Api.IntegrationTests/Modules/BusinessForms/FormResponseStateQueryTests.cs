using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Security;
using Microsoft.AspNetCore.DataProtection;
using System.Reflection;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormResponseStateQueryTests
{
    [Theory]
    [InlineData("Received")]
    [InlineData("PendingApproval")]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void The_selected_state_is_a_server_side_parameterized_predicate(string state)
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var reader = new FormResponseReader(db, new FormRecordReader(db, new DataProtectionFormPublicationProtector(new EphemeralDataProtectionProvider())));
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{}", "{\"format\":1,\"questions\":[]}", "es", ["es", "en"]);
        var definition = new FormResponseDefinitionRow(Guid.NewGuid(), "TEST", version.Id, [version]);
        var request = JsonSerializer.Deserialize<ListFormRecordsRequest>(JsonSerializer.Serialize(new { state }), JsonSerializerOptions.Web)!;
        var filtering = typeof(FormResponseReader).GetMethod("Filter", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormRecord>>(filtering.Invoke(reader, [definition, request, null]));
        var sql = query.Select(record => record.Id).ToQueryString();
        var statement = sql[sql.IndexOf("SELECT", StringComparison.Ordinal)..];
        Assert.Contains("\"State\" = @", statement, StringComparison.Ordinal);
        Assert.DoesNotContain("'" + state + "'", statement, StringComparison.Ordinal);
        Assert.Contains("'" + state + "'", sql, StringComparison.Ordinal);
    }
}
