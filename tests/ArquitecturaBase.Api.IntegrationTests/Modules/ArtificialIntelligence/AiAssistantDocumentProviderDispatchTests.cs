using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// The existing provider adapter routes ReadDocument: it opens the private copy with its exact size, checks that nothing follows and the
// SHA-256, and only then calls the document transport once. A storage failure is ContentUnavailable before any POST.
public sealed class AiAssistantDocumentProviderDispatchTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly byte[] Sample = Encoding.ASCII.GetBytes("%PDF-1.7 fictitious reading sample");
    private static LlmConversationRequest Request(FakeTimeProvider clock) => new(AiProvider.OpenAI, "vision-model", "fictional-shared-key",
        "sealed reading instructions", [], [], [], null, 8192, 262144, 131072)
    {
        Document = new("private/document.pdf", "Comprobante.pdf", "application/pdf", Sample.Length, Convert.ToHexStringLower(SHA256.HashData(Sample)), 1,
            clock.GetUtcNow().UtcDateTime.AddHours(1)),
        DocumentMaximumBytes = 10 * 1024 * 1024, LowReasoningEffort = true,
    };

    private static LlmConversationClient Client(DocumentReadingClient documents, AudioNormalizationFiles files, FakeTimeProvider clock, HttpClient http)
    {
        var limits = new AiAssistantLimits();
        var transport = new LlmConversationHttpTransport(limits, clock);
        return new(new OpenAiConversationClient(http, transport), new AnthropicConversationClient(http, transport), new AudioNormalizationClient(),
            documents, files, limits, clock);
    }

    [Fact]
    public async Task The_existing_provider_adapter_reads_verified_private_bytes_and_dispatches_the_reading_once()
    {
        var clock = new FakeTimeProvider(); var documents = new DocumentReadingClient(); var files = new AudioNormalizationFiles(Sample);
        using var http = new HttpClient();

        var result = await Client(documents, files, clock, http).SendAsync(Request(clock), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(LlmDocumentReadingResponse.Read, result.Value.State);
        Assert.Equal("| Monto | $ 1.000 |", result.Value.DocumentReading!.Text);
        Assert.Null(result.Value.NormalizedAudio);
        Assert.Empty(result.Value.ToolCalls);
        Assert.Null(result.Value.Continuation);
        Assert.Equal(new LlmConversationUsage(1200, 300), result.Value.Usage);
        Assert.Equal(1, files.Reads); Assert.Equal(1, documents.Calls);
        var sent = documents.Last!;
        Assert.Equal(Sample, sent.Content.ToArray());
        Assert.Equal(("vision-model", "fictional-shared-key", "application/pdf"), (sent.Model, sent.ApiKey, sent.ContentType));
        Assert.Equal("sealed reading instructions", sent.Instructions);
        Assert.Equal((8192, 262144, 60), (sent.MaxOutputTokens, sent.MaxResponseBytes, sent.TimeoutSeconds));
        Assert.True(sent.LowReasoningEffort);
    }

    [Theory]
    [InlineData("refusal")]
    [InlineData("incomplete")]
    public async Task A_reading_without_content_keeps_its_state_and_usage_for_the_engine(string state)
    {
        var clock = new FakeTimeProvider();
        var documents = new DocumentReadingClient { Response = new LlmDocumentReadingResponse(state, null, new(12, 3)) };
        using var http = new HttpClient();

        var result = await Client(documents, new AudioNormalizationFiles(Sample), clock, http).SendAsync(Request(clock), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value.State);
        Assert.Null(result.Value.DocumentReading);
        Assert.Equal(new LlmConversationUsage(12, 3), result.Value.Usage);
    }

    [Fact]
    public async Task A_transport_failure_is_returned_as_is()
    {
        var clock = new FakeTimeProvider();
        var documents = new DocumentReadingClient { Response = Result.Failure<LlmDocumentReadingResponse>(AiAssistantErrors.ProviderUncertain) };
        using var http = new HttpClient();

        var result = await Client(documents, new AudioNormalizationFiles(Sample), clock, http).SendAsync(Request(clock), Ct);

        Assert.Equal(AiAssistantErrors.ProviderUncertain, result.Error);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("longer")]
    [InlineData("profileBytes")]
    [InlineData("expired")]
    [InlineData("expiresDuringRead")]
    [InlineData("mixedText")]
    [InlineData("noInstructions")]
    [InlineData("anthropic")]
    public async Task Invalid_or_expired_private_documents_never_reach_the_provider(string change)
    {
        var clock = new FakeTimeProvider(); var documents = new DocumentReadingClient();
        var files = new AudioNormalizationFiles(change switch { "missing" => null, "longer" => [.. Sample, (byte)'!'], _ => Sample });
        var request = Request(clock);
        if (change == "hash") request = request with { Document = request.Document! with { Sha256 = new string('a', 64) } };
        if (change == "size") request = request with { Document = request.Document! with { Size = Sample.Length + 1 } };
        if (change == "profileBytes") request = request with { DocumentMaximumBytes = Sample.Length - 1 };
        if (change == "expired") clock.Advance(TimeSpan.FromHours(2));
        if (change == "expiresDuringRead") files.BeforeRead = () => clock.Advance(TimeSpan.FromHours(2));
        if (change == "mixedText") request = request with { Messages = [new("user", "second input")] };
        if (change == "noInstructions") request = request with { SystemPrompt = "" };
        if (change == "anthropic") request = request with { Provider = AiProvider.Anthropic };
        using var http = new HttpClient();

        Assert.True((await Client(documents, files, clock, http).SendAsync(request, Ct)).IsFailure);
        Assert.Equal(0, documents.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_private_storage_failure_is_known_unavailable_before_any_reading_request(bool duringStream)
    {
        var clock = new FakeTimeProvider(); var documents = new DocumentReadingClient();
        var files = new AudioNormalizationFiles(Sample);
        var failure = new AggregateException(new Azure.RequestFailedException(503, "Synthetic exhausted private storage retries."));
        if (duringStream) files.StreamReadFailure = failure;
        else files.BeforeRead = () => throw failure;
        using var http = new HttpClient();

        var result = await Client(documents, files, clock, http).SendAsync(Request(clock), Ct);

        Assert.Equal(AiAssistantErrors.ContentUnavailable, result.Error);
        Assert.Equal(1, files.Reads);
        Assert.Equal(0, documents.Calls);
    }

    [Fact]
    public async Task Cancellation_while_opening_the_private_document_remains_canceled_without_a_reading()
    {
        var clock = new FakeTimeProvider(); var documents = new DocumentReadingClient();
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var files = new AudioNormalizationFiles(Sample) { BeforeRead = () => { canceled.Cancel(); canceled.Token.ThrowIfCancellationRequested(); } };
        using var http = new HttpClient();

        var result = await Client(documents, files, clock, http).SendAsync(Request(clock), canceled.Token);

        Assert.Equal(AiAssistantErrors.Canceled, result.Error);
        Assert.Equal(0, documents.Calls);
    }

    [Fact]
    public async Task An_audio_and_a_document_together_are_never_sent()
    {
        var clock = new FakeTimeProvider(); var documents = new DocumentReadingClient();
        var request = Request(clock) with { SystemPrompt = "", Audio = new("private/audio", "audio.ogg", "audio/ogg", 3, new string('a', 64), clock.GetUtcNow().UtcDateTime.AddHours(1)),
            AudioMaximumBytes = 25000000 };
        using var http = new HttpClient();

        Assert.Equal(AiAssistantErrors.Invalid, (await Client(documents, new AudioNormalizationFiles(Sample), clock, http).SendAsync(request, Ct)).Error);
        Assert.Equal(0, documents.Calls);
    }
}
