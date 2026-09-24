using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppHttpContractsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Webhook_get_and_post_share_the_same_rate_limit()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("RateLimiting:WhatsAppWebhookPermitLimit", "1"));
        using var client = api.CreateClient();

        using var verification = await client.GetAsync(
            $"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}&hub.challenge=rate-limit", Ct);
        using var post = await client.PostJsonAsync("/webhooks/whatsapp", new { });

        Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
        await AssertProblemAsync(post, HttpStatusCode.TooManyRequests, "Http.TooManyRequests");
    }

    [Fact]
    public async Task A_signed_webhook_keeps_accepting_raw_json_bytes_with_text_plain_content_type()
    {
        using var client = factory.CreateClient();
        var waId = MetaWebhook.UniqueWaId();
        var bsuid = MetaWebhook.UniqueBsuid();
        var messageId = MetaWebhook.UniqueWaMessageId();
        var body = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId, bsuid, "Contrato")],
            messages: [MetaWebhook.Text(
                messageId, waId, bsuid,
                MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime), "Hola por text/plain")]);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
        var persistedBody = await factory.ExecuteDbContextAsync(db => db.WhatsAppMessages
            .Where(message => message.WaMessageId == messageId)
            .Select(message => message.Body)
            .SingleAsync(Ct));
        Assert.Equal("Hola por text/plain", persistedBody);
    }

    [Fact]
    public async Task A_whatsapp_login_code_request_with_a_non_json_content_type_is_rejected()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{\"country\":\"AR\",\"number\":\"3515551234\"}", Encoding.UTF8, "text/plain");

        using var response = await client.SendAsync(HttpMethod.Post, "/account/login-code/whatsapp", content);

        await AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "Request.Invalid");
    }

    [Fact]
    public async Task A_whatsapp_login_code_request_with_malformed_or_missing_json_is_rejected()
    {
        using var client = factory.CreateClient();
        using var malformed = await client.SendAsync(
            HttpMethod.Post, "/account/login-code/whatsapp", new StringContent("{", Encoding.UTF8, "application/json"));
        using var missing = await client.PostAsync("/account/login-code/whatsapp", content: null, cancellationToken: Ct);

        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest, "Request.Invalid");
        await AssertProblemAsync(missing, HttpStatusCode.BadRequest, "Request.Invalid");
    }

    [Fact]
    public async Task An_authenticated_profile_code_request_with_malformed_or_missing_json_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("invalid-whatsapp-body"));

        using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/me/whatsapp/code")
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        malformedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var malformed = await client.SendAsync(malformedRequest, Ct);

        using var missingRequest = new HttpRequestMessage(HttpMethod.Post, "/api/me/whatsapp/code");
        missingRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        using var missing = await client.SendAsync(missingRequest, Ct);

        await AssertProblemAsync(malformed, HttpStatusCode.BadRequest, "Request.Invalid");
        await AssertProblemAsync(missing, HttpStatusCode.BadRequest, "Request.Invalid");
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    public async Task A_signed_webhook_with_malformed_or_missing_json_still_answers_200(string text)
    {
        using var client = factory.CreateClient();
        var body = Encoding.UTF8.GetBytes(text);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(code, problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
