using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

[Collection(ApiTestGroup.Name)]
public sealed class FormMissingRequestReaderTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Assistant_topics_exclude_discarded_rows_before_the_bounded_ordered_read()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_topics")));
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        try
        {
            await db.Database.EnsureCreatedAsync(Ct);
            var discarded = BusinessFormMissingTopic.Create("Discarded topic", "Fictitious discarded request.", "en").Value;
            Assert.True(discarded.Discard(1, Guid.CreateVersion7(), factory.Clock.GetUtcNow().UtcDateTime).IsSuccess);
            var pending = BusinessFormMissingTopic.Create("Pending topic", "Fictitious pending request.", "en").Value;
            db.Set<BusinessFormMissingTopic>().AddRange(discarded, pending);
            await db.SaveChangesAsync(Ct);
            var reader = scope.ServiceProvider.GetRequiredService<IFormMissingRequestReader>();
            var first = Assert.Single(await reader.ListAssistantTopicsAsync(Ct));
            Assert.Equal(pending.Id, first.Id);
            for (var index = 0; index < 100; index++)
                db.Set<BusinessFormMissingTopic>().Add(BusinessFormMissingTopic.Create("Pending topic " + index,
                    "Fictitious pending request for the bounded read.", "en").Value);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var rows = await reader.ListAssistantTopicsAsync(Ct);
            Assert.Equal(101, rows.Count);
            Assert.DoesNotContain(rows, row => row.Id == discarded.Id);
            Assert.Equal(rows.Select(row => row.Id).Order(), rows.Select(row => row.Id));
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
