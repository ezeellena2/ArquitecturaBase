using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// No Collection or ApiFactory: these are technical fast tests with in-process HTTP only.
public sealed class AiAudioTranscriptionProviderContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Transcript = "Visité la sucursal Norte. La puerta no cierra.";
    // This sample proves multipart preservation only. It does not prove Ogg/Opus decoding.
    private static readonly byte[] ProtocolSample = Encoding.ASCII.GetBytes("OggS-fictitious-protocol-sample");
    private static LlmAudioTranscriptionRequest Request() => new(AiProvider.OpenAI, "gpt-transcribe", "fictional-audio-key",
        ProtocolSample, "audio.ogg", "audio/ogg; codecs=opus", 4000, 65536, 30);
    private static OpenAiAudioTranscriptionClient Client(HttpClient http, TimeProvider? clock = null,
        AiAssistantLimits? limits = null) => new OpenAiAudioTranscriptionClient(http, limits ?? new(), clock ?? TimeProvider.System);
    private static string Body => System.Text.Json.JsonSerializer.Serialize(new { text = Transcript, languages = new[] { new { code = "es" } } });

    [Fact]
    public async Task Multipart_uses_fixed_origin_explicit_model_and_per_request_key_without_mutating_defaults()
    {
        using var handler = AudioTranscriptionHandler.Json(Body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.example/") };
        var result = await Client(http).TranscribeAsync(Request(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(Transcript, result.Value.Text);
        Assert.Equal(["es"], result.Value.Languages);
        Assert.Null(result.Value.Usage);
        Assert.Null(result.Value.AudioSeconds);
        Assert.True(result.Value.TokenUsageUnknown);
        Assert.Equal("https://api.openai.com/v1/audio/transcriptions", Assert.Single(handler.Urls));
        Assert.Equal("Bearer", Assert.Single(handler.AuthenticationSchemes));
        Assert.Equal("fictional-audio-key", Assert.Single(handler.Credentials));
        var fields = Assert.Single(handler.Fields);
        Assert.Equal(2, fields.Count);
        Assert.Equal("gpt-transcribe", fields["model"]);
        Assert.Equal("json", fields["response_format"]);
        Assert.Equal(ProtocolSample, Assert.Single(handler.Files));
        Assert.Equal("audio.ogg", Assert.Single(handler.FileNames));
        Assert.Equal("audio/ogg; codecs=opus", Assert.Single(handler.ContentTypes));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Theory]
    [InlineData("{\"text\":\"Hello\"}")]
    [InlineData("{\"text\":\"Hello\",\"languages\":[]}")]
    public async Task Absent_or_empty_language_detection_is_not_fabricated(string body)
    {
        using var handler = AudioTranscriptionHandler.Json(body);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Languages);
        Assert.True(result.Value.TokenUsageUnknown);
    }

    [Fact]
    public async Task Mixed_language_detection_is_preserved_without_translating_the_transcript()
    {
        const string body = "{\"text\":\"Fui a North branch y la puerta no cierra.\",\"languages\":[{\"code\":\"es\"},{\"code\":\"en\"}]}";
        using var handler = AudioTranscriptionHandler.Json(body);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Fui a North branch y la puerta no cierra.", result.Value.Text);
        Assert.Equal(["es", "en"], result.Value.Languages);
    }

    [Theory]
    [InlineData("{\"text\":\"Hello\",\"usage\":{\"type\":\"tokens\",\"input_tokens\":0,\"output_tokens\":0,\"total_tokens\":0}}", 0, 0)]
    [InlineData("{\"text\":\"Hello\",\"usage\":{\"type\":\"tokens\",\"input_tokens\":12,\"output_tokens\":7,\"total_tokens\":19}}", 12, 7)]
    public async Task Explicit_token_usage_including_zero_remains_known(string body, long input, long output)
    {
        using var handler = AudioTranscriptionHandler.Json(body);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(new LlmConversationUsage(input, output), result.Value.Usage);
        Assert.False(result.Value.TokenUsageUnknown);
        Assert.Null(result.Value.AudioSeconds);
    }

    [Fact]
    public async Task Reported_duration_never_becomes_an_invented_token_count()
    {
        using var handler = AudioTranscriptionHandler.Json("{\"text\":\"Hello\",\"usage\":{\"type\":\"duration\",\"seconds\":2.75}}");
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Usage);
        Assert.Equal(2.75m, result.Value.AudioSeconds);
        Assert.True(result.Value.TokenUsageUnknown);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"text\":\"one\",\"text\":\"two\"}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":\"   \"}")]
    [InlineData("{\"text\":\"hello\",\"languages\":[{\"code\":\"en\",\"code\":\"es\"}]}")]
    [InlineData("{\"text\":\"hello\",\"languages\":null}")]
    [InlineData("{\"text\":\"hello\",\"languages\":[{\"code\":12}]}")]
    [InlineData("{\"text\":\"hello\",\"usage\":{\"type\":\"tokens\",\"input_tokens\":-1}}")]
    [InlineData("{\"text\":\"hello\",\"usage\":{\"type\":\"duration\",\"seconds\":-1}}")]
    public async Task Invalid_or_ambiguous_protocol_output_is_rejected_without_repair(string body)
    {
        using var handler = AudioTranscriptionHandler.Json(body);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.Equal("Ai.Assistant.InvalidOutput", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_transcript_larger_than_the_bound_is_rejected_whole_and_never_truncated()
    {
        using var handler = AudioTranscriptionHandler.Json(System.Text.Json.JsonSerializer.Serialize(new { text = new string('a', 4001) }));
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.Equal("Ai.Assistant.LimitExceeded", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declared_and_chunked_response_bodies_have_the_same_byte_bound(bool chunked)
    {
        using var handler = AudioTranscriptionHandler.Json(Body, chunked: chunked);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request() with { MaxResponseBytes = 32 }, Ct);
        Assert.Equal("Ai.Assistant.LimitExceeded", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.Unauthorized, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.Forbidden, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.TooManyRequests, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.InternalServerError, "Ai.Assistant.ProviderUncertain")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Ai.Assistant.ProviderUncertain")]
    public async Task Http_failures_never_expose_provider_bodies_or_retry_the_post(HttpStatusCode status, string code)
    {
        using var handler = AudioTranscriptionHandler.Json("private-provider-error fictional-audio-key", status);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.Equal(code, result.Error.Code);
        Assert.DoesNotContain("private-provider", result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-audio-key", result.Error.Description, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    public async Task Transport_failure_after_a_possible_post_is_uncertain_without_retry(string fault)
    {
        using var handler = new AudioTranscriptionHandler(_ => Task.FromException<HttpResponseMessage>(fault == "http"
            ? new HttpRequestException("private-provider-detail fictional-audio-key")
            : new IOException("private-provider-detail fictional-audio-key")));
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request(), Ct);
        Assert.Equal("Ai.Assistant.ProviderUncertain", result.Error.Code);
        Assert.DoesNotContain("private-provider-detail", result.Error.Description, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_successful_non_json_response_is_not_accepted_as_a_transcription()
    {
        using var handler = AudioTranscriptionHandler.Json(Body, contentType: "text/html");
        using var http = new HttpClient(handler);
        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(http).TranscribeAsync(Request(), Ct)).Error.Code);
    }

    [Fact]
    public async Task Cancellation_before_http_is_known_and_after_http_is_uncertain()
    {
        using var handler = new AudioTranscriptionHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = Client(http);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal("Ai.Assistant.Canceled", (await client.TranscribeAsync(Request(), canceled.Token)).Error.Code);
        Assert.Equal(0, handler.Calls);
        using var active = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pending = client.TranscribeAsync(Request(), active.Token);
        await Task.WhenAny(handler.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.True(handler.Started.Task.IsCompletedSuccessfully);
        active.Cancel();
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await pending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Provider_deadline_uses_fake_time_after_an_observed_http_start()
    {
        var clock = new FakeTimeProvider();
        using var handler = new AudioTranscriptionHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var pending = Client(http, clock).TranscribeAsync(Request() with { TimeoutSeconds = 3 }, Ct);
        await Task.WhenAny(handler.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.True(handler.Started.Task.IsCompletedSuccessfully);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await pending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cancellation_during_the_response_body_is_uncertain_and_does_not_retry()
    {
        using var responseStream = new WaitingAudioReadStream();
        using var handler = new AudioTranscriptionHandler(_ =>
        {
            var content = new StreamContent(responseStream);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var active = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pending = Client(http).TranscribeAsync(Request(), active.Token);
        await Task.WhenAny(responseStream.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.True(responseStream.Started.Task.IsCompletedSuccessfully);
        active.Cancel();
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await pending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Invalid_local_metadata_and_excessive_profile_bounds_make_zero_http_calls()
    {
        using var handler = AudioTranscriptionHandler.Json(Body);
        using var http = new HttpClient(handler);
        var client = Client(http);
        foreach (var request in new[]
        {
            Request() with { Provider = AiProvider.Anthropic },
            Request() with { FileName = "../audio.ogg" },
            Request() with { ContentType = "not a media type" },
        }) Assert.Equal("Ai.Assistant.Invalid", (await client.TranscribeAsync(request, Ct)).Error.Code);
        foreach (var request in new[]
        {
            Request() with { Content = ReadOnlyMemory<byte>.Empty },
            Request() with { MaxTextCharacters = 4001 },
            Request() with { MaxResponseBytes = 262145 },
            Request() with { TimeoutSeconds = 31 },
        }) Assert.Equal("Ai.Assistant.LimitExceeded", (await client.TranscribeAsync(request, Ct)).Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task The_audio_byte_ceiling_is_checked_before_sending_multipart()
    {
        using var handler = AudioTranscriptionHandler.Json(Body);
        using var http = new HttpClient(handler);
        var result = await Client(http).TranscribeAsync(Request() with
        {
            Content = new byte[OpenAiAudioTranscriptionClient.MaximumAudioBytes + 1]
        }, Ct);
        Assert.Equal("Ai.Assistant.LimitExceeded", result.Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Service_defaults_do_not_add_retries_to_transcription_posts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddArtificialIntelligenceInfrastructure(builder.Configuration);
        using var handler = AudioTranscriptionHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
        builder.Services.AddHttpClient("AiAssistantOpenAIAudio").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = builder.Services.BuildServiceProvider();
        var result = await services.GetRequiredService<ILlmAudioTranscriptionClient>().TranscribeAsync(Request(), Ct);
        Assert.Equal("Ai.Assistant.ProviderUncertain", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Diagnostic_strings_redact_credential_bytes_and_transcription()
    {
        Assert.DoesNotContain("fictional-audio-key", Request().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("fictitious-protocol-sample", Request().ToString(), StringComparison.Ordinal);
        var response = new LlmAudioTranscriptionResponse(Transcript, ["es"], null, null);
        Assert.DoesNotContain(Transcript, response.ToString(), StringComparison.Ordinal);
    }
}
