using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// Protege las rutas que se agregan al iniciar la Api. Un 404 por sí solo no alcanza:
/// también comprobamos que el endpoint no exista en el registro de rutas.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppRouteContractsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Conditional_routes_follow_the_two_startup_switches(bool whatsappEnabled, bool webhookEnabled)
    {
        await using var api = ConfiguredApi(whatsappEnabled, webhookEnabled);
        using var client = api.CreateClient();
        var availability = api.Services.GetRequiredService<IWhatsAppAvailability>();

        Assert.Equal(whatsappEnabled, availability.IsEnabled);
        Assert.Equal(webhookEnabled, availability.IsWebhookEnabled);
        AssertRouteCount(api, "POST", "/account/login-code/whatsapp", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", "/api/me/whatsapp/code", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "GET", "/webhooks/whatsapp", webhookEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", "/webhooks/whatsapp", webhookEnabled ? 1 : 0);

        var phone = TestPhones.Unique();
        using var loginCode = await client.PostJsonAsync(
            "/account/login-code/whatsapp", new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        using var linkCode = await client.PostJsonAsync(
            "/api/me/whatsapp/code", new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        using var verification = await client.GetAsync(
            $"/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token={ApiFactory.WhatsAppVerifyToken}&hub.challenge=contract-123", Ct);
        using var webhookPost = await client.PostJsonAsync("/webhooks/whatsapp", new { });

        if (whatsappEnabled)
        {
            Assert.Equal(HttpStatusCode.Accepted, loginCode.StatusCode);
            var body = await loginCode.ReadJsonAsync();
            Assert.Equal(phone.Value, body.GetProperty("phone").GetString());
            Assert.Equal(
                ["maskedPhone", "phone", "resendAfterSeconds"],
                body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
            Assert.Equal(HttpStatusCode.Unauthorized, linkCode.StatusCode);
            Assert.Equal("Http.Unauthorized", (await linkCode.ReadJsonAsync()).GetProperty("code").GetString());
        }
        else
        {
            await AssertFrameworkNotFoundAsync(loginCode);
            await AssertFrameworkNotFoundAsync(linkCode);
        }

        if (webhookEnabled)
        {
            Assert.Equal(HttpStatusCode.OK, verification.StatusCode);
            Assert.Equal("text/plain", verification.Content.Headers.ContentType?.MediaType);
            Assert.Equal("contract-123", await verification.Content.ReadAsStringAsync(Ct));
            Assert.Equal(HttpStatusCode.Unauthorized, webhookPost.StatusCode);
        }
        else
        {
            await AssertFrameworkNotFoundAsync(verification);
            await AssertFrameworkNotFoundAsync(webhookPost);
        }

        using var wrongLoginMethod = await client.GetAsync("/account/login-code/whatsapp", Ct);
        using var wrongLinkMethod = await client.GetAsync("/api/me/whatsapp/code", Ct);
        using var wrongWebhookMethod = await client.PutAsJsonAsync("/webhooks/whatsapp", new { }, Ct);
        await AssertMethodMismatchAsync(wrongLoginMethod, whatsappEnabled);
        await AssertMethodMismatchAsync(wrongLinkMethod, whatsappEnabled);
        await AssertMethodMismatchAsync(wrongWebhookMethod, webhookEnabled);
    }

    [Fact]
    public async Task Disabling_whatsapp_keeps_profile_unlink_and_login_links_registered()
    {
        await using var api = ConfiguredApi(whatsappEnabled: false, webhookEnabled: false);
        using var client = api.CreateClient();

        AssertRouteCount(api, "PUT", "/api/me/whatsapp", 1);
        AssertRouteCount(api, "DELETE", "/api/me/whatsapp", 1);
        AssertRouteCount(api, "POST", "/account/login-link/preview", 1);
        AssertRouteCount(api, "POST", "/account/login-link/redeem", 1);
        AssertRouteCount(api, "GET", "/account/login-methods", 1);
        AssertRouteCount(api, "POST", "/account/login-code/verify", 1);
        AssertRouteCount(api, "GET", "/api/me", 1);

        using var confirm = await client.PutAsJsonAsync(
            "/api/me/whatsapp", new { phone = "+5493515551234", code = "000000" }, Ct);
        using var unlink = await client.DeleteAsync("/api/me/whatsapp", Ct);
        using var preview = await client.PostJsonAsync("/account/login-link/preview", new { token = "invented" });
        using var redeem = await client.PostJsonAsync("/account/login-link/redeem", new { token = "invented" });
        using var methods = await client.GetAsync("/account/login-methods", Ct);
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("whatsapp-off-contract"));
        using var authenticatedConfirm = await client.SendWithTokenAsync(
            HttpMethod.Put, "/api/me/whatsapp", tokens.AccessToken,
            new { phone = "+5493515551234", code = "000000" });
        using var authenticatedUnlink = await client.SendWithTokenAsync(
            HttpMethod.Delete, "/api/me/whatsapp", tokens.AccessToken);

        Assert.Equal(HttpStatusCode.Unauthorized, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unlink.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, authenticatedConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, authenticatedUnlink.StatusCode);
        var methodsBody = await methods.ReadJsonAsync();
        Assert.False(methodsBody.GetProperty("whatsapp").GetBoolean());
        Assert.Empty(methodsBody.GetProperty("whatsappCountries").EnumerateArray());
    }

    private WebApplicationFactory<Program> ConfiguredApi(bool whatsappEnabled, bool webhookEnabled) =>
        factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:PhoneNumberId", whatsappEnabled ? ApiFactory.WhatsAppPhoneNumberId : "")
            .UseSetting("WhatsApp:AppSecret", webhookEnabled ? ApiFactory.WhatsAppAppSecret : "")
            .UseSetting("WhatsApp:VerifyToken", webhookEnabled ? ApiFactory.WhatsAppVerifyToken : ""));

    private static void AssertRouteCount(WebApplicationFactory<Program> api, string method, string path, int expected)
    {
        var endpoints = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(
                endpoint.RoutePattern.RawText?.Trim('/'), path.Trim('/'), StringComparison.OrdinalIgnoreCase))
            .Where(endpoint => endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                .Contains(method, StringComparer.OrdinalIgnoreCase) == true);

        Assert.Equal(expected, endpoints.Count());
    }

    private static async Task AssertFrameworkNotFoundAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    private static async Task AssertMethodMismatchAsync(HttpResponseMessage response, bool routeRegistered)
    {
        Assert.Equal(routeRegistered ? HttpStatusCode.MethodNotAllowed : HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(
            routeRegistered ? "Http.MethodNotAllowed" : "Http.NotFound",
            problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
