using System.Net;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantProviderContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string EmptySchema = "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}";
    private const string OpenAiTool = "{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\",\"id\":\"reason-1\",\"summary\":[],\"encrypted_content\":\"opaque\"},{\"type\":\"function_call\",\"id\":\"item-1\",\"call_id\":\"call-1\",\"name\":\"users_summary\",\"arguments\":\"{}\",\"status\":\"completed\"}],\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}";
    private const string AnthropicTool = "{\"type\":\"message\",\"role\":\"assistant\",\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"thinking\",\"thinking\":\"private\",\"signature\":\"opaque\"},{\"type\":\"tool_use\",\"id\":\"call-1\",\"name\":\"users_summary\",\"input\":{}}],\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}";

    private static LlmConversationRequest Request(AiProvider provider) => new(provider, "fictional-model", "fictional-key", "Query authorized facts.",
        [new("user", "How many accounts?")], [new("users.summary", "Count accounts.", EmptySchema), new("assistant_answer", "Publish a validated answer.", EmptySchema)],
        [], null, 1500, 262144, 131072);

    private static ILlmConversationClient Client(AiProvider provider, HttpClient http, TimeProvider? clock = null, AiAssistantLimits? limits = null)
    {
        var transport = new LlmConversationHttpTransport(limits ?? new AiAssistantLimits(), clock ?? TimeProvider.System);
        return provider == AiProvider.OpenAI ? new OpenAiConversationClient(http, transport) : new AnthropicConversationClient(http, transport);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Generation_uses_fixed_origins_explicit_model_and_credentials_per_request(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://untrusted.example/") };
        var result = await Client(provider, http).SendAsync(Request(provider), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("tools", result.Value.State);
        Assert.Equal("users.summary", Assert.Single(result.Value.ToolCalls).Name);
        Assert.Equal(new LlmConversationUsage(12, 7), result.Value.Usage);
        Assert.Equal(provider == AiProvider.OpenAI ? "https://api.openai.com/v1/responses" : "https://api.anthropic.com/v1/messages", Assert.Single(handler.Urls));
        Assert.Equal("fictional-key", Assert.Single(handler.Credentials));
        using var body = JsonDocument.Parse(Assert.Single(handler.Bodies));
        Assert.Equal("fictional-model", body.RootElement.GetProperty("model").GetString());
        var tool = body.RootElement.GetProperty("tools")[0];
        Assert.Equal("users_summary", tool.GetProperty("name").GetString());
        if (provider == AiProvider.OpenAI)
        {
            Assert.False(body.RootElement.GetProperty("store").GetBoolean());
            Assert.False(body.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
            Assert.True(tool.GetProperty("strict").GetBoolean());
            Assert.False(body.RootElement.TryGetProperty("previous_response_id", out _));
        }
        else
        {
            Assert.Equal("2023-06-01", Assert.Single(handler.Versions));
            Assert.Equal("Query authorized facts.", body.RootElement.GetProperty("system").GetString());
        }
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Continuation_preserves_all_protocol_items_and_links_results_in_order(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool,
            provider == AiProvider.OpenAI
                ? "{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"call-2\",\"name\":\"assistant_answer\",\"arguments\":\"{}\"}]}"
                : "{\"type\":\"message\",\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"tool_use\",\"id\":\"call-2\",\"name\":\"assistant_answer\",\"input\":{}}]}");
        using var http = new HttpClient(handler);
        var client = Client(provider, http);
        var first = await client.SendAsync(Request(provider), Ct);
        Assert.True(first.IsSuccess);
        var second = await client.SendAsync(Request(provider) with
        {
            Continuation = first.Value.Continuation,
            ToolResults = [new("call-1", "users.summary", "{\"active\":4}")]
        }, Ct);
        Assert.True(second.IsSuccess);
        using var body = JsonDocument.Parse(handler.Bodies[1]);
        var items = body.RootElement.GetProperty(provider == AiProvider.OpenAI ? "input" : "messages");
        Assert.Equal(provider == AiProvider.OpenAI ? 4 : 3, items.GetArrayLength());
        if (provider == AiProvider.OpenAI)
        {
            Assert.Equal("reasoning", items[1].GetProperty("type").GetString());
            Assert.Equal("opaque", items[1].GetProperty("encrypted_content").GetString());
            Assert.Equal("function_call", items[2].GetProperty("type").GetString());
            Assert.Equal("call-1", items[3].GetProperty("call_id").GetString());
            Assert.Equal("{\"active\":4}", items[3].GetProperty("output").GetString());
        }
        else
        {
            Assert.Equal("assistant", items[1].GetProperty("role").GetString());
            Assert.Equal("thinking", items[1].GetProperty("content")[0].GetProperty("type").GetString());
            Assert.Equal("opaque", items[1].GetProperty("content")[0].GetProperty("signature").GetString());
            Assert.Equal("user", items[2].GetProperty("role").GetString());
            Assert.Equal("call-1", items[2].GetProperty("content")[0].GetProperty("tool_use_id").GetString());
        }
    }

    [Theory]
    [InlineData(AiProvider.OpenAI, "{\"status\":\"incomplete\"}")]
    [InlineData(AiProvider.Anthropic, "{\"type\":\"message\",\"stop_reason\":\"max_tokens\"}")]
    public async Task Incomplete_output_has_no_publishable_tools_or_continuation(AiProvider provider, string body)
    {
        using var handler = new RecordingHandler(body);
        using var http = new HttpClient(handler);
        var result = await Client(provider, http).SendAsync(Request(provider), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("incomplete", result.Value.State);
        Assert.Empty(result.Value.ToolCalls);
        Assert.Null(result.Value.Continuation);
        Assert.Null(result.Value.Usage);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI, "{\"status\":\"incomplete\",\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}", "incomplete")]
    [InlineData(AiProvider.OpenAI, "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"Declined\"}]}],\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}", "refusal")]
    [InlineData(AiProvider.Anthropic, "{\"type\":\"message\",\"stop_reason\":\"max_tokens\",\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}", "incomplete")]
    [InlineData(AiProvider.Anthropic, "{\"type\":\"message\",\"stop_reason\":\"refusal\",\"usage\":{\"input_tokens\":12,\"output_tokens\":7}}", "refusal")]
    public async Task Incomplete_and_refused_protocol_responses_preserve_known_usage_without_retry(AiProvider provider, string body, string state)
    {
        using var handler = new RecordingHandler(body);
        using var http = new HttpClient(handler);
        var result = await Client(provider, http).SendAsync(Request(provider), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(state, result.Value.State);
        Assert.Empty(result.Value.ToolCalls);
        Assert.Null(result.Value.Continuation);
        Assert.Equal(12, result.Value.Usage?.InputTokens);
        Assert.Equal(7, result.Value.Usage?.OutputTokens);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI, "not-json")]
    [InlineData(AiProvider.OpenAI, "{\"status\":\"completed\",\"status\":\"completed\",\"output\":[]}")]
    [InlineData(AiProvider.OpenAI, "{\"status\":\"completed\",\"output\":[{\"type\":\"function_call\",\"call_id\":\"id\",\"name\":\"users_summary\",\"arguments\":\"{\\\"x\\\":1,\\\"x\\\":2}\"}]}")]
    [InlineData(AiProvider.Anthropic, "{\"type\":\"message\",\"stop_reason\":\"tool_use\",\"content\":[{\"type\":\"tool_use\",\"id\":\"id\",\"name\":\"users_summary\",\"input\":{\"x\":1,\"x\":2}}]}")]
    public async Task Malformed_or_ambiguous_json_is_rejected_without_repair(AiProvider provider, string body)
    {
        using var handler = new RecordingHandler(body);
        using var http = new HttpClient(handler);
        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(provider, http).SendAsync(Request(provider), Ct)).Error.Code);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Deadline_uses_fake_time_and_never_retries_a_possible_send(AiProvider provider)
    {
        var clock = new FakeTimeProvider();
        using var handler = new WaitingHandler();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var sending = Client(provider, http, clock, new AiAssistantLimits { ProviderTimeoutSeconds = 3 }).SendAsync(Request(provider), Ct);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);
        clock.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await sending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI, HttpStatusCode.BadRequest, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.OpenAI, HttpStatusCode.Unauthorized, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.OpenAI, HttpStatusCode.TooManyRequests, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.OpenAI, HttpStatusCode.ServiceUnavailable, "Ai.Assistant.ProviderUncertain")]
    [InlineData(AiProvider.Anthropic, HttpStatusCode.BadRequest, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.Anthropic, HttpStatusCode.Forbidden, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.Anthropic, HttpStatusCode.TooManyRequests, "Ai.Assistant.ProviderRejected")]
    [InlineData(AiProvider.Anthropic, HttpStatusCode.InternalServerError, "Ai.Assistant.ProviderUncertain")]
    public async Task Http_failures_are_classified_without_parsing_or_exposing_provider_errors(AiProvider provider, HttpStatusCode status, string code)
    {
        using var handler = new RecordingHandler("private provider body and fictional-key", status: status);
        using var http = new HttpClient(handler);
        var result = await Client(provider, http).SendAsync(Request(provider), Ct);
        Assert.Equal(code, result.Error.Code);
        Assert.DoesNotContain("private", result.Error.Description, StringComparison.Ordinal);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Response_limits_apply_to_content_length_and_chunked_bodies(AiProvider provider)
    {
        foreach (var chunked in new[] { false, true })
        {
            using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool, chunked: chunked);
            using var http = new HttpClient(handler);
            var result = await Client(provider, http).SendAsync(Request(provider) with { MaxResponseBytes = 32 }, Ct);
            Assert.Equal("Ai.Assistant.LimitExceeded", result.Error.Code);
            Assert.Single(handler.Bodies);
        }
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Serialized_context_and_continuation_have_independent_byte_limits(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool);
        using var http = new HttpClient(handler);
        var context = await Client(provider, http, limits: new AiAssistantLimits { ContextBytes = 2000 }).SendAsync(Request(provider) with
        {
            Messages = [new("user", new string('é', 1000))]
        }, Ct);
        Assert.Equal("Ai.Assistant.LimitExceeded", context.Error.Code);
        Assert.Empty(handler.Bodies);

        var continuation = await Client(provider, http).SendAsync(Request(provider) with { MaxContinuationBytes = 16 }, Ct);
        Assert.Equal("Ai.Assistant.LimitExceeded", continuation.Error.Code);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Non_json_success_is_not_accepted_as_a_model_result(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool, mediaType: "text/html");
        using var http = new HttpClient(handler);
        Assert.Equal("Ai.Assistant.InvalidOutput", (await Client(provider, http).SendAsync(Request(provider), Ct)).Error.Code);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Missing_usage_is_unknown_and_zero_usage_remains_known(AiProvider provider)
    {
        var sample = provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool;
        const string usage = ",\"usage\":{\"input_tokens\":12,\"output_tokens\":7}";
        using var handler = new RecordingHandler(sample.Replace(usage, "", StringComparison.Ordinal),
            sample.Replace(usage, ",\"usage\":{\"input_tokens\":0,\"output_tokens\":0}", StringComparison.Ordinal));
        using var http = new HttpClient(handler);
        var client = Client(provider, http);
        var unknown = await client.SendAsync(Request(provider), Ct);
        Assert.True(unknown.IsSuccess);
        Assert.Null(unknown.Value.Usage);
        var known = await client.SendAsync(Request(provider), Ct);
        Assert.True(known.IsSuccess);
        Assert.Equal(new LlmConversationUsage(0, 0), known.Value.Usage);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Invented_tools_and_repeated_call_identifiers_are_rejected(AiProvider provider)
    {
        var sample = provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool;
        using var handler = new RecordingHandler(sample.Replace("users_summary", "run_sql", StringComparison.Ordinal), sample);
        using var http = new HttpClient(handler);
        var client = Client(provider, http);
        Assert.Equal("Ai.Assistant.ToolRejected", (await client.SendAsync(Request(provider), Ct)).Error.Code);
        var valid = await client.SendAsync(Request(provider), Ct);
        Assert.True(valid.IsSuccess);
        var repeated = await client.SendAsync(Request(provider) with
        {
            Continuation = valid.Value.Continuation,
            ToolResults = [new("call-1", "users.summary", "{}")]
        }, Ct);
        Assert.Equal("Ai.Assistant.InvalidOutput", repeated.Error.Code);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Invalid_continuations_and_mismatched_results_do_not_send_another_request(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool);
        using var http = new HttpClient(handler);
        var client = Client(provider, http);
        Assert.Equal("Ai.Assistant.Invalid", (await client.SendAsync(Request(provider) with { Continuation = "{\"version\":\"bad\"}" }, Ct)).Error.Code);
        Assert.Empty(handler.Bodies);
        var first = await client.SendAsync(Request(provider), Ct);
        Assert.True(first.IsSuccess);
        var mismatched = await client.SendAsync(Request(provider) with
        {
            Continuation = first.Value.Continuation,
            ToolResults = [new("foreign-call", "users.summary", "{}")]
        }, Ct);
        Assert.Equal("Ai.Assistant.Invalid", mismatched.Error.Code);
        Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Cancellation_before_send_is_known_and_after_send_is_uncertain(AiProvider provider)
    {
        using var handler = new WaitingHandler();
        using var http = new HttpClient(handler);
        var client = Client(provider, http);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Equal("Ai.Assistant.Canceled", (await client.SendAsync(Request(provider), canceled.Token)).Error.Code);
        Assert.Equal(0, handler.Calls);
        using var active = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var sending = client.SendAsync(Request(provider), active.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2), Ct);
        active.Cancel();
        Assert.Equal("Ai.Assistant.ProviderUncertain", (await sending.WaitAsync(Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Alias_collisions_cannot_change_the_tool_catalog(AiProvider provider)
    {
        using var handler = new RecordingHandler(provider == AiProvider.OpenAI ? OpenAiTool : AnthropicTool);
        using var http = new HttpClient(handler);
        var result = await Client(provider, http).SendAsync(Request(provider) with
        {
            Tools = [new("users.summary", "First.", EmptySchema), new("users_summary", "Second.", EmptySchema)]
        }, Ct);
        Assert.Equal("Ai.Assistant.Invalid", result.Error.Code);
        Assert.Empty(handler.Bodies);
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Service_defaults_never_retry_assistant_generation_posts(AiProvider provider)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddArtificialIntelligenceInfrastructure(builder.Configuration);
        builder.Services.AddSingleton<ArquitecturaBase.Application.Interfaces.Integrations.Files.IPrivateFileStore>(
            new ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence.AudioNormalizationFiles(null));
        using var handler = new RecordingHandler("{}", status: HttpStatusCode.ServiceUnavailable);
        builder.Services.AddHttpClient(provider == AiProvider.OpenAI ? "AiAssistantOpenAI" : "AiAssistantAnthropic")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var services = builder.Services.BuildServiceProvider();
        var result = await services.GetRequiredService<ILlmConversationClient>().SendAsync(Request(provider), Ct);
        Assert.Equal("Ai.Assistant.ProviderUncertain", result.Error.Code);
        Assert.Single(handler.Bodies);
    }

    private sealed class RecordingHandler(string body, string? secondBody = null, HttpStatusCode status = HttpStatusCode.OK,
        string mediaType = "application/json", bool chunked = false) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];
        public List<string> Urls { get; } = [];
        public List<string> Credentials { get; } = [];
        public List<string> Versions { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Urls.Add(request.RequestUri!.AbsoluteUri);
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            Credentials.Add(request.Headers.Authorization?.Parameter ?? Assert.Single(request.Headers.GetValues("x-api-key")));
            if (request.Headers.TryGetValues("anthropic-version", out var values)) Versions.Add(Assert.Single(values));
            var text = Bodies.Count > 1 ? secondBody ?? body : body;
            if (chunked)
            {
                var content = new StreamContent(new NonSeekingStream(Encoding.UTF8.GetBytes(text)));
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
                return new HttpResponseMessage(status) { Content = content };
            }
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, mediaType) };
        }
    }

    private sealed class NonSeekingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }

    private sealed class WaitingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        }
    }
}
