using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Consumo de WhatsApp (T7, PR14): the <c>pricing_analytics</c> of the account for one number, read once with a bounded time; an error, a
/// timeout or a body it cannot read is <c>WhatsApp.Usage.Unavailable</c>, and the log never carries the token or the number. Without Docker.
/// </summary>
public sealed class WhatsAppPricingAnalyticsClientTests
{
    private const string Token = "test-usage-token";
    private const string Phone = "5493406401234";
    private static readonly DateTime StartUtc = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime EndUtc = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_asks_the_account_for_the_daily_points_of_the_number_by_category_with_the_token_as_bearer()
    {
        using var handler = new StubFlowHttpHandler(_ => Json(HttpStatusCode.OK, """
            {"currency":"USD","pricing_analytics":{"data":[{"data_points":[
              {"start":1759287600,"end":1759374000,"country":"AR","pricing_type":"REGULAR","pricing_category":"MARKETING","volume":70,"cost":4.2},
              {"start":1759287600,"end":1759374000,"country":"AR","pricing_type":"FREE_CUSTOMER_SERVICE","pricing_category":"SERVICE","volume":1200,"cost":0}]},
              {"data_points":[{"start":1759374000,"end":1759460400,"country":"AR","pricing_type":"REGULAR","pricing_category":"MARKETING","volume":50,"cost":3.22,"tier":"0:250000"}]}]},
             "id":"200000000000002"}
            """));
        var (client, _, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.True(read.IsSuccess);
        Assert.Equal("USD", read.Value.Currency);
        Assert.Equal([new("MARKETING", 70, 4.2m), new("SERVICE", 1200, 0m), new("MARKETING", 50, 3.22m)], read.Value.Points);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(("Bearer", Token), (request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));
        Assert.Equal("https://graph.facebook.com/v25.0/200000000000002", request.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(
            """?fields=currency,pricing_analytics.start(1790823600).end(1793502000).granularity(DAILY).phone_numbers(["5493406401234"]).dimensions(["PRICING_CATEGORY"])""",
            Uri.UnescapeDataString(request.RequestUri.Query));
        // The query goes encoded: its parentheses, brackets and quotes do not reach Meta as they are.
        Assert.DoesNotContain("\"", request.RequestUri.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("[", request.RequestUri.Query, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task An_answer_that_is_not_a_success_is_unavailable_and_logged_without_the_token_or_the_number(HttpStatusCode status)
    {
        using var handler = new StubFlowHttpHandler(_ => Json(status, """{"error":{"message":"(#100) Invalid parameter","code":100}}"""));
        var (client, logger, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
        var entry = Assert.Single(logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Phone, entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "InvalidToken")]
    [InlineData(HttpStatusCode.Forbidden, "MissingPermission")]
    public async Task A_refused_token_or_permission_also_reaches_the_health(HttpStatusCode status, string problem)
    {
        using var handler = new StubFlowHttpHandler(_ => new HttpResponseMessage(status));
        var (client, _, health) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
        Assert.Equal(problem, health.TokenProblem?.ToString());
        Assert.Equal(status == HttpStatusCode.Forbidden, health.MissingPermissions.Management);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"pricing_analytics":{"data":[]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":[]}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":{}}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":{}}]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":[{"volume":1,"cost":0}]}]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":[{"pricing_category":"SERVICE","volume":"1","cost":0}]}]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":[{"pricing_category":"SERVICE","volume":1.5,"cost":0}]}]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":[{"pricing_category":"SERVICE","volume":1}]}]}}""")]
    public async Task A_body_it_cannot_read_is_unavailable(string body)
    {
        using var handler = new StubFlowHttpHandler(_ => Json(HttpStatusCode.OK, body));
        var (client, logger, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
        Assert.Equal(LogLevel.Warning, Assert.Single(logger.Collector.GetSnapshot()).Level);
    }

    [Theory]
    [InlineData("""{"currency":"USD","id":"200000000000002"}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[]}}""")]
    [InlineData("""{"currency":"USD","pricing_analytics":{"data":[{"data_points":[]}]}}""")]
    public async Task A_range_without_messages_has_no_points(string body)
    {
        using var handler = new StubFlowHttpHandler(_ => Json(HttpStatusCode.OK, body));
        var (client, _, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.True(read.IsSuccess);
        Assert.Equal("USD", read.Value.Currency);
        Assert.Empty(read.Value.Points);
    }

    [Fact]
    public async Task A_body_larger_than_its_limit_is_unavailable_without_reading_it_whole()
    {
        var huge = "{\"currency\":\"USD\",\"padding\":\"" + new string('x', 2 * 1024 * 1024) + "\"}";
        using var handler = new StubFlowHttpHandler(_ => Json(HttpStatusCode.OK, huge));
        var (client, _, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
    }

    [Fact]
    public async Task Meta_silent_past_the_deadline_is_unavailable_and_not_before()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 27, 15, 0, 0, TimeSpan.Zero));
        using var handler = new SilentHandler();
        var (client, logger, _) = Create(handler, time);

        var reading = client.ReadAsync(Phone, StartUtc, EndUtc, Ct);
        await handler.Started.Task.WaitAsync(Ct);
        time.Advance(WhatsAppPricingAnalyticsClient.Deadline - TimeSpan.FromSeconds(1));
        Assert.False(reading.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(1));
        var read = await reading.WaitAsync(Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
        Assert.Contains("did not answer", Assert.Single(logger.Collector.GetSnapshot()).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_failure_is_unavailable()
    {
        using var handler = new StubFlowHttpHandler(_ => throw new HttpRequestException("Connection refused", null, HttpStatusCode.ServiceUnavailable));
        var (client, _, _) = Create(handler);

        var read = await client.ReadAsync(Phone, StartUtc, EndUtc, Ct);

        Assert.Equal(WhatsAppUsageErrors.Unavailable, read.Error);
    }

    [Fact]
    public async Task Cancelling_the_request_is_not_a_failure_of_meta()
    {
        using var handler = new SilentHandler();
        var (client, _, _) = Create(handler);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var reading = client.ReadAsync(Phone, StartUtc, EndUtc, cancellation.Token);
        await handler.Started.Task.WaitAsync(Ct);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reading);
    }

    [Theory]
    [InlineData(null, "test-usage-token", false)]
    [InlineData("WABA", "test-usage-token", false)]
    [InlineData("200000000000002", null, false)]
    [InlineData("200000000000002", "test-usage-token", true)]
    public void It_can_be_asked_only_with_the_account_id_and_the_token(string? wabaId, string? token, bool configured)
    {
        using var handler = new StubFlowHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var client = new WhatsAppPricingAnalyticsClient(http, Options.Create(new WhatsAppOptions { PhoneNumberId = "100000000000001", WabaId = wabaId, AccessToken = token }),
            new WhatsAppHealth(), TimeProvider.System, new FakeLogger<WhatsAppPricingAnalyticsClient>());

        Assert.Equal(configured, client.IsConfigured);
    }

    private static (WhatsAppPricingAnalyticsClient Client, FakeLogger<WhatsAppPricingAnalyticsClient> Logger, WhatsAppHealth Health) Create(
        HttpMessageHandler handler, TimeProvider? time = null)
    {
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = WhatsAppCloudClient.GraphApiAddress };
        var logger = new FakeLogger<WhatsAppPricingAnalyticsClient>();
        var health = new WhatsAppHealth();
        var options = Options.Create(new WhatsAppOptions
        {
            PhoneNumberId = "100000000000001", WabaId = "200000000000002", AccessToken = Token, GraphApiVersion = "v25.0",
        });
        return (new WhatsAppPricingAnalyticsClient(http, options, health, time ?? TimeProvider.System, logger), logger, health);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class SilentHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
