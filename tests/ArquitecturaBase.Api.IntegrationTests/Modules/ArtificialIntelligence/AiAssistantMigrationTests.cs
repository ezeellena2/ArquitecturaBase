using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_and_removing_assistant_tables_preserves_existing_connection_and_business_facts()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var connectionId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        try
        {
            await db.GetService<IMigrator>().MigrateAsync("20261003215957_AddGuidedAiBusinessInformation", Ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "AiConnections" ("Id","Scope","Provider","Model","ProtectedApiKey","Enabled","Revision","CreatedAtUtc")
                VALUES ({connectionId},1,'OpenAI','previous-model','previous-envelope',true,7,{nowUtc});
                INSERT INTO "AiBusinessInformation" ("Id","Scope","Name","Description","Hours","Location","Services","Policies","Faq","Revision","CreatedAtUtc")
                VALUES ({businessId},1,'Previous shop','Description','Hours','Location','Services','Policies','FAQ',9,{nowUtc});
                """, Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.False(await db.Set<AiConversation>().AnyAsync(Ct));
            Assert.False(await db.Set<AiAssistantTurn>().AnyAsync(Ct));
            Assert.False(await db.Set<AiAssistantRequestReceipt>().AnyAsync(Ct));
            Assert.False(await db.Set<AiAssistantToolExecution>().AnyAsync(Ct));
            await AssertPreviousFactsAsync();
            await db.GetService<IMigrator>().MigrateAsync("20261003215957_AddGuidedAiBusinessInformation", Ct);
            await AssertPreviousFactsAsync();
            await db.Database.MigrateAsync(Ct);
            await AssertPreviousFactsAsync();
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }

        async Task AssertPreviousFactsAsync()
        {
            var connection = await db.Set<AiConnection>().AsNoTracking().SingleAsync(Ct);
            Assert.Equal(connectionId, connection.Id);
            Assert.Equal("previous-envelope", connection.ProtectedApiKey);
            Assert.Equal(7, connection.Revision);
            var business = await db.Set<AiBusinessInformation>().AsNoTracking().SingleAsync(Ct);
            Assert.Equal(businessId, business.Id);
            Assert.Equal("Previous shop", business.Name);
            Assert.Equal("FAQ", business.Faq);
            Assert.Equal(9, business.Revision);
        }
    }
}
