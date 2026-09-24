using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.Routing;
using ArquitecturaBase.Application.Interfaces.Integrations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

[Collection(ApiTestGroup.Name)]
public sealed class MvcWhatsAppRouteConventionTests(ApiFactory factory)
{
    private const string ProbePrefix = "/api/_mvc-whatsapp-probe";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Unavailable_whatsapp_actions_are_absent_from_mvc_endpoint_data_source(
        bool whatsappEnabled, bool webhookEnabled)
    {
        await using var api = ProbeApi(whatsappEnabled, webhookEnabled);
        using var client = api.CreateClient();
        var availability = api.Services.GetRequiredService<IWhatsAppAvailability>();

        Assert.Equal(whatsappEnabled, availability.IsEnabled);
        Assert.Equal(webhookEnabled, availability.IsWebhookEnabled);
        AssertRouteCount(api, "POST", $"{ProbePrefix}/account/login-code/whatsapp", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", $"{ProbePrefix}/api/me/whatsapp/code", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "GET", $"{ProbePrefix}/webhooks/whatsapp", webhookEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", $"{ProbePrefix}/webhooks/whatsapp", webhookEnabled ? 1 : 0);

        // The production Minimal API routes still have exactly one registration while this spike runs.
        AssertRouteCount(api, "POST", "/account/login-code/whatsapp", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", "/api/me/whatsapp/code", whatsappEnabled ? 1 : 0);
        AssertRouteCount(api, "GET", "/webhooks/whatsapp", webhookEnabled ? 1 : 0);
        AssertRouteCount(api, "POST", "/webhooks/whatsapp", webhookEnabled ? 1 : 0);
    }

    private WebApplicationFactory<Program> ProbeApi(bool whatsappEnabled, bool webhookEnabled)
    {
        _ = factory.Services.GetRequiredService<ApplicationPartManager>();

        return factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:PhoneNumberId", whatsappEnabled ? ApiFactory.WhatsAppPhoneNumberId : "")
            .UseSetting("WhatsApp:AppSecret", webhookEnabled ? ApiFactory.WhatsAppAppSecret : "")
            .UseSetting("WhatsApp:VerifyToken", webhookEnabled ? ApiFactory.WhatsAppVerifyToken : "")
            .ConfigureTestServices(services => services.AddControllers()
                .AddApplicationPart(typeof(MvcWhatsAppRouteProbeController).Assembly)
                .AddConditionalWhatsAppRoutes()));
    }

    private static void AssertRouteCount(WebApplicationFactory<Program> api, string method, string path, int expected)
    {
        var count = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Count(endpoint => string.Equals(endpoint.RoutePattern.RawText?.Trim('/'), path.Trim('/'),
                    StringComparison.OrdinalIgnoreCase)
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Contains(method, StringComparer.OrdinalIgnoreCase) == true);

        Assert.Equal(expected, count);
    }
}

/// <summary>Only the test host discovers these actions; their prefix cannot duplicate production routes.</summary>
[ApiController]
[Route("api/_mvc-whatsapp-probe")]
public sealed class MvcWhatsAppRouteProbeController : ControllerBase
{
    [HttpPost("account/login-code/whatsapp")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    public IActionResult LoginCode() => Ok();

    [HttpPost("api/me/whatsapp/code")]
    [WhatsAppRoute(WhatsAppRouteFeature.Messaging)]
    public IActionResult ProfileCode() => Ok();

    [HttpGet("webhooks/whatsapp")]
    [WhatsAppRoute(WhatsAppRouteFeature.Webhook)]
    public IActionResult VerifyWebhook() => Ok();

    [HttpPost("webhooks/whatsapp")]
    [WhatsAppRoute(WhatsAppRouteFeature.Webhook)]
    public IActionResult ReceiveWebhook() => Ok();
}
