using System.Net;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Places;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class GooglePlacesContractTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Autocomplete_uses_a_fixed_destination_header_key_and_only_required_fields()
    {
        using var handler = new Handler("""{"suggestions":[{"placePrediction":{"placeId":"ChIJ_fictional","text":{"text":"Fictional clinic, Example City"}}}]}""");
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://places.googleapis.com/") };
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key" }));
        var result = await client.SearchAsync("Fictional clinic", "es", Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("ChIJ_fictional", Assert.Single(result.Value.Places).PlaceId);
        Assert.Equal("https://places.googleapis.com/v1/places:autocomplete", handler.Url);
        Assert.Equal("fictional-key", handler.Key);
        Assert.Equal("suggestions.placePrediction.placeId,suggestions.placePrediction.text.text", handler.Mask);
        Assert.Equal("Fictional clinic", handler.Body!.Value.GetProperty("input").GetString());
        Assert.Equal("es", handler.Body.Value.GetProperty("languageCode").GetString());
        Assert.False(handler.Body.Value.TryGetProperty("sessionToken", out _));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"suggestions\":{}}")]
    [InlineData("{\"suggestions\":[{\"placePrediction\":{\"placeId\":\"\",\"text\":{\"text\":\"Name\"}}}]}")]
    public async Task Invalid_responses_do_not_become_empty_search_results(string json)
    {
        using var handler = new Handler(json);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://places.googleapis.com/") };
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key" }));
        Assert.Equal("Ai.Places.ResponseInvalid", (await client.SearchAsync("Clinic", "en", Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Ai.Places.ConfigurationInvalid")]
    [InlineData(HttpStatusCode.TooManyRequests, "Ai.Places.RateLimited")]
    [InlineData(HttpStatusCode.InternalServerError, "Ai.Places.Unavailable")]
    [InlineData(HttpStatusCode.Redirect, "Ai.Places.Unavailable")]
    public async Task Provider_errors_are_sanitized_and_never_retried(HttpStatusCode status, string code)
    {
        using var handler = new Handler("secret-provider-message", status);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://places.googleapis.com/") };
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key" }));
        var error = (await client.SearchAsync("Clinic", "en", Ct)).Error;
        Assert.Equal(code, error.Code);
        Assert.DoesNotContain("secret-provider-message", error.Description, StringComparison.Ordinal);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_credentials_do_not_attempt_network_access()
    {
        using var handler = new Handler("{}");
        using var http = new HttpClient(handler);
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions()));
        Assert.Equal("Ai.Places.NotConfigured", (await client.SearchAsync("Clinic", "en", Ct)).Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task A_timeout_releases_capacity_and_does_not_retry()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key", TimeoutSeconds = 1 }));
        Assert.Equal("Ai.Places.Unavailable", (await client.SearchAsync("Clinic", "en", Ct)).Error.Code);
        Assert.Equal(1, handler.Calls);
        handler.Release.TrySetResult();
        Assert.True((await client.SearchAsync("Clinic", "en", Ct)).IsSuccess);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Concurrent_searches_have_no_queue_and_cancellation_releases_the_slot()
    {
        using var handler = new BlockingHandler();
        using var http = new HttpClient(handler);
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key", TimeoutSeconds = 15 }));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var first = client.SearchAsync("Clinic", "es", cancellation.Token);
        var others = Enumerable.Range(0, 3).Select(_ => client.SearchAsync("Clinic", "es", Ct)).ToArray();
        await handler.AllStarted.Task.WaitAsync(Ct);
        Assert.Equal("Ai.Places.Busy", (await client.SearchAsync("Clinic", "es", Ct)).Error.Code);
        Assert.Equal(4, handler.Calls);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        handler.Release.TrySetResult();
        await Task.WhenAll(others);
        Assert.True((await client.SearchAsync("Clinic", "es", Ct)).IsSuccess);
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public async Task An_oversized_response_is_rejected_without_exposing_its_body()
    {
        using var handler = new Handler(new string('x', 32769));
        using var http = new HttpClient(handler);
        using var client = new GooglePlacesClient(http, Options.Create(new GooglePlacesOptions { ApiKey = "fictional-key" }));
        Assert.Equal("Ai.Places.ResponseInvalid", (await client.SearchAsync("Clinic", "es", Ct)).Error.Code);
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        private int calls;
        public int Calls => Volatile.Read(ref calls);
        public TaskCompletionSource AllStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 4) AllStarted.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Handler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? Key { get; private set; }
        public string? Mask { get; private set; }
        public JsonElement? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Url = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.GetValues("X-Goog-Api-Key").Single();
            Mask = request.Headers.GetValues("X-Goog-FieldMask").Single();
            Body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
