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

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantAudioReservationMigrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PreviousMigration = "20261019001407_AddFormRecordPresentationCulture";
    private const string AudioMigration = "20261019180252_ExcludeAudioFromActiveChannelUniqueness";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audio_reservations_preserve_seals_and_refuse_to_downgrade_unrepresentable_active_work(bool withInterpretation)
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("ConnectionStrings:appdb", factory.NewDatabaseConnectionString("assistant_audio_reservations")));
        await using var scope = host.Services.CreateAsyncScope();
        await using var db = new ApplicationDbContext(scope.ServiceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>());
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var actor = Guid.CreateVersion7();
        var channel = "whatsapp-manual";
        var context = new AssistantChannelContext(channel, actor, null, "es", "UTC", new([], []), [], []);
        var audioProfile = new AiAssistantExecutionProfileSnapshot("fixture-audio", 1, "audio-none-v1", 1, context)
            { Purpose = AiAssistantPurpose.NormalizeAudio };
        var profileJson = JsonSerializer.Serialize(audioProfile, JsonSerializerOptions.Web);
        var firstConversation = AiConversation.Create(actor, now, channel);
        var first = AiAssistantTurn.Create(actor, firstConversation.Id, Guid.NewGuid(), 1, "sealed-audio-question", "es", "UTC",
            AiProvider.OpenAI, "fixture-audio", 1, now, now.AddDays(1), channel);
        Assert.True(first.SealExecutionProfile(profileJson).IsSuccess);
        var legacyConversation = AiConversation.Create(actor, now, "legacy-channel");
        var legacy = AiAssistantTurn.Create(actor, legacyConversation.Id, Guid.NewGuid(), 1, "legacy-question", "es", "UTC",
            AiProvider.OpenAI, "legacy-model", 1, now, now.AddDays(1), "legacy-channel");
        Assert.True(legacy.SealExecutionProfile("not-json").IsSuccess);
        try
        {
            await db.Database.MigrateAsync(Ct);
            db.Set<AiConversation>().AddRange(firstConversation, legacyConversation);
            db.Set<AiAssistantTurn>().AddRange(first, legacy);
            var receipt = AiAssistantRequestReceipt.Create(actor, first.RequestId, "sealed-fingerprint", "fixture-key",
                firstConversation.Id, first.Id, now, now.AddDays(1), channel, true);
            db.Set<AiAssistantRequestReceipt>().Add(receipt);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);

            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var secondConversation = AiConversation.Create(actor, now, channel);
            var second = AiAssistantTurn.Create(actor, secondConversation.Id, Guid.NewGuid(), 1, "second-question", "es", "UTC",
                AiProvider.OpenAI, "fixture-model", 1, now, now.AddDays(1), channel);
            var secondProfile = withInterpretation
                ? audioProfile with { Purpose = AiAssistantPurpose.Interpret, InstructionsId = "capabilities-v1" }
                : audioProfile;
            Assert.True(second.SealExecutionProfile(JsonSerializer.Serialize(secondProfile, JsonSerializerOptions.Web)).IsSuccess);
            Assert.True(second.Claim(Guid.NewGuid(), now.AddMinutes(1)).IsSuccess);
            db.Set<AiConversation>().Add(secondConversation);
            db.Set<AiAssistantTurn>().Add(second);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            var reservation = await new AiAssistantRepository(db, factory.Clock).GetActiveForActorChannelAsync(actor, channel, null, Ct);
            if (withInterpretation) { Assert.NotNull(reservation); Assert.Equal(second.Id, reservation.Id); }
            else Assert.Null(reservation);

            if (withInterpretation)
            {
                var duplicateConversation = AiConversation.Create(actor, now, channel);
                var duplicate = AiAssistantTurn.Create(actor, duplicateConversation.Id, Guid.NewGuid(), 1, "duplicate-question", "es", "UTC",
                    AiProvider.OpenAI, "fixture-model", 1, now, now.AddDays(1), channel);
                Assert.True(duplicate.SealExecutionProfile(JsonSerializer.Serialize(secondProfile, JsonSerializerOptions.Web)).IsSuccess);
                db.Set<AiConversation>().Add(duplicateConversation);
                db.Set<AiAssistantTurn>().Add(duplicate);
                var rejected = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(Ct));
                var unique = Assert.IsType<PostgresException>(rejected.InnerException);
                Assert.Equal("23505", unique.SqlState);
                Assert.Equal("IX_AiAssistantTurns_OwnerUserId_ChannelId_ChannelConversationId", unique.ConstraintName);
                db.ChangeTracker.Clear();
            }

            var downgrade = await Assert.ThrowsAsync<PostgresException>(() => db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct));
            Assert.Equal("P0001", downgrade.SqlState);
            Assert.Contains(AudioMigration, await db.Database.GetAppliedMigrationsAsync(Ct));
            var retained = await db.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == actor)
                .Select(turn => new { turn.Id, turn.State, turn.ProtectedQuestion, turn.ExecutionProfileJson }).ToArrayAsync(Ct);
            Assert.Equal(3, retained.Length);
            Assert.Equal(AiAssistantTurnState.Pending, retained.Single(turn => turn.Id == first.Id).State);
            Assert.Equal(AiAssistantTurnState.Running, retained.Single(turn => turn.Id == second.Id).State);
            Assert.Equal("legacy-question", retained.Single(turn => turn.Id == legacy.Id).ProtectedQuestion);
            Assert.Equal("not-json", retained.Single(turn => turn.Id == legacy.Id).ExecutionProfileJson);
            Assert.Equal(profileJson, retained.Single(turn => turn.Id == first.Id).ExecutionProfileJson);
            Assert.Equal("sealed-audio-question", retained.Single(turn => turn.Id == first.Id).ProtectedQuestion);

            await db.Database.MigrateAsync(Ct);
            var toCancel = await db.Set<AiAssistantTurn>().SingleAsync(turn => turn.Id == first.Id, Ct);
            toCancel.RequestCancellation(now);
            await db.SaveChangesAsync(Ct);
            db.ChangeTracker.Clear();
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, Ct);
            await db.Database.MigrateAsync(Ct);
            Assert.False(db.Database.HasPendingModelChanges());
            var final = await db.Set<AiAssistantTurn>().AsNoTracking().SingleAsync(turn => turn.Id == first.Id, Ct);
            Assert.Equal(AiAssistantTurnState.Canceled, final.State);
            Assert.Equal(profileJson, final.ExecutionProfileJson);
            Assert.Equal("sealed-audio-question", final.ProtectedQuestion);
            var retainedReceipt = await db.Set<AiAssistantRequestReceipt>().AsNoTracking().SingleAsync(row => row.Id == receipt.Id, Ct);
            Assert.Equal("sealed-fingerprint", retainedReceipt.Fingerprint);
            Assert.Equal(first.RequestId, retainedReceipt.RequestId);
            Assert.True(retainedReceipt.IsAudio);
            Assert.Equal(channel, retainedReceipt.ChannelId);
        }
        finally { await db.Database.EnsureDeletedAsync(CancellationToken.None); }
    }
}
