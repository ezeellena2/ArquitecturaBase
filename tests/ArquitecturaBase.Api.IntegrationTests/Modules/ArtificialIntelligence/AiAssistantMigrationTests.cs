using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

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

    [Fact]
    public async Task Channel_scope_migration_preserves_historical_turns_and_refuses_a_lossy_downgrade()
    {
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_channel_migration")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var owner = Guid.CreateVersion7();
        var conversations = new[] { AiConversation.Create(owner, now, "fixture-first"), AiConversation.Create(owner, now, "fixture-second") };
        var turns = conversations.Select(conversation => AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1,
            "historical-encrypted-question", "es", "UTC", AiProvider.OpenAI, "historical-model", 7, now, now.AddDays(1), conversation.ChannelId)).ToArray();
        try
        {
            await db.Database.MigrateAsync(Ct);
            db.Set<AiConversation>().AddRange(conversations);
            db.Set<AiAssistantTurn>().AddRange(turns);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>()
                .MigrateAsync("20261017233619_AddFormAssistantSuggestionsAndAudio", Ct));
            Assert.Equal("P0001", rejected.SqlState);
            var second = await db.Set<AiAssistantTurn>().SingleAsync(turn => turn.Id == turns[1].Id, Ct);
            Assert.Equal("fixture-second", second.ChannelId);
            second.RequestCancellation(now);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync("20261017233619_AddFormAssistantSuggestionsAndAudio", Ct);
            var historicalContent = await db.Database.SqlQuery<string>($"""SELECT "ProtectedQuestion" AS "Value" FROM "AiAssistantTurns" WHERE "Id" = {turns[0].Id}""").SingleAsync(Ct);
            Assert.Equal("historical-encrypted-question", historicalContent);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var migrated = await db.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == owner).ToArrayAsync(Ct);
            Assert.Equal(2, migrated.Length);
            foreach (var original in turns)
            {
                var turn = Assert.Single(migrated, row => row.Id == original.Id);
                Assert.Equal(original.ChannelId, turn.ChannelId);
                Assert.Equal(original.RequestId, turn.RequestId);
                Assert.Equal("historical-encrypted-question", turn.ProtectedQuestion);
                Assert.Null(turn.ExecutionProfileJson);
            }
            var duplicateConversation = AiConversation.Create(owner, now, turns[0].ChannelId);
            db.Set<AiConversation>().Add(duplicateConversation);
            db.Set<AiAssistantTurn>().Add(AiAssistantTurn.Create(owner, duplicateConversation.Id, Guid.NewGuid(), 1,
                "fictitious duplicate", "es", "UTC", AiProvider.OpenAI, "historical-model", 7, now, now.AddDays(1), turns[0].ChannelId));
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            var constraint = Assert.IsType<PostgresException>(duplicate.InnerException);
            Assert.Equal("23505", constraint.SqlState);
            Assert.Equal("IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId", constraint.ConstraintName);
        }
        finally { await db.Database.EnsureDeletedAsync(Ct); }
    }
}
