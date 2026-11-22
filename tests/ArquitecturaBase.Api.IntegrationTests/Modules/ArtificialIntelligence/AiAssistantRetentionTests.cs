using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
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

    // I20: the purge asks each reason apart, with the batch's order and limit, and keeps the first of their union in that order. One turn
    // per reason, interleaved by expiry (two tied, which the Id breaks) and created backwards, so their Ids do not follow it: a small
    // limit takes exactly the first ones. Its instants come before anything the other tests leave, which never takes those places.
    [Fact]
    public async Task A_small_purge_batch_takes_the_first_turns_of_every_reason_in_expiry_order()
    {
        var nowUtc = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var submitted = nowUtc.AddDays(-2);
        var deleted = await CreateUserAsync("purge-order-deleted");
        var rows = new List<object>();
        var conversations = new List<Guid>();
        AiAssistantTurn Add(Guid owner, string channel, DateTime contentExpiresAtUtc, bool retired = false)
        {
            var conversation = AiConversation.Create(owner, submitted, channel);
            var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC",
                AiProvider.OpenAI, "fictional-model", 1, submitted, contentExpiresAtUtc, channel);
            turn.RequestCancellation(submitted);
            if (retired) conversation.Remove(submitted);
            rows.AddRange([conversation, turn]);
            conversations.Add(conversation.Id);
            return turn;
        }
        var unnamedReading = Add(Guid.NewGuid(), "home-document", nowUtc.AddHours(3));
        var deletedOwner = Add(deleted, "home", nowUtc.AddHours(3));
        var removed = Add(Guid.NewGuid(), "home", nowUtc.AddHours(2), retired: true);
        var unnamedRecording = Add(Guid.NewGuid(), "home-audio", nowUtc.AddHours(1));
        var expired = Add(Guid.NewGuid(), "home", nowUtc.AddDays(-1));
        // PostgreSQL compares a uuid by its bytes, which is the order of its text.
        Guid[] expected = [expired.Id, unnamedRecording.Id, removed.Id,
            .. new[] { deletedOwner.Id, unnamedReading.Id }.OrderBy(id => id.ToString(), StringComparer.Ordinal)];
        try
        {
            await factory.InTransactionAsync(services =>
            {
                services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>().AddRange(rows);
                return Task.CompletedTask;
            });
            await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().DeleteAsync(deleted, Ct));

            for (var limit = 1; limit <= expected.Length; limit++)
            {
                var batch = limit;
                var purgeable = await factory.ExecuteScopeAsync(async services => (await services.GetRequiredService<IAiAssistantRepository>()
                    .ListPurgeableAsync(nowUtc, new(nowUtc.AddDays(-1), "home-audio", "home-document", "ai/assistant/documents/",
                        [".pdf", ".jpg", ".png"]), null, batch, Ct))
                    .Select(turn => turn.Id).ToArray());
                Assert.Equal(expected[..limit], purgeable);
            }
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<AiAssistantTurn>().RemoveRange(await db.Set<AiAssistantTurn>()
                    .IgnoreQueryFilters().Where(row => conversations.Contains(row.ConversationId)).ToArrayAsync(CancellationToken.None));
                db.Set<AiConversation>().RemoveRange(await db.Set<AiConversation>()
                    .IgnoreQueryFilters().Where(row => conversations.Contains(row.Id)).ToArrayAsync(CancellationToken.None));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        }
    }

    private Task<Guid> CreateUserAsync(string prefix) => factory.InTransactionAsync(async services =>
        (await services.GetRequiredService<IUserRepository>().CreateAsync(Email.Create(TestEmails.Unique(prefix)).Value,
            null, false, null, "es", Ct)).Id);

    // The purge goes in batches shared with what earlier scenarios left: it runs until a batch finds nothing to do.
    // RC1-A: the administrator shortened the Home days to 7. What Home saved before goes at the next cleanup, before its original date,
    // with its receipt, so its conversation goes too. A recent Home conversation and a channel's content and receipt of the same age stay.
    [Fact]
    public async Task Shortening_the_home_days_purges_older_home_content_and_removes_its_conversation()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var owner = await factory.InTransactionAsync(async services => (await services.GetRequiredService<IUserRepository>()
            .CreateAsync(Email.Create(TestEmails.Unique("home-days")).Value, null, false, null, "es", Ct)).Id);
        var old = AiConversation.Create(owner, nowUtc.AddDays(-10));
        var oldTurn = AiAssistantTurn.Create(owner, old.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC", AiProvider.OpenAI,
            "fictional-model", 1, nowUtc.AddDays(-10), nowUtc.AddDays(20));
        var oldReceipt = AiAssistantRequestReceipt.Create(owner, oldTurn.RequestId, "fingerprint", "test", old.Id, oldTurn.Id,
            nowUtc.AddDays(-10), nowUtc.AddDays(20), old.ChannelId, false);
        var recent = AiConversation.Create(owner, nowUtc.AddDays(-2));
        var recentTurn = AiAssistantTurn.Create(owner, recent.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC", AiProvider.OpenAI,
            "fictional-model", 1, nowUtc.AddDays(-2), nowUtc.AddDays(28));
        var channel = AiConversation.Create(owner, nowUtc.AddDays(-10), "channel-test");
        var channelTurn = AiAssistantTurn.Create(owner, channel.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC", AiProvider.OpenAI,
            "fictional-model", 1, nowUtc.AddDays(-10), nowUtc.AddDays(20), channel.ChannelId);
        var channelReceipt = AiAssistantRequestReceipt.Create(owner, channelTurn.RequestId, "fingerprint", "test", channel.Id, channelTurn.Id,
            nowUtc.AddDays(-10), nowUtc.AddDays(20), channel.ChannelId, false);
        // Finished turns, as days-old ones are: active work would keep a conversation on its own.
        foreach (var turn in new[] { oldTurn, recentTurn, channelTurn }) turn.RequestCancellation(turn.SubmittedAtUtc.AddMinutes(1));
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>()
                .AddRange(old, oldTurn, oldReceipt, recent, recentTurn, channel, channelTurn, channelReceipt);
            return Task.CompletedTask;
        });

        for (var batch = 0; batch < 50; batch++)
        {
            var result = await factory.ExecuteScopeAsync(services => ActivatorUtilities.CreateInstance<AiAssistantRetentionService>(services,
                new HomeRetentionConnection(7)).PurgeBatchAsync(Ct));
            Assert.True(result.IsSuccess);
            if (result.Value == 0) break;
        }

        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == old.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == oldTurn.Id, Ct));
            Assert.False(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == oldReceipt.Id, Ct));
            Assert.True(await db.Set<AiAssistantRequestReceipt>().AnyAsync(row => row.Id == channelReceipt.Id, Ct));
            var kept = await db.Set<AiAssistantTurn>().IgnoreQueryFilters().Where(row => row.Id == recentTurn.Id || row.Id == channelTurn.Id)
                .ToListAsync(Ct);
            Assert.Equal(2, kept.Count);
            Assert.All(kept, turn => Assert.Equal("protected-question", turn.ProtectedQuestion));
        });
    }

    // RC1-A: shortened to 7 days, a removed Home conversation whose content went before, with the 30-day expiry it was saved with, goes
    // once its turns were accepted before the cut; one accepted after the cut still waits.
    [Fact]
    public async Task Shortening_the_home_days_removes_a_removed_conversation_whose_content_went_before_the_cut()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var owner = await CreateUserAsync("home-days-removed");
        (AiConversation Conversation, AiAssistantTurn Turn) Removed(int daysAgo)
        {
            var conversation = AiConversation.Create(owner, nowUtc.AddDays(-daysAgo));
            var turn = AiAssistantTurn.Create(owner, conversation.Id, Guid.NewGuid(), 1, "protected-question", "es", "UTC", AiProvider.OpenAI,
                "fictional-model", 1, nowUtc.AddDays(-daysAgo), nowUtc.AddDays(30 - daysAgo));
            turn.RequestCancellation(turn.SubmittedAtUtc.AddMinutes(1));
            turn.PurgeContent();
            conversation.Remove(nowUtc.AddHours(-1));
            return (conversation, turn);
        }
        var old = Removed(10);
        var recent = Removed(2);
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>()
                .AddRange(old.Conversation, old.Turn, recent.Conversation, recent.Turn);
            return Task.CompletedTask;
        });

        for (var batch = 0; batch < 50; batch++)
        {
            var result = await factory.ExecuteScopeAsync(services => ActivatorUtilities.CreateInstance<AiAssistantRetentionService>(services,
                new HomeRetentionConnection(7)).PurgeBatchAsync(Ct));
            Assert.True(result.IsSuccess);
            if (result.Value == 0) break;
        }

        await factory.ExecuteDbContextAsync(async db =>
        {
            Assert.False(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == old.Conversation.Id, Ct));
            Assert.False(await db.Set<AiAssistantTurn>().IgnoreQueryFilters().AnyAsync(row => row.Id == old.Turn.Id, Ct));
            Assert.True(await db.Set<AiConversation>().IgnoreQueryFilters().AnyAsync(row => row.Id == recent.Conversation.Id, Ct));
        });
    }

    // I1: a receipt of the business day in course counts in its quotas, so it is not deleted even if it expired or was accepted before the
    // Home cut (one day of retention on the 25-hour day the clocks go back); one accepted before the day started goes.
    [Fact]
    public async Task Receipts_accepted_since_the_business_day_started_are_not_listed_to_delete()
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;
        var dayStartUtc = nowUtc.AddHours(-25);
        var owner = await CreateUserAsync("receipt-day");
        AiAssistantRequestReceipt Receipt(DateTime acceptedAtUtc, DateTime expiresAtUtc) => AiAssistantRequestReceipt.Create(owner, Guid.NewGuid(),
            "fingerprint", "test", Guid.NewGuid(), Guid.NewGuid(), acceptedAtUtc, expiresAtUtc, "home", false);
        var expiredBefore = Receipt(dayStartUtc.AddMinutes(-1), nowUtc.AddMinutes(-1));
        var expiredToday = Receipt(dayStartUtc.AddMinutes(10), nowUtc.AddMinutes(-1));
        var homeBefore = Receipt(dayStartUtc.AddMinutes(-1), nowUtc.AddDays(5));
        var homeToday = Receipt(dayStartUtc.AddMinutes(10), nowUtc.AddDays(5));
        await factory.InTransactionAsync(services =>
        {
            services.GetRequiredService<ArquitecturaBase.Infrastructure.Persistence.ApplicationDbContext>()
                .AddRange(expiredBefore, expiredToday, homeBefore, homeToday);
            return Task.CompletedTask;
        });
        try
        {
            var listed = await factory.ExecuteScopeAsync(async services => (await services.GetRequiredService<IAiAssistantRepository>()
                .ListExpiredReceiptsAsync(nowUtc, dayStartUtc, new(nowUtc.AddDays(-1), ["home", "home-audio", "home-document"]), 100_000, Ct))
                .Select(receipt => receipt.Id).ToHashSet());
            Assert.Contains(expiredBefore.Id, listed);
            Assert.Contains(homeBefore.Id, listed);
            Assert.DoesNotContain(expiredToday.Id, listed);
            Assert.DoesNotContain(homeToday.Id, listed);
        }
        finally
        {
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.RemoveRange(await db.Set<AiAssistantRequestReceipt>().Where(row => row.OwnerUserId == owner).ToArrayAsync(CancellationToken.None));
                await db.SaveChangesAsync(CancellationToken.None);
            });
        }
    }

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
