using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// No Collection or ApiFactory: technical fast tests with in-process HTTP only. The bytes are protocol samples: they prove the exact
// request and the parsing, never that a model reads a real PDF or photo.
public sealed class AiDocumentReadingProviderContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Instructions = "Fictitious document reading instructions.";
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 fictitious protocol sample+/+/");
    // Their base64 has «+», «/» and padding.
    private static readonly byte[] PaddedPdf = [.. Pdf, 0xFB, 0xEF, 0xBE, 0xFF];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFB, 0xEF, 0xBE, 0xFF, 0xFF, 0xFF, 0x4A, 0x46, 0x49, 0x46, 0x00, 0xFF, 0xD9];
    private static readonly JsonSerializerOptions Serializer = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static LlmDocumentReadingRequest Request(string contentType = "application/pdf") => new(AiProvider.OpenAI, "gpt-vision",
        "fictional-document-key", Instructions, Pdf, contentType, 8192, 262144, 60);
    private static OpenAiDocumentReadingClient Client(HttpClient http, TimeProvider? clock = null) => new(http, new AiAssistantLimits(), clock ?? TimeProvider.System);
    private static string Body => DocumentReadingHandler.Completed(DocumentReadingHandler.Reading());

    [Fact]
    public async Task A_pdf_goes_as_an_input_file_with_the_strict_schema_to_the_fixed_origin_without_being_stored()
    {
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.example/") };

        var result = await Client(http).ReadAsync(Request(), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(LlmDocumentReadingResponse.Read, result.Value.State);
        Assert.Equal(new StructuredDocumentReading(StructuredDocumentReadability.Readable, null, "es", "| Monto | $ 1.000 |", false), result.Value.Reading);
        Assert.Equal(new LlmConversationUsage(1200, 300), result.Value.Usage);
        Assert.Equal("https://api.openai.com/v1/responses", Assert.Single(handler.Urls));
        Assert.Equal("Bearer", Assert.Single(handler.AuthenticationSchemes));
        Assert.Equal("fictional-document-key", Assert.Single(handler.Credentials));
        Assert.Equal("application/json", Assert.Single(handler.ContentTypes));
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        var raw = Assert.Single(handler.Bodies);
        var data = "data:application/pdf;base64," + Convert.ToBase64String(Pdf);
        Assert.Contains(data, raw, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(raw);
        var root = body.RootElement;
        Assert.Equal(["input", "instructions", "max_output_tokens", "model", "store", "text"],
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("gpt-vision", root.GetProperty("model").GetString());
        Assert.Equal(Instructions, root.GetProperty("instructions").GetString());
        Assert.Equal(8192, root.GetProperty("max_output_tokens").GetInt32());
        Assert.False(root.GetProperty("store").GetBoolean());
        var message = Assert.Single(root.GetProperty("input").EnumerateArray());
        Assert.Equal("user", message.GetProperty("role").GetString());
        var file = Assert.Single(message.GetProperty("content").EnumerateArray());
        Assert.Equal(["file_data", "filename", "type"], file.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("input_file", file.GetProperty("type").GetString());
        Assert.Equal("document.pdf", file.GetProperty("filename").GetString());
        Assert.Equal(data, file.GetProperty("file_data").GetString());
        AssertSchema(root.GetProperty("text").GetProperty("format"));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task A_photo_goes_as_an_input_image_with_high_detail(string contentType)
    {
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);

        Assert.True((await Client(http).ReadAsync(Request(contentType), Ct)).IsSuccess);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        var image = Assert.Single(Assert.Single(body.RootElement.GetProperty("input").EnumerateArray()).GetProperty("content").EnumerateArray());
        Assert.Equal(["detail", "image_url", "type"], image.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("input_image", image.GetProperty("type").GetString());
        Assert.Equal("high", image.GetProperty("detail").GetString());
        Assert.Equal($"data:{contentType};base64,{Convert.ToBase64String(Pdf)}", image.GetProperty("image_url").GetString());
        AssertSchema(body.RootElement.GetProperty("text").GetProperty("format"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_low_reasoning_effort_is_asked_only_when_the_profile_takes_it(bool reasoning)
    {
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);

        Assert.True((await Client(http).ReadAsync(Request() with { LowReasoningEffort = reasoning }, Ct)).IsSuccess);

        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal(reasoning, body.RootElement.TryGetProperty("reasoning", out var effort));
        if (reasoning) Assert.Equal("{\"effort\":\"low\"}", effort.GetRawText());
    }

    // The body is written in pieces into a pooled buffer, and it is byte for byte the one the serializer wrote from whole strings: the
    // same order, the relaxed encoder (escaped quotes and controls, everything else as is) and the base64 padding.
    [Theory]
    [InlineData("application/pdf", false)]
    [InlineData("image/jpeg", true)]
    public async Task The_body_is_byte_for_byte_the_one_the_serializer_wrote(string contentType, bool reasoning)
    {
        var request = Request(contentType) with
        {
            Instructions = "Transcribí «todo»:\n\"Monto\" <b>&</b> + / \\ \t[ilegible]",
            Content = contentType == "application/pdf" ? PaddedPdf : Jpeg,
            LowReasoningEffort = reasoning,
        };
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);

        Assert.True((await Client(http).ReadAsync(request, Ct)).IsSuccess);

        Assert.Equal(SerializerBody(request), Assert.Single(handler.RawBodies));
    }

    [Theory]
    [InlineData(3_071)]
    [InlineData(3_072)]
    [InlineData(3_073)]
    [InlineData(10_000)]
    public async Task A_file_of_several_pieces_goes_as_the_base64_of_the_whole_file(int size)
    {
        var content = new byte[size];
        for (var index = 0; index < size; index++) content[index] = (byte)(index * 151 + 7);
        var request = Request("image/png") with { Content = content };
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);

        Assert.True((await Client(http).ReadAsync(request, Ct)).IsSuccess);

        Assert.Equal(SerializerBody(request), Assert.Single(handler.RawBodies));
    }

    [Fact]
    public async Task Each_problem_and_a_missing_language_are_read()
    {
        foreach (var (problem, expected) in new (string, StructuredDocumentProblem)[] { ("blurry", StructuredDocumentProblem.Blurry),
            ("dark", StructuredDocumentProblem.Dark), ("cut", StructuredDocumentProblem.Cut), ("covered", StructuredDocumentProblem.Covered),
            ("other", StructuredDocumentProblem.Other) })
        {
            using var handler = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading("[ilegible]", "partial", problem, "es", true)));
            using var http = new HttpClient(handler);
            var result = await Client(http).ReadAsync(Request(), Ct);
            Assert.Equal(new StructuredDocumentReading(StructuredDocumentReadability.Partial, expected, "es", "[ilegible]", true), result.Value.Reading);
        }
        using var unreadable = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading("", "unreadable", "dark", null)));
        using var client = new HttpClient(unreadable);
        var empty = await Client(client).ReadAsync(Request(), Ct);
        Assert.Equal(new StructuredDocumentReading(StructuredDocumentReadability.Unreadable, StructuredDocumentProblem.Dark, null, "", false), empty.Value.Reading);
    }

    // The schema has no pattern for the language: a strict one would turn a locale into a 400 and lose the reading. The client keeps
    // what the model wrote and the engine maps it before validating.
    [Theory]
    [InlineData("", null)]
    [InlineData("es-AR", "es")]
    [InlineData("EN", "en")]
    public async Task A_blank_or_region_coded_language_arrives_as_written_and_becomes_its_lowercase_primary_subtag_before_validating(
        string language, string? expected)
    {
        using var handler = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(DocumentReadingHandler.Reading(language: language)));
        using var http = new HttpClient(handler);

        var result = await Client(http).ReadAsync(Request(), Ct);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(language, result.Value.Reading!.Language);
        var reading = AiAssistantDocumentPolicy.Normalize(result.Value.Reading);
        Assert.Equal(expected, reading.Language);
        Assert.True(AiAssistantDocumentPolicy.Valid(reading, AiAssistantLimits.ReadingCharactersCeiling));
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("failed")]
    [InlineData("refusal")]
    public async Task A_refusal_or_an_incomplete_reading_keeps_its_usage_without_a_reading(string outcome)
    {
        var body = outcome == "refusal"
            ? JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", role = "assistant",
                content = new[] { new { type = "refusal", refusal = "private refusal" } } } }, usage = new { input_tokens = 12, output_tokens = 3 } })
            : JsonSerializer.Serialize(new { status = outcome, output = Array.Empty<object>(), usage = new { input_tokens = 12, output_tokens = 3 } });
        using var handler = DocumentReadingHandler.Json(body);
        using var http = new HttpClient(handler);

        var result = await Client(http).ReadAsync(Request(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(outcome == "incomplete" ? "incomplete" : "refusal", result.Value.State);
        Assert.Null(result.Value.Reading);
        Assert.Equal(new LlmConversationUsage(12, 3), result.Value.Usage);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"status\":\"completed\",\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}")]
    [InlineData("{\"status\":\"queued\",\"output\":[]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"name\":\"x\"}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"plain text\"}]}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"{}\"},{\"type\":\"output_text\",\"text\":\"{}\"}]}]}")]
    [InlineData("{\"status\":\"completed\",\"output\":[],\"usage\":{\"input_tokens\":-1}}")]
    public async Task Invalid_or_ambiguous_protocol_output_is_rejected_without_repair(string body)
    {
        using var handler = DocumentReadingHandler.Json(body);
        using var http = new HttpClient(handler);

        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(http).ReadAsync(Request(), Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"readability\":\"legible\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":\"stained\",\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":null,\"language\":7,\"text\":\"Monto\",\"truncated\":false}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":null,\"truncated\":false}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":\"no\"}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\"}")]
    [InlineData("{\"readability\":\"readable\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false,\"total\":1}")]
    [InlineData("{\"readability\":\"readable\",\"readability\":\"partial\",\"problem\":null,\"language\":\"es\",\"text\":\"Monto\",\"truncated\":false}")]
    public async Task A_reading_outside_the_schema_is_invalid(string reading)
    {
        using var handler = DocumentReadingHandler.Json(DocumentReadingHandler.Completed(reading));
        using var http = new HttpClient(handler);

        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(http).ReadAsync(Request(), Ct)).Error.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Declared_and_chunked_response_bodies_have_the_same_byte_bound(bool chunked)
    {
        using var handler = DocumentReadingHandler.Json(Body, chunked: chunked);
        using var http = new HttpClient(handler);

        Assert.Equal("Ai.Assistant.LimitExceeded", (await Client(http).ReadAsync(Request() with { MaxResponseBytes = 64 }, Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.Unauthorized, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.TooManyRequests, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "Ai.Assistant.ProviderRejected")]
    [InlineData(HttpStatusCode.InternalServerError, "Ai.Assistant.ProviderUncertain")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "Ai.Assistant.ProviderUncertain")]
    public async Task Http_failures_never_expose_provider_bodies_or_retry_the_post(HttpStatusCode status, string code)
    {
        using var handler = DocumentReadingHandler.Json("private-provider-error fictional-document-key", status);
        using var http = new HttpClient(handler);

        var result = await Client(http).ReadAsync(Request(), Ct);

        Assert.Equal(code, result.Error.Code);
        Assert.DoesNotContain("private-provider", result.Error.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("fictional-document-key", result.Error.Description, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("io")]
    public async Task Transport_failure_after_a_possible_post_is_uncertain_without_retry(string fault)
    {
        using var handler = new DocumentReadingHandler(_ => Task.FromException<HttpResponseMessage>(fault == "http"
            ? new HttpRequestException("private-provider-detail") : new IOException("private-provider-detail")));
        using var http = new HttpClient(handler);

        var result = await Client(http).ReadAsync(Request(), Ct);

        Assert.Equal("Ai.Assistant.ProviderUncertain", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_successful_non_json_response_is_not_a_reading()
    {
        using var handler = DocumentReadingHandler.Json(Body, contentType: "text/html");
        using var http = new HttpClient(handler);

        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(http).ReadAsync(Request(), Ct)).Error.Code);
    }

    [Fact]
    public async Task Cancellation_before_http_is_known_and_after_http_is_uncertain()
    {
        using var handler = new DocumentReadingHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var client = Client(http);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal("Ai.Assistant.Canceled", (await client.ReadAsync(Request(), canceled.Token)).Error.Code);
        Assert.Equal(0, handler.Calls);
        using var active = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var pending = client.ReadAsync(Request(), active.Token);
        await Task.WhenAny(handler.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.True(handler.Started.Task.IsCompletedSuccessfully);
        active.Cancel();
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await pending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task The_document_deadline_uses_fake_time_after_an_observed_http_start()
    {
        var clock = new FakeTimeProvider();
        using var handler = new DocumentReadingHandler(async token =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var pending = Client(http, clock).ReadAsync(Request() with { TimeoutSeconds = 45 }, Ct);
        await Task.WhenAny(handler.Started.Task, pending).WaitAsync(TimeSpan.FromSeconds(2), Ct);
        Assert.True(handler.Started.Task.IsCompletedSuccessfully);
        clock.Advance(TimeSpan.FromSeconds(44));
        Assert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await pending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Invalid_local_metadata_and_excessive_bounds_make_zero_http_calls()
    {
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);
        var client = Client(http);
        foreach (var request in new[]
        {
            Request() with { Provider = AiProvider.Anthropic },
            Request() with { Model = "model with spaces" },
            Request() with { ApiKey = "" },
            Request() with { Instructions = " " },
            Request("application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
        }) Assert.Equal("Ai.Assistant.Invalid", (await client.ReadAsync(request, Ct)).Error.Code);
        foreach (var request in new[]
        {
            Request() with { Content = ReadOnlyMemory<byte>.Empty },
            Request() with { Content = new byte[OpenAiDocumentReadingClient.MaximumDocumentBytes + 1] },
            Request() with { MaxOutputTokens = AiAssistantLimits.DocumentOutputTokensCeiling + 1 },
            Request() with { MaxResponseBytes = 262145 },
            Request() with { TimeoutSeconds = 61 },
        }) Assert.Equal("Ai.Assistant.LimitExceeded", (await client.ReadAsync(request, Ct)).Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task The_largest_document_fits_the_body_cap_without_escaping_its_base64()
    {
        var content = new byte[OpenAiDocumentReadingClient.MaximumDocumentBytes];
        Array.Fill(content, (byte)0xFB);
        using var handler = DocumentReadingHandler.Json(Body);
        using var http = new HttpClient(handler);

        Assert.True((await Client(http).ReadAsync(Request() with { Content = content }, Ct)).IsSuccess);

        var raw = Assert.Single(handler.Bodies);
        Assert.Contains("+", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u002B", raw, StringComparison.Ordinal);
        Assert.True(Encoding.UTF8.GetByteCount(raw) <= OpenAiDocumentReadingClient.MaximumRequestBytes);
    }

    [Fact]
    public async Task Service_defaults_do_not_add_retries_to_reading_posts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddArtificialIntelligenceInfrastructure(builder.Configuration);
        using var handler = DocumentReadingHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
        builder.Services.AddHttpClient("AiAssistantOpenAIDocument").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = builder.Services.BuildServiceProvider();

        var result = await services.GetRequiredService<ILlmDocumentReadingClient>().ReadAsync(Request(), Ct);

        Assert.Equal("Ai.Assistant.ProviderUncertain", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void Diagnostic_strings_redact_the_credential_the_bytes_and_the_reading()
    {
        Assert.DoesNotContain("fictional-document-key", Request().ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Fictitious document", Request().ToString(), StringComparison.Ordinal);
        var response = new LlmDocumentReadingResponse(LlmDocumentReadingResponse.Read,
            new(StructuredDocumentReadability.Readable, null, "es", "private-reading", false), null);
        Assert.DoesNotContain("private-reading", response.ToString(), StringComparison.Ordinal);
    }

    // The body as JsonSerializer wrote it before the pooled writer: a dictionary of whole strings, the data URL among them.
    private static byte[] SerializerBody(LlmDocumentReadingRequest request)
    {
        var data = $"data:{request.ContentType};base64,{Convert.ToBase64String(request.Content.Span)}";
        object content = request.ContentType == "application/pdf"
            ? new { type = "input_file", filename = OpenAiDocumentReadingClient.FileName, file_data = data }
            : new { type = "input_image", image_url = data, detail = "high" };
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["model"] = request.Model, ["instructions"] = request.Instructions,
            ["input"] = new[] { new { role = "user", content = new[] { content } } },
            ["text"] = new { format = OpenAiDocumentReadingClient.Format }, ["max_output_tokens"] = request.MaxOutputTokens, ["store"] = false,
        };
        if (request.LowReasoningEffort) body["reasoning"] = new { effort = "low" };
        return JsonSerializer.SerializeToUtf8Bytes(body, Serializer);
    }

    private static void AssertSchema(JsonElement format)
    {
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.Equal("document_reading", format.GetProperty("name").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        var schema = format.GetProperty("schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.False(schema.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(["readability", "problem", "language", "text", "truncated"],
            schema.GetProperty("required").EnumerateArray().Select(item => item.GetString()));
        var properties = schema.GetProperty("properties");
        Assert.Equal(["language", "problem", "readability", "text", "truncated"],
            properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("{\"type\":\"string\",\"enum\":[\"readable\",\"partial\",\"unreadable\"]}", properties.GetProperty("readability").GetRawText());
        Assert.Equal("{\"type\":[\"string\",\"null\"],\"enum\":[\"blurry\",\"dark\",\"cut\",\"covered\",\"other\",null]}", properties.GetProperty("problem").GetRawText());
        Assert.Equal("{\"type\":[\"string\",\"null\"]}", properties.GetProperty("language").GetRawText());
        Assert.Equal("{\"type\":\"string\"}", properties.GetProperty("text").GetRawText());
        Assert.Equal("{\"type\":\"boolean\"}", properties.GetProperty("truncated").GetRawText());
    }
}
