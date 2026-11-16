using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantRetentionTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Purged_removed_content_keeps_the_receipt_until_its_deduplication_window_expires()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc);
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "es", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc, nowUtc.AddDays(30));
        var receipt = AiAssistantRequestReceipt.Create(conversation.OwnerUserId, turn.RequestId, "fingerprint", "test",
            conversation.Id, turn.Id, nowUtc, nowUtc.AddDays(30), conversation.ChannelId, false);
        conversation.Remove(nowUtc);
        turn.RequestCancellation(nowUtc);
        await factory.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>();
            db.AddRange(conversation, turn, receipt);
            return Task.CompletedTask;
        });
        await PurgeAllAsync();
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().AnyAsync(row => row.Id == conversation.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().AnyAsync(row => row.Id == turn.Id, Ct));
            var durable = await db.Set<AiAssistantTurn>().IgnoreQueryFilters().SingleAsync(row => row.Id == turn.Id, Ct);
            Assert.Null(durable.ProtectedQuestion);
            Assert.Null(durable.ProtectedAnswer);
            Assert.Equal(AiAssistantTurnState.Canceled, durable.State);
            Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
        });
    }

    [Fact]
    public async Task Removed_conversations_are_physically_purged_after_all_turns_expire_without_cascading_receipts()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc.AddDays(-31));
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "es", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc.AddDays(-31), nowUtc.AddDays(-1));
        var receipt = AiAssistantRequestReceipt.Create(conversation.OwnerUserId, turn.RequestId, "fingerprint", "test",
            conversation.Id, turn.Id, nowUtc.AddDays(-29), nowUtc.AddDays(1), conversation.ChannelId, false);
        conversation.Remove(nowUtc.AddDays(-1));
        turn.RequestCancellation(nowUtc.AddDays(-1));
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn, receipt);
            return Task.CompletedTask;
        });
        await PurgeAllAsync();
        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == conversation.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == turn.Id, Ct));
            Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
        });
    }

    [Fact]
    public async Task An_expired_running_lease_with_a_reserved_step_becomes_uncertain_without_regeneration()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var conversation = AiConversation.Create(Guid.NewGuid(), nowUtc.AddMinutes(-3));
        var turn = AiAssistantTurn.Create(conversation.OwnerUserId, conversation.Id, Guid.NewGuid(), 1,
            "protected-question", "en", "UTC", AiProvider.OpenAI, "fictional-model", 1, nowUtc.AddMinutes(-3), nowUtc.AddDays(30));
        Assert.True(turn.Claim(Guid.NewGuid(), nowUtc.AddSeconds(-16)).IsSuccess);
        Assert.True(turn.BeginProviderStep(turn.Fence, nowUtc.AddSeconds(-106), 4).IsSuccess);
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn);
            return Task.CompletedTask;
        });
        await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().ReconcileExpiredBatchAsync(Ct));
        await factory.ExecuteDbContextAsync(async db =>
        {
            var durable = await db.Set<AiAssistantTurn>().SingleAsync(row => row.Id == turn.Id, Ct);
            Assert.Equal(AiAssistantTurnState.Uncertain, durable.State);
            Assert.Equal(1, durable.ProviderSteps);
            Assert.True(durable.UsageUnknown);
        });
        var claim = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantDispatchService>().ClaimAsync(Ct));
        Assert.Null(claim.Value);
    }
    [Theory]
    [InlineData("whatsapp-assistant", false, false, false, true)]
    [InlineData("whatsapp-manual", false, false, false, true)]
    [InlineData("whatsapp-assistant", true, false, false, false)]
    [InlineData("whatsapp-assistant", false, true, false, false)]
    [InlineData("whatsapp-assistant", false, false, true, false)]
    [InlineData("home", false, false, false, true)]
    [InlineData("home", true, false, false, false)]
    [InlineData("home", false, true, false, false)]
    [InlineData("home", false, false, true, false)]
    public async Task Channel_and_home_metadata_wait_for_turns_and_receipts_without_an_explicit_removal(
        string channel, bool liveReceipt, bool liveContent, bool activeTurn, bool removed)
    {
        var owner = await CreateUserAsync("channel-retention");
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var submitted = now.AddDays(-32);
        var conversation = AiConversation.Create(owner, submitted, channel);
        var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
            AiProvider.OpenAI, "fictional-model", 1, submitted, now.AddDays(liveContent ? 1 : -1), channel);
        if (!activeTurn) turn.RequestCancellation(now.AddDays(-2));
        var receipt = AiAssistantRequestReceipt.Create(owner, turn.RequestId, "fingerprint", "test", conversation.Id, turn.Id,
            submitted, now.AddDays(liveReceipt ? 1 : -1), channel, false);
        try
        {
            await factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(conversation, turn, receipt);
                return Task.CompletedTask;
            });
            await PurgeAllAsync();
            await factory.ExecuteDbContextAsync(async db =>
            {
                Assert.Equal(!removed, await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == conversation.Id, Ct));
                Assert.Equal(!removed, await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == turn.Id, Ct));
                if (liveReceipt) Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == receipt.Id, Ct));
            });
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<AiAssistantRequestReceipt>().RemoveRange(await db.Set<AiAssistantRequestReceipt>()
                    .IgnoreQueryFilters().Where(row => row.Id == receipt.Id).ToArrayAsync(CancellationToken.None));
                db.Set<AiAssistantTurn>().RemoveRange(await db.Set<AiAssistantTurn>()
                    .IgnoreQueryFilters().Where(row => row.Id == turn.Id).ToArrayAsync(CancellationToken.None));
                db.Set<AiConversation>().RemoveRange(await db.Set<AiConversation>()
                    .IgnoreQueryFilters().Where(row => row.Id == conversation.Id).ToArrayAsync(CancellationToken.None));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        }
    }

    // I3: a deleted account is left without conversations even once its turns have no content to purge. A channel's content expires with
    // the channel's window, before the receipt: such a conversation is kept by its receipt, and only the account's deletion retires it.
    // A living account, with the same conversation, keeps its own in the same purge.
    [Fact]
    public async Task A_deleted_account_is_left_without_conversations_whose_content_expired_while_a_living_one_keeps_its_own()
    {
        var deleted = await CreateUserAsync("deleted-expired-content");
        var living = await CreateUserAsync("living-expired-content");
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var lastUse = now.AddDays(-20);
        var rows = new List<object>();
        var conversations = new Dictionary<Guid, Guid>();
        foreach (var owner in new[] { deleted, living })
        {
            var conversation = AiConversation.Create(owner, lastUse, "whatsapp-assistant");
            var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
                AiProvider.OpenAI, "fictional-model", 1, lastUse, lastUse.AddDays(1), conversation.ChannelId);
            turn.RequestCancellation(lastUse);
            turn.PurgeContent();
            rows.AddRange([conversation, turn, AiAssistantRequestReceipt.Create(owner, turn.RequestId, "fingerprint", "test", conversation.Id,
                turn.Id, lastUse, lastUse.AddDays(30), conversation.ChannelId, false)]);
            conversations[owner] = conversation.Id;
        }
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(rows);
            return Task.CompletedTask;
        });
        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().DeleteAsync(deleted, Ct));

        await PurgeAllAsync();

        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().AnyAsync(row => row.OwnerUserId == deleted, Ct));
            var retired = await db.Set<AiConversation>().IgnoreQueryFilters().SingleAsync(row => row.Id == conversations[deleted], Ct);
            Assert.Equal(AiConversationState.Removed, retired.State);
            var kept = await db.Set<AiConversation>().SingleAsync(row => row.OwnerUserId == living, Ct);
            Assert.Equal(conversations[living], kept.Id);
            Assert.Equal(AiConversationState.Active, kept.State);
            Assert.Equal(2, await db.Set<AiAssistantRequestReceipt>().CountAsync(row => row.OwnerUserId == deleted || row.OwnerUserId == living, Ct));
        });
    }

    // I3: a deleted account retires its conversations without content while a receipt still lives: Home, once retired, goes when its
    // turns expired, and another channel stays hidden until its receipts expire.
    [Fact]
    public async Task A_deleted_account_retires_its_conversations_without_content_while_a_receipt_still_lives()
    {
        var owner = await CreateUserAsync("deleted-live-receipt");
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var submitted = now.AddDays(-10);
        var rows = new List<object>();
        var conversations = new Dictionary<string, Guid>();
        foreach (var channel in new[] { "home", "whatsapp-assistant" })
        {
            var conversation = AiConversation.Create(owner, submitted, channel);
            var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
                AiProvider.OpenAI, "fictional-model", 1, submitted, now.AddDays(-1), channel);
            turn.RequestCancellation(submitted);
            turn.PurgeContent();
            rows.AddRange([conversation, turn, AiAssistantRequestReceipt.Create(owner, turn.RequestId, "fingerprint", "test", conversation.Id,
                turn.Id, submitted, now.AddDays(20), channel, false)]);
            conversations[channel] = conversation.Id;
        }
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(rows);
            return Task.CompletedTask;
        });

        // While the account exists, the receipts keep both.
        await PurgeAllAsync();
        await factory.ExecuteDbContextAsync(async db =>
            Assert.Equal(2, await db.Set<AiConversation>().CountAsync(row => row.OwnerUserId == owner, Ct)));

        await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().DeleteAsync(owner, Ct));
        await PurgeAllAsync();

        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().AnyAsync(row => row.OwnerUserId == owner, Ct));
            Assert.False(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == conversations["home"], Ct));
            var channel = await db.Set<AiConversation>().IgnoreQueryFilters().SingleAsync(row => row.Id == conversations["whatsapp-assistant"], Ct);
            Assert.Equal(AiConversationState.Removed, channel.State);
            Assert.Equal(2, await db.Set<AiAssistantRequestReceipt>().CountAsync(row => row.OwnerUserId == owner, Ct));
        });
    }

    private Task<Guid> CreateUserAsync(string prefix) => factory.InTransactionAsync(async services =>
        (await services.GetRequiredService<IUserRepository>().CreateAsync(Email.Create(TestEmails.Unique(prefix)).Value,
            null, false, null, "es", Ct)).Id);

    // The purge goes in batches shared with what earlier scenarios left: it runs until a batch finds nothing to do.
    private async Task PurgeAllAsync()
    {
        for (var batch = 0; batch < 50; batch++)
        {
            var result = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IAiAssistantRetentionService>().PurgeBatchAsync(Ct));
            Assert.True(result.IsSuccess);
            if (result.Value == 0) return;
        }
        Assert.Fail("The purge did not finish in 50 batches.");
    }
}
