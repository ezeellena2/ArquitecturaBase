using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantChannelQuotaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Another_channel_does_not_consume_the_current_channels_daily_quota()
    {
        var f = new AssistantOperationsFixture();
        await FillAsync(f, 10, "another-channel", false);
        Assert.True((await f.Service.SubmitAsync(f.Request(), Ct)).IsSuccess);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Audio_and_interpretation_have_independent_daily_acceptance_quotas(bool audioFirst)
    {
        var f = new AssistantOperationsFixture(audioQualified: true);
        await FillAsync(f, 10, f.Context.ChannelId, audioFirst);
        Assert.True((await f.Service.SubmitAsync(Request(f, f.Context.ChannelId, !audioFirst), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Removing_content_does_not_release_the_daily_quota_for_its_original_channel()
    {
        var f = new AssistantOperationsFixture();
        await FillAsync(f, 10, f.Context.ChannelId, false);
        f.Store.Turns.Clear(); f.Store.Conversations.Clear();
        var blocked = await f.Service.SubmitAsync(f.Request(), Ct);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
        Assert.True((await f.Service.SubmitAsync(Request(f, "another-channel", false), Ct)).IsSuccess);
    }

    [Fact]
    public async Task An_identical_accepted_uuid_is_recovered_after_its_channel_quota_is_exhausted()
    {
        var f = new AssistantOperationsFixture(); var request = f.Request();
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.True((await f.Service.CancelAsync(request.Context, request.RequestId, Ct)).IsSuccess);
        await FillAsync(f, 9, f.Context.ChannelId, false);
        var before = f.Store.BudgetReads;
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.Equal(before, f.Store.BudgetReads);
        Assert.Equal(10, f.Store.Receipts.Count);
    }

    [Fact]
    public async Task Installation_acceptance_quota_still_counts_all_channels_and_audio()
    {
        var f = new AssistantOperationsFixture(audioQualified: true);
        for (var i = 0; i < 10; i++) await FillAsync(f, 10, "channel-" + i, i % 2 == 0);
        var blocked = await f.Service.SubmitAsync(f.Request(), Ct);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
        Assert.Equal(100, f.Store.Receipts.Count);
    }

    [Fact]
    public async Task Home_acceptances_do_not_consume_a_channels_daily_quota()
    {
        var f = new AssistantOperationsFixture(); var now = f.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 10; i++) f.Store.Receipts.Add(AiAssistantRequestReceipt.Create(f.ActorId, Guid.NewGuid(),
            "fictional-fingerprint", "fixture-key", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), "home", false));
        Assert.True((await f.Service.SubmitAsync(f.Request(), Ct)).IsSuccess);
    }

    private static async Task FillAsync(AssistantOperationsFixture f, int count, string channel, bool audio)
    {
        for (var i = 0; i < count; i++)
        {
            var request = Request(f, channel, audio);
            Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
            Assert.True((await f.Service.CancelAsync(request.Context, request.RequestId, Ct)).IsSuccess);
        }
    }

    private static AssistantOperationRequest Request(AssistantOperationsFixture f, string channel, bool audio)
    {
        var request = f.Request() with { Context = f.Context with { ChannelId = channel } };
        return !audio ? request : request with { Text = "", Context = request.Context with { AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("audio/private-object", "audio.ogg", "audio/ogg", 42, new string('a', 64), request.ContentExpiresAtUtc) };
    }
}
