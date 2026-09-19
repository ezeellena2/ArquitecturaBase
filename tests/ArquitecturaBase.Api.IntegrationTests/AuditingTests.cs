using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class AuditingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_sets_created_fields_from_the_clock_and_the_current_user()
    {
        using var client = factory.CreateClient();
        factory.Clock.Advance(TimeSpan.FromHours(1));
        var createdAt = factory.Clock.GetUtcNow().UtcDateTime;
        var userId = Guid.CreateVersion7();

        using var created = await client.SendAsync(
            HttpMethod.Post,
            "/test/widgets",
            JsonContent.Create(new { name = "Auditado" }),
            userId: userId.ToString("D", CultureInfo.InvariantCulture));
        var id = await created.Content.ReadFromJsonAsync<Guid>(Ct);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(createdAt, widget.GetProperty("createdAtUtc").GetDateTime());
        Assert.Equal(userId, widget.GetProperty("createdBy").GetGuid());
        Assert.Equal(JsonValueKind.Null, widget.GetProperty("modifiedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Anonymous_creation_leaves_created_by_empty()
    {
        using var client = factory.CreateClient();

        using var created = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "Anónimo" }));
        var id = await created.Content.ReadFromJsonAsync<Guid>(Ct);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(JsonValueKind.Null, widget.GetProperty("createdBy").ValueKind);
    }

    [Fact]
    public async Task Modifying_sets_modified_fields()
    {
        var id = await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = new Widget("Original");
            dbContext.Add(widget);
            await dbContext.SaveChangesAsync(Ct);
            return widget.Id;
        });
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        var modifiedAt = factory.Clock.GetUtcNow().UtcDateTime;

        await factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = await dbContext.Set<Widget>().SingleAsync(w => w.Id == id, Ct);
            widget.Rename("Renombrado");
            return await dbContext.SaveChangesAsync(Ct);
        });
        var stored = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().AsNoTracking().SingleAsync(w => w.Id == id, Ct));

        Assert.Equal(modifiedAt, stored.ModifiedAtUtc);
        Assert.Null(stored.ModifiedBy);
        Assert.True(stored.CreatedAtUtc < modifiedAt);
    }
}
