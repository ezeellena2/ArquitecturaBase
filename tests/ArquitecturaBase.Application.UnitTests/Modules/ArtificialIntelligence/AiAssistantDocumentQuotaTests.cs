using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Readings have their own two quotas, per person across every channel and per installation, and stay out of the quotas of
// interpretations and audio: neither the actor's by channel nor the installation's global one counts them.
public sealed class AiAssistantDocumentQuotaTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Readings_count_per_person_across_every_channel()
    {
        var f = new AssistantOperationsFixture(documentQualified: true);
        await FillAsync(f, 10, "channel-a");

        var blocked = await f.Service.SubmitAsync(AiAssistantDocumentOperationsTests.DocumentRequest(f, "channel-b"), Ct);

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
        Assert.Equal(10, f.Store.Receipts.Count);
    }

    [Fact]
    public async Task Interpretations_and_audio_of_the_same_channel_do_not_count_readings()
    {
        var f = new AssistantOperationsFixture(audioQualified: true, documentQualified: true);
        await FillAsync(f, 10, f.Context.ChannelId);

        Assert.True((await f.Service.SubmitAsync(f.Request(), Ct)).IsSuccess);
        Assert.True((await f.Service.SubmitAsync(AiAssistantAudioOperationsRequests.Audio(f), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Interpretations_and_audio_do_not_consume_the_reading_quota()
    {
        var f = new AssistantOperationsFixture(documentQualified: true);
        var now = f.Clock.GetUtcNow().UtcDateTime;
        foreach (var audio in new[] { false, true })
            for (var i = 0; i < 10; i++) f.Store.Receipts.Add(AiAssistantRequestReceipt.Create(f.ActorId, Guid.NewGuid(), "fictional-fingerprint",
                "fixture-key", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), f.Context.ChannelId, audio));

        Assert.True((await f.Service.SubmitAsync(AiAssistantDocumentOperationsTests.DocumentRequest(f), Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_installation_caps_readings_apart_from_its_global_quota()
    {
        var f = new AssistantOperationsFixture(documentQualified: true, limits: new AiAssistantLimits { DocumentInstallationDailyQuota = 12 });
        Seed(f, 12, isDocument: true);

        var blocked = await f.Service.SubmitAsync(AiAssistantDocumentOperationsTests.DocumentRequest(f), Ct);

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
        Assert.True((await f.Service.SubmitAsync(f.Request(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task Readings_never_exhaust_the_global_installation_quota_of_interpretations()
    {
        var f = new AssistantOperationsFixture(documentQualified: true, limits: new AiAssistantLimits { DocumentInstallationDailyQuota = 200 });
        Seed(f, 100, isDocument: true);

        Assert.True((await f.Service.SubmitAsync(f.Request(), Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_global_installation_quota_of_interpretations_never_blocks_a_reading()
    {
        var f = new AssistantOperationsFixture(documentQualified: true);
        Seed(f, 100, isDocument: false);

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, (await f.Service.SubmitAsync(f.Request(), Ct)).Error.Code);
        Assert.True((await f.Service.SubmitAsync(AiAssistantDocumentOperationsTests.DocumentRequest(f), Ct)).IsSuccess);
    }

    [Fact]
    public async Task An_identical_accepted_reading_is_recovered_after_its_quota_is_exhausted()
    {
        var f = new AssistantOperationsFixture(documentQualified: true);
        var request = AiAssistantDocumentOperationsTests.DocumentRequest(f);
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        Assert.True((await f.Service.CancelAsync(request.Context, request.RequestId, Ct)).IsSuccess);
        await FillAsync(f, 9, "another-channel");
        var before = f.Store.BudgetReads;

        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);

        Assert.Equal(before, f.Store.BudgetReads);
        Assert.Equal(10, f.Store.Receipts.Count);
    }

    [Fact]
    public async Task Removing_content_does_not_release_the_reading_quota()
    {
        var f = new AssistantOperationsFixture(documentQualified: true);
        await FillAsync(f, 10, f.Context.ChannelId);
        f.Store.Turns.Clear(); f.Store.Conversations.Clear();

        var blocked = await f.Service.SubmitAsync(AiAssistantDocumentOperationsTests.DocumentRequest(f), Ct);

        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, blocked.Error.Code);
    }

    private static async Task FillAsync(AssistantOperationsFixture f, int count, string channel)
    {
        for (var i = 0; i < count; i++)
        {
            var request = AiAssistantDocumentOperationsTests.DocumentRequest(f, channel);
            Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
            Assert.True((await f.Service.CancelAsync(request.Context, request.RequestId, Ct)).IsSuccess);
        }
    }

    // Other people's acceptances of the day: readings or interpretations.
    private static void Seed(AssistantOperationsFixture f, int count, bool isDocument)
    {
        var now = f.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < count; i++) f.Store.Receipts.Add(AiAssistantRequestReceipt.Create(Guid.NewGuid(), Guid.NewGuid(), "fictional-fingerprint",
            "fixture-key", Guid.NewGuid(), Guid.NewGuid(), now, now.AddDays(1), "channel-" + i, false, isDocument));
    }
}
