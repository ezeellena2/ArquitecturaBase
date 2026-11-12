using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Repositories;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// AddAssistantDocumentReading: readings never take the interpretation slot, their receipts count apart, and going back refuses an
// active reading (which the previous index would take for an interpretation) without closing or deleting anything.
[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantDocumentReadingMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PreviousMigration = "20261111220113_AddFormRecordVoidReasonCode";
    private const string DocumentMigration = "20261111233120_AddAssistantDocumentReading";

    [Fact]
    public async Task Readings_keep_their_seals_and_quota_and_an_active_one_refuses_the_downgrade()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_document_reading")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var actor = Guid.CreateVersion7();
        const string channel = "whatsapp-documents";
        var context = new AssistantChannelContext(channel, actor, null, "es", "UTC", new([], []), [], []);
        var readingProfile = JsonSerializer.Serialize(new AiAssistantExecutionProfileSnapshot("fixture-document", 1, "document-read-v1", 1, context)
            { Purpose = AiAssistantPurpose.ReadDocument }, JsonSerializerOptions.Web);
        var interpretationProfile = JsonSerializer.Serialize(new AiAssistantExecutionProfileSnapshot("fixture-model", 1, "capabilities-v1", 1, context),
            JsonSerializerOptions.Web);
        var (readingConversation, reading) = Turn(actor, channel, now, readingProfile, "sealed-document-descriptor");
        var (interpretationConversation, interpretation) = Turn(actor, channel, now, interpretationProfile, "sealed-interpretation");
        Assert.True(interpretation.Claim(Guid.NewGuid(), now.AddMinutes(1)).IsSuccess);
        var readingReceipt = AiAssistantRequestReceipt.Create(actor, reading.RequestId, "reading-fingerprint", "fixture-key", readingConversation.Id,
            reading.Id, now, now.AddDays(1), channel, false, isDocument: true);
        var interpretationReceipt = AiAssistantRequestReceipt.Create(actor, interpretation.RequestId, "interpretation-fingerprint", "fixture-key",
            interpretationConversation.Id, interpretation.Id, now, now.AddDays(1), channel, false);
        try
        {
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            db.Set<AiConversation>().AddRange(readingConversation, interpretationConversation);
            db.Set<AiAssistantTurn>().AddRange(reading, interpretation);
            db.Set<AiAssistantRequestReceipt>().AddRange(readingReceipt, interpretationReceipt);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();

            // A reading and an interpretation are active together in the same scope, a second reading too; a second interpretation is not.
            var (secondReadingConversation, secondReading) = Turn(actor, channel, now, readingProfile, "second-document-descriptor");
            db.Set<AiConversation>().Add(secondReadingConversation);
            db.Set<AiAssistantTurn>().Add(secondReading);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var reservation = await new AiAssistantRepository(db, factory.Clock).GetActiveForActorChannelAsync(actor, channel, null, Ct);
            Assert.Equal(interpretation.Id, reservation?.Id);
            var (duplicateConversation, duplicate) = Turn(actor, channel, now, interpretationProfile, "duplicate-interpretation");
            db.Set<AiConversation>().Add(duplicateConversation);
            db.Set<AiAssistantTurn>().Add(duplicate);
            var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
            var unique = Assert.IsType<PostgresException>(rejected.InnerException);
            Assert.Equal(("23505", "IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId"), (unique.SqlState, unique.ConstraintName));
            db.ChangeTracker.Clear();

            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct));
            Assert.Equal("P0001", downgrade.SqlState);
            Assert.Contains(DocumentMigration, await db.Database.GetAppliedMigrationsAsync(Ct));
            var retained = await db.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == actor)
                .Select(turn => new { turn.Id, turn.State, turn.ProtectedQuestion, turn.ExecutionProfileJson }).ToArrayAsync(Ct);
            Assert.Equal(3, retained.Length);
            Assert.Equal((AiAssistantTurnState.Pending, "sealed-document-descriptor", readingProfile),
                retained.Where(turn => turn.Id == reading.Id).Select(turn => (turn.State, turn.ProtectedQuestion, turn.ExecutionProfileJson)).Single());
            Assert.Equal(AiAssistantTurnState.Running, retained.Single(turn => turn.Id == interpretation.Id).State);

            foreach (var id in new[] { reading.Id, secondReading.Id })
                (await db.Set<AiAssistantTurn>().SingleAsync(turn => turn.Id == id, Ct)).RequestCancellation(now);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);
            Assert.DoesNotContain(DocumentMigration, await db.Database.GetAppliedMigrationsAsync(Ct));

            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var receipts = await db.Set<AiAssistantRequestReceipt>().AsNoTracking().Where(receipt => receipt.OwnerUserId == actor)
                .ToDictionaryAsync(receipt => receipt.Id, Ct);
            Assert.True(receipts[readingReceipt.Id].IsDocument);
            Assert.False(receipts[interpretationReceipt.Id].IsDocument);
            Assert.Equal(("reading-fingerprint", reading.RequestId), (receipts[readingReceipt.Id].Fingerprint, receipts[readingReceipt.Id].RequestId));
            var final = await db.Set<AiAssistantTurn>().AsNoTracking().SingleAsync(turn => turn.Id == reading.Id, Ct);
            Assert.Equal((AiAssistantTurnState.Canceled, readingProfile, "sealed-document-descriptor"), (final.State, final.ExecutionProfileJson, final.ProtectedQuestion));
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }

    private static (AiConversation Conversation, AiAssistantTurn Turn) Turn(Guid actor, string channel, DateTime now, string profile, string question)
    {
        var conversation = AiConversation.Create(actor, now, channel);
        var turn = AiAssistantTurn.Create(actor, conversation.Id, Guid.NewGuid(), 1, question, "es", "UTC", AiProvider.OpenAI,
            "fixture-model", 1, now, now.AddDays(1), channel);
        Assert.True(turn.SealExecutionProfile(profile).IsSuccess);
        return (conversation, turn);
    }
}
