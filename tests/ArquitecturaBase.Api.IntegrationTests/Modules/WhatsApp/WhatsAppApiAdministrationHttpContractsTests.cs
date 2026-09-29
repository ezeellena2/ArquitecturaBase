using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp de los contratos HTTP de administración (ApiAdministrationHttpContractsTests): las
/// rutas del código por WhatsApp del perfil tienen el mismo límite por IP que las del correo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppApiAdministrationHttpContractsTests(ApiFactory factory)
{
    [Theory]
    [InlineData("POST", "/api/me/whatsapp/code", "RateLimiting:LoginCodePermitLimit")]
    [InlineData("PUT", "/api/me/whatsapp", "RateLimiting:LoginVerifyPermitLimit")]
    public async Task Profile_code_routes_return_429_with_retry_after_when_the_ip_limit_is_reached(
        string method,
        string route,
        string setting)
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, "1"));
        using var client = api.CreateClient();

        using var first = await client.SendAsync(new HttpMethod(method), route, JsonContent.Create(new { }), language: "es");
        using var second = await client.SendAsync(new HttpMethod(method), route, JsonContent.Create(new { }), language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryAfter").GetInt32() > 0);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
