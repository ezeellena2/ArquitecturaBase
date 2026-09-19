using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class SoftDeleteTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Deleting_marks_the_row_instead_of_removing_it()
    {
        var id = await CreateWidgetAsync("Borrable");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var deletedAt = factory.Clock.GetUtcNow().UtcDateTime;

        await DeleteWidgetAsync(id);
        var stored = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().IgnoreQueryFilters().AsNoTracking().SingleAsync(w => w.Id == id, Ct));

        Assert.True(stored.IsDeleted);
        Assert.Equal(deletedAt, stored.DeletedAtUtc);
    }

    [Fact]
    public async Task Deleted_rows_are_hidden_from_queries_and_endpoints()
    {
        using var client = factory.CreateClient();
        var id = await CreateWidgetAsync("Oculto");

        await DeleteWidgetAsync(id);
        var visible = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>().AnyAsync(w => w.Id == id, Ct));
        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");

        Assert.False(visible);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_rows_are_found_when_ignoring_only_the_soft_delete_filter_by_name()
    {
        var id = await CreateWidgetAsync("Nombrado");

        await DeleteWidgetAsync(id);
        var visible = await factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Set<Widget>()
                .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
                .AnyAsync(w => w.Id == id, Ct));

        Assert.True(visible);
    }

    private Task<Guid> CreateWidgetAsync(string name) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = new Widget(name);
            dbContext.Add(widget);
            await dbContext.SaveChangesAsync(Ct);
            return widget.Id;
        });

    private Task<int> DeleteWidgetAsync(Guid id) =>
        factory.ExecuteDbContextAsync(async dbContext =>
        {
            var widget = await dbContext.Set<Widget>().SingleAsync(w => w.Id == id, Ct);
            dbContext.Remove(widget);
            return await dbContext.SaveChangesAsync(Ct);
        });
}
