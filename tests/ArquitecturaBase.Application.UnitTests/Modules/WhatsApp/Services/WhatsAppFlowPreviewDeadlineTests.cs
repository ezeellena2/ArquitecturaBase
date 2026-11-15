using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFlowPreviewDeadlineTests
{
    [Fact]
    public async Task Official_preview_answers_when_meta_answers()
    {
        var fixture = new WhatsAppFlowPreviewFixture();

        var result = await fixture.CreateService().GetPreviewAsync(fixture.Query, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData(WhatsAppFlowPreviewHang.Account)]
    [InlineData(WhatsAppFlowPreviewHang.Verification)]
    [InlineData(WhatsAppFlowPreviewHang.Preview)]
    public async Task Official_preview_is_unavailable_when_meta_stops_answering_for_thirty_seconds(WhatsAppFlowPreviewHang hang)
    {
        var fixture = new WhatsAppFlowPreviewFixture { Hang = hang };

        var pending = fixture.CreateService().GetPreviewAsync(fixture.Query, TestContext.Current.CancellationToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(pending.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.PreviewUnavailable, result.Error);
    }

    [Theory]
    [InlineData(WhatsAppFlowPreviewHang.Account)]
    [InlineData(WhatsAppFlowPreviewHang.Verification)]
    public async Task Publication_status_is_unavailable_when_meta_stops_answering_for_thirty_seconds(WhatsAppFlowPreviewHang hang)
    {
        var fixture = new WhatsAppFlowPreviewFixture { Hang = hang };

        var pending = fixture.CreateService().GetAsync(new(fixture.Query.OperationId, fixture.Query.DefinitionId, fixture.Query.VersionId, fixture.Query.PublicationFingerprint),
            TestContext.Current.CancellationToken);
        fixture.Time.Advance(TimeSpan.FromSeconds(29));
        Assert.False(pending.IsCompleted);
        fixture.Time.Advance(TimeSpan.FromSeconds(1));
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal(FlowErrors.AccountUnavailable, result.Error);
    }

    [Fact]
    public async Task Closing_the_official_preview_still_cancels_the_request()
    {
        var fixture = new WhatsAppFlowPreviewFixture { Hang = WhatsAppFlowPreviewHang.Verification };
        using var closed = new CancellationTokenSource();

        var pending = fixture.CreateService().GetPreviewAsync(fixture.Query, closed.Token);
        await closed.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }
}
