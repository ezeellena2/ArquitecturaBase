using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioProviderDispatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Sample = Encoding.ASCII.GetBytes("OggS-fictitious-normalization-sample");
    private static LlmConversationRequest Request(FakeTimeProvider clock) => new(AiProvider.OpenAI, "audio-model", "fictional-shared-key", "", [], [], [], null, 0, 262144, 131072)
    { Audio = new("private/audio", "audio.ogg", "audio/ogg; codecs=opus", Sample.Length, Convert.ToHexString(SHA256.HashData(Sample)), clock.GetUtcNow().UtcDateTime.AddHours(1)), AudioMaximumBytes = 25000000 };
    private static LlmConversationClient Client(HttpClient http, AudioNormalizationClient audio, AudioNormalizationFiles files, FakeTimeProvider clock)
    {
        var limits = new AiAssistantLimits();
        var transport = new LlmConversationHttpTransport(limits, clock);
        return new(new OpenAiConversationClient(http, transport), new AnthropicConversationClient(http, transport), audio, files, limits, clock);
    }
    [Fact]
    public async Task The_existing_provider_adapter_reads_verified_private_bytes_and_dispatches_STT_once()
    {
        var clock = new FakeTimeProvider(); var audio = new AudioNormalizationClient(); var files = new AudioNormalizationFiles(Sample);
        using var http = new HttpClient();
        var result = await Client(http, audio, files, clock).SendAsync(Request(clock), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("normalizedAudio", result.Value.State);
        Assert.Equal("Visit at North branch", result.Value.NormalizedAudio!.Text);
        Assert.Equal(["en"], result.Value.NormalizedAudio.Languages);
        Assert.Empty(result.Value.ToolCalls);
        Assert.Null(result.Value.Continuation);
        Assert.Null(result.Value.Usage!.InputTokens);
        Assert.Equal(2.75m, result.Value.Usage.AudioSeconds);
        Assert.Equal(1, files.Reads); Assert.Equal(1, audio.Calls);
        Assert.Equal(Sample, audio.Last!.Content.ToArray());
        Assert.Equal("fictional-shared-key", audio.Last.ApiKey);
        Assert.Equal("audio-model", audio.Last.Model);
        Assert.Equal("audio/ogg; codecs=opus", audio.Last.ContentType);
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("profileBytes")]
    [InlineData("expired")]
    [InlineData("expiresDuringRead")]
    [InlineData("mixedText")]
    public async Task Invalid_or_expired_private_audio_never_reaches_the_provider(string change)
    {
        var clock = new FakeTimeProvider(); var audio = new AudioNormalizationClient(); var files = new AudioNormalizationFiles(change == "missing" ? null : Sample);
        var request = Request(clock);
        if (change == "hash") request = request with { Audio = request.Audio! with { Sha256 = new string('a', 64) } };
        if (change == "size") request = request with { Audio = request.Audio! with { Size = Sample.Length + 1 } };
        if (change == "profileBytes") request = request with { AudioMaximumBytes = Sample.Length - 1 };
        if (change == "expired") clock.Advance(TimeSpan.FromHours(2));
        if (change == "expiresDuringRead") files.BeforeRead = () => clock.Advance(TimeSpan.FromHours(2));
        if (change == "mixedText") request = request with { Messages = [new("user", "second input")] };
        using var http = new HttpClient();
        Assert.True((await Client(http, audio, files, clock).SendAsync(request, Ct)).IsFailure);
        Assert.Equal(0, audio.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_private_storage_failure_is_known_unavailable_before_any_STT_request(bool duringStream)
    {
        var clock = new FakeTimeProvider();
        var audio = new AudioNormalizationClient();
        var files = new AudioNormalizationFiles(Sample) { FailDuringStreamRead = duringStream };
        if (!duringStream) files.BeforeRead = () => throw new Azure.RequestFailedException(503, "Synthetic private storage open failure.");
        using var http = new HttpClient();
        var result = await Client(http, audio, files, clock).SendAsync(Request(clock), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal("Ai.Assistant.ContentUnavailable", result.Error.Code);
        Assert.Equal(0, audio.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exhausted_storage_retries_are_known_unavailable_before_STT(bool duringStream)
    {
        var clock = new FakeTimeProvider();
        var audio = new AudioNormalizationClient();
        var files = new AudioNormalizationFiles(Sample);
        var failure = new AggregateException(new Azure.RequestFailedException(503, "Synthetic exhausted private storage retries."));
        if (duringStream) files.StreamReadFailure = failure;
        else files.BeforeRead = () => throw failure;
        using var http = new HttpClient();

        var result = await Client(http, audio, files, clock).SendAsync(Request(clock), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
        Assert.Equal(1, files.Reads);
        Assert.Equal(0, audio.Calls);
    }

    [Fact]
    public async Task Cancellation_while_opening_private_audio_remains_canceled_without_STT()
    {
        var clock = new FakeTimeProvider();
        var audio = new AudioNormalizationClient();
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var files = new AudioNormalizationFiles(Sample) { BeforeRead = () => { canceled.Cancel(); canceled.Token.ThrowIfCancellationRequested(); } };
        using var http = new HttpClient();

        var result = await Client(http, audio, files, clock).SendAsync(Request(clock), canceled.Token);

        Assert.Equal(AiAssistantErrors.Canceled, result.Error);
        Assert.Equal(1, files.Reads);
        Assert.Equal(0, audio.Calls);
    }

    [Fact]
    public async Task Canceled_before_preparation_reads_no_private_blob_and_sends_nothing()
    {
        var clock = new FakeTimeProvider(); var audio = new AudioNormalizationClient(); var files = new AudioNormalizationFiles(Sample);
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); using var http = new HttpClient();
        var result = await Client(http, audio, files, clock).SendAsync(Request(clock), canceled.Token);
        Assert.Equal("Ai.Assistant.Canceled", result.Error.Code);
        Assert.Equal(0, files.Reads); Assert.Equal(0, audio.Calls);
    }
}
