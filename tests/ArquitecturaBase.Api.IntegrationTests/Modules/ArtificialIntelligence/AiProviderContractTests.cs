using System.Net;
using System.Text;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.OpenAI;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Providers.Anthropic;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiProviderContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task Catalogs_use_authenticated_gets_and_list_all_pages_without_generating_text(AiProvider provider)
    {
        using var handler = new CatalogHandler(provider);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(provider == AiProvider.OpenAI ? "https://api.openai.com/" : "https://api.anthropic.com/") };
        var transport = new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System);
        ILlmProviderClient client = provider == AiProvider.OpenAI ? new OpenAiResponsesClient(http, transport) : new AnthropicMessagesClient(http, transport);
        var result = await client.ListModelsAsync(new(provider, "fictional-key"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(["a-model", "z-model"], result.Value.Select(x => x.Id));
        Assert.Equal(provider == AiProvider.OpenAI ? 1 : 2, handler.Calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"data\":{}}")]
    [InlineData("{\"data\":[{\"id\":\"\"}]}")]
    [InlineData("{\"data\":[{\"id\":\"bad model\"}]}")]
    public async Task Malformed_catalogs_do_not_become_an_empty_success(string json)
    {
        using var http = new HttpClient(new RawHandler(json)) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        Assert.Equal("Ai.Provider.ResponseInvalid", (await client.ListModelsAsync(new(AiProvider.OpenAI, "fictional-key"), Ct)).Error.Code);
    }

    [Fact]
    public async Task A_catalog_timeout_is_unavailable_and_does_not_claim_billable_uncertainty()
    {
        using var handler = new TimeoutHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions { TimeoutSeconds = 1 }), TimeProvider.System));
        Assert.Equal("Ai.Provider.Unavailable", (await client.ListModelsAsync(new(AiProvider.OpenAI, "fictional-key"), Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{\"data\":[],\"has_more\":true,\"last_id\":\"cursor\"}")]
    [InlineData("{\"data\":[{\"id\":\"model\"}],\"has_more\":true}")]
    [InlineData("{\"data\":[{\"id\":\"model\"}],\"has_more\":\"yes\"}")]
    [InlineData("{\"data\":[{\"id\":\"model\"}]}")]
    public async Task Incomplete_anthropic_pagination_never_returns_a_partial_catalog(string json)
    {
        using var http = new HttpClient(new RawHandler(json)) { BaseAddress = new Uri("https://api.anthropic.com/") };
        var client = new AnthropicMessagesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        Assert.Equal("Ai.Provider.ResponseInvalid", (await client.ListModelsAsync(new(AiProvider.Anthropic, "fictional-key"), Ct)).Error.Code);
    }

    [Fact]
    public async Task Repeated_anthropic_cursors_terminate_without_returning_a_partial_list()
    {
        const string json = "{\"data\":[{\"id\":\"model\"}],\"has_more\":true,\"last_id\":\"model\"}";
        using var http = new HttpClient(new RawHandler(json)) { BaseAddress = new Uri("https://api.anthropic.com/") };
        var client = new AnthropicMessagesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        Assert.Equal("Ai.Provider.ResponseInvalid", (await client.ListModelsAsync(new(AiProvider.Anthropic, "fictional-key"), Ct)).Error.Code);
    }

    private sealed class CatalogHandler(AiProvider provider) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/v1/models", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            Assert.DoesNotContain("fictional-key", request.RequestUri.Query, StringComparison.Ordinal);
            string json;
            if (provider == AiProvider.OpenAI)
            {
                Assert.Equal("fictional-key", request.Headers.Authorization!.Parameter);
                json = "{\"data\":[{\"id\":\"z-model\"},{\"id\":\"a-model\"},{\"id\":\"a-model\"}]}";
            }
            else
            {
                Assert.Equal("fictional-key", Assert.Single(request.Headers.GetValues("x-api-key")));
                Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
                if (Calls == 1) json = "{\"data\":[{\"id\":\"z-model\",\"display_name\":\"Z model\"}],\"has_more\":true,\"last_id\":\"z-model\"}";
                else
                {
                    Assert.Contains("after_id=z-model", request.RequestUri.Query, StringComparison.Ordinal);
                    json = "{\"data\":[{\"id\":\"a-model\",\"display_name\":\"A model\"}],\"has_more\":false,\"last_id\":\"a-model\"}";
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Service_defaults_cannot_retry_billable_posts_and_busy_tests_do_not_queue()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddArtificialIntelligenceInfrastructure(builder.Configuration);
        using var handler = new ControlledHandler();
        builder.Services.AddHttpClient(ArtificialIntelligenceInfrastructureRegistration.OpenAiHttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = builder.Services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILlmClient>();
        var request = new ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models.LlmTestRequest(AiProvider.OpenAI, "fictional-model", "fictional-key");
        var first = client.TestAsync(request, Ct);
        var second = client.TestAsync(request, Ct);
        await handler.Started.Task.WaitAsync(Ct);
        Assert.Equal("Ai.Provider.Busy", (await client.TestAsync(request, Ct)).Error.Code);
        Assert.Equal("Ai.Provider.Busy", (await client.ListModelsAsync(new(AiProvider.OpenAI, "fictional-key"), Ct)).Error.Code);
        handler.Release.SetResult();
        Assert.Equal("Ai.Provider.Unavailable", (await first).Error.Code);
        Assert.Equal("Ai.Provider.Unavailable", (await second).Error.Code);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task A_transport_deadline_returns_an_uncertain_result_without_retrying()
    {
        using var handler = new TimeoutHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions { TimeoutSeconds = 1 }), TimeProvider.System));
        Assert.Equal("Ai.Provider.TestUncertain", (await client.TestAsync(new(AiProvider.OpenAI, "fictional-model", "fictional-key"), Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class ControlledHandler : HttpMessageHandler
    {
        private int _calls;
        public int Calls => _calls;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 2) Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }
    }
    private sealed class TimeoutHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("not-json", "Ai.Provider.ResponseInvalid")]
    [InlineData("[]", "Ai.Provider.ResponseInvalid")]
    [InlineData("{\"status\":\"incomplete\"}", "Ai.Provider.ResponseIncomplete")]
    [InlineData("{\"status\":\"completed\",\"output\":[]}", "Ai.Provider.ResponseIncomplete")]
    public async Task Malformed_or_truncated_responses_are_not_reported_as_success(string json, string code)
    {
        using var http = new HttpClient(new RawHandler(json)) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        Assert.Equal(code, (await client.TestAsync(new(AiProvider.OpenAI, "fictional-model", "fictional-key"), Ct)).Error.Code);
    }

    [Fact]
    public async Task Invalid_usage_numbers_are_ignored_without_throwing_or_returning_generated_text()
    {
        const string json = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"fictional-key\"}]}],\"usage\":{\"input_tokens\":\"bad\",\"output_tokens\":-1}}";
        using var http = new HttpClient(new RawHandler(json)) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        var result = await client.TestAsync(new(AiProvider.OpenAI, "fictional-model", "fictional-key"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.InputTokens);
        Assert.Null(result.Value.OutputTokens);
        Assert.DoesNotContain("fictional-key", result.Value.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_response_is_rejected_before_parsing()
    {
        using var http = new HttpClient(new RawHandler(new string('a', 65537))) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        Assert.Equal("Ai.Provider.ResponseInvalid", (await client.TestAsync(new(AiProvider.OpenAI, "fictional-model", "fictional-key"), Ct)).Error.Code);
    }

    private sealed class RawHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    [Theory]
    [InlineData(AiProvider.OpenAI)]
    [InlineData(AiProvider.Anthropic)]
    public async Task The_test_sends_per_request_auth_and_a_bounded_fixed_prompt(AiProvider provider)
    {
        using var handler = new Handler(provider);
        using var http = new HttpClient(handler) { BaseAddress = new Uri(provider == AiProvider.OpenAI ? "https://api.openai.com/" : "https://api.anthropic.com/") };
        var transport = new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System);
        ILlmProviderClient client = provider == AiProvider.OpenAI ? new OpenAiResponsesClient(http, transport) : new AnthropicMessagesClient(http, transport);
        var result = await client.TestAsync(new(provider, "fictional-model", "fictional-key"), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.InputTokens);
        Assert.Equal(1, result.Value.OutputTokens);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(401, "Ai.Provider.CredentialInvalid")]
    [InlineData(429, "Ai.Provider.RateLimited")]
    [InlineData(503, "Ai.Provider.Unavailable")]
    public async Task Provider_errors_are_normalized_without_returning_or_repeating_their_body(int status, string code)
    {
        using var handler = new Handler(AiProvider.OpenAI, (HttpStatusCode)status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/") };
        var client = new OpenAiResponsesClient(http, new LlmHttpTransport(Options.Create(new AiTransportOptions()), TimeProvider.System));
        var result = await client.TestAsync(new(AiProvider.OpenAI, "fictional-model", "fictional-key"), Ct);
        Assert.Equal(code, result.Error.Code);
        Assert.DoesNotContain("fictional-key", result.Error.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    private sealed class Handler(AiProvider provider, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("fictional-model", body, StringComparison.Ordinal);
            Assert.DoesNotContain("fictional-key", body, StringComparison.Ordinal);
            if (provider == AiProvider.OpenAI)
            {
                Assert.Equal("/v1/responses", request.RequestUri!.AbsolutePath);
                Assert.Equal("fictional-key", request.Headers.Authorization!.Parameter);
                Assert.Contains("\"store\":false", body, StringComparison.Ordinal);
                Assert.Contains("\"max_output_tokens\":128", body, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal("/v1/messages", request.RequestUri!.AbsolutePath);
                Assert.Equal("fictional-key", Assert.Single(request.Headers.GetValues("x-api-key")));
                Assert.Equal("2023-06-01", Assert.Single(request.Headers.GetValues("anthropic-version")));
                Assert.Contains("\"max_tokens\":128", body, StringComparison.Ordinal);
            }
            var json = status != HttpStatusCode.OK ? "{\"error\":{\"message\":\"fictional-key\",\"type\":\"api_error\"}}"
                : provider == AiProvider.OpenAI
                    ? "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"OK\"}]}],\"usage\":{\"input_tokens\":4,\"output_tokens\":1}}"
                    : "{\"type\":\"message\",\"stop_reason\":\"end_turn\",\"content\":[{\"type\":\"text\",\"text\":\"OK\"}],\"usage\":{\"input_tokens\":4,\"output_tokens\":1}}";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
