using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

// An External catalog with PostgreSQL and the worker run by hand: it is created only when its source answers, in its system
// folder; it is not read again until the next day; what stops coming is deactivated; a failure keeps what it had and says why;
// and nobody edits it by hand.
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task A_fed_catalog_is_created_when_its_source_answers_and_then_refreshes_once_a_day()
    {
        var feed = new TestCatalogFeed();
        await using var host = FeedHost(feed);
        await RemoveFedCatalogAsync();
        var worker = host.Services.GetRequiredService<FormCatalogFeedBackgroundService>();
        try
        {
            await worker.ProcessDueAsync(Ct);
            Assert.Null(await FedCatalogAsync());

            feed.Items = [TestCatalogFeed.Item("1", "Rojo"), TestCatalogFeed.Item("2", "Verde")];
            await worker.ProcessDueAsync(Ct);
            var catalog = await FedCatalogAsync();
            Assert.Equal((BusinessFormCatalogKind.External, BusinessFormCatalogValueType.Integer), (catalog!.Kind, catalog.ValueType));
            Assert.Equal([("1", "Rojo", true), ("2", "Verde", true)], (await FedItemsAsync()).Select(item => (item.Code, item.TextEs!, item.IsActive)));
            Assert.True(await factory.ExecuteDbContextAsync(db => db.Set<BusinessFormFolderMembership>().AnyAsync(membership => membership.ItemId == catalog.Id
                && db.Set<BusinessFormFolder>().Any(folder => folder.Id == membership.FolderId && folder.SystemKey == "test-feed"), Ct)));

            var reads = feed.Reads;
            await worker.ProcessDueAsync(Ct);
            Assert.Equal(reads, feed.Reads);

            feed.Items = [TestCatalogFeed.Item("1", "Rojo oscuro")];
            factory.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
            await worker.ProcessDueAsync(Ct);
            Assert.Equal([("1", "Rojo oscuro", true), ("2", "Verde", false)], (await FedItemsAsync()).Select(item => (item.Code, item.TextEs!, item.IsActive)));

            feed.Items = null;
            factory.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));
            await worker.ProcessDueAsync(Ct);
            var failed = await FedCatalogAsync();
            Assert.Equal("test.unavailable", failed!.FeedFailureCode);
            Assert.Single(await FedItemsAsync(), item => item.IsActive);
        }
        finally
        {
            await RemoveFedCatalogAsync();
        }
    }

    [Fact]
    public async Task A_fed_catalog_shows_when_its_source_answered_and_is_not_edited_or_imported_by_hand()
    {
        var feed = new TestCatalogFeed { Items = [TestCatalogFeed.Item("1", "Rojo")] };
        await using var host = FeedHost(feed);
        await RemoveFedCatalogAsync();
        try
        {
            await host.Services.GetRequiredService<FormCatalogFeedBackgroundService>().ProcessDueAsync(Ct);
            var catalog = await FedCatalogAsync();
            using var manager = await OperatorAsync([BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);

            using var detail = await manager.GetAsync($"/api/forms/catalogs/{catalog!.Id}", Ct);
            var body = await detail.ReadJsonAsync();
            Assert.Equal("External", body.GetProperty("kind").GetString());
            Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, body.GetProperty("feed").GetProperty("refreshedAtUtc").GetDateTime());

            using var edited = await manager.PutAsJsonAsync($"/api/forms/catalogs/{catalog.Id}", new
            {
                expectedRevision = catalog.Revision, name = new { es = "Otro" }, description = new { es = "" }, items = Array.Empty<object>(),
            }, Ct);
            Assert.Equal(HttpStatusCode.Conflict, edited.StatusCode);
            Assert.Equal("Forms.Catalog.ReadOnly", (await edited.ReadJsonAsync()).GetProperty("code").GetString());

            using var listed = await manager.GetAsync("/api/forms/catalogs?kind=External", Ct);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            Assert.Contains((await listed.ReadJsonAsync()).GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == catalog.Id);
        }
        finally
        {
            await RemoveFedCatalogAsync();
        }
    }

    private WebApplicationFactory<Program> FeedHost(TestCatalogFeed feed) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IStructuredCatalogFeed>();
        services.AddSingleton<IStructuredCatalogFeed>(feed);
    }));

    private Task<BusinessFormCatalog?> FedCatalogAsync() => factory.ExecuteDbContextAsync(db =>
        db.Set<BusinessFormCatalog>().AsNoTracking().SingleOrDefaultAsync(catalog => catalog.Key == TestCatalogFeed.Key, Ct));

    private Task<BusinessFormCatalogItem[]> FedItemsAsync() => factory.ExecuteDbContextAsync(db => db.Set<BusinessFormCatalogItem>().AsNoTracking()
        .Where(item => db.Set<BusinessFormCatalog>().Any(catalog => catalog.Id == item.CatalogId && catalog.Key == TestCatalogFeed.Key))
        .OrderBy(item => item.Position).ToArrayAsync(Ct));

    private Task RemoveFedCatalogAsync() => factory.ExecuteDbContextAsync(async db =>
    {
        var ids = await db.Set<BusinessFormCatalog>().Where(catalog => catalog.Key == TestCatalogFeed.Key).Select(catalog => catalog.Id).ToArrayAsync(Ct);
        db.Set<BusinessFormFolderMembership>().RemoveRange(await db.Set<BusinessFormFolderMembership>().Where(value => ids.Contains(value.ItemId)).ToArrayAsync(Ct));
        db.Set<BusinessFormCatalogItem>().RemoveRange(await db.Set<BusinessFormCatalogItem>().Where(item => ids.Contains(item.CatalogId)).ToArrayAsync(Ct));
        db.Set<BusinessFormCatalog>().RemoveRange(await db.Set<BusinessFormCatalog>().Where(catalog => ids.Contains(catalog.Id)).ToArrayAsync(Ct));
        await db.SaveChangesAsync(Ct);
    });
}
