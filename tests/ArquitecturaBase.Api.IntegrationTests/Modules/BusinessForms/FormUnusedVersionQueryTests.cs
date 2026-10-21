using System.Reflection;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormUnusedVersionQueryTests
{
    [Fact]
    public void Retirement_translates_one_read_including_deleted_definitions_and_excluding_open_publications()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=not-opened;Username=test;Password=test").Options);
        var type = typeof(FormSelectionReader).Assembly.GetType(
            "ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers.FormUnusedVersionReader");
        Assert.NotNull(type);
        var reader = Activator.CreateInstance(type, db);
        var method = type.GetMethod("Unused", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var query = Assert.IsAssignableFrom<IQueryable<BusinessFormVersion>>(method.Invoke(reader,
            new object[] { new[] { Guid.Parse("019d480a-b0db-7100-8084-010101010101") } }));
        var sql = query.Select(version => new { version.DefinitionId, version.Id }).ToQueryString();
        Assert.Contains("\"DeactivatedAtUtc\" IS NOT NULL", sql, StringComparison.Ordinal);
        Assert.Contains("\"ActiveVersionId\"", sql, StringComparison.Ordinal);
        Assert.Matches("\\.\"IsDeleted\"\\s+OR", sql);
        Assert.DoesNotMatch("NOT\\s+\\(?[a-z][a-z0-9]*\\.\"IsDeleted\"", sql);
        Assert.Contains("'Published'", sql, StringComparison.Ordinal);
        Assert.Contains("'Failed'", sql, StringComparison.Ordinal);
        Assert.Contains("NOT EXISTS", sql, StringComparison.Ordinal);
        Assert.Contains("\"DefinitionId\"", sql, StringComparison.Ordinal);
        Assert.Contains("\"VersionId\"", sql, StringComparison.Ordinal);
    }
}