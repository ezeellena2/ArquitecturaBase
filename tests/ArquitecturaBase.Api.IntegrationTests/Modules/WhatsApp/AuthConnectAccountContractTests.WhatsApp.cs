using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// La parte del módulo WhatsApp de AuthConnectAccountContractTests: el pedido del código por WhatsApp tiene la misma
/// forma HTTP que las rutas de la cuenta del núcleo. Lleva el namespace de la clase del núcleo, no el de su carpeta, para
/// ser otra parte de la misma clase.
/// </summary>
public sealed partial class AuthConnectAccountContractTests
{
    private const string WhatsAppCodeRoute = "/account/login-code/whatsapp";

    [Fact]
    public async Task The_whatsapp_code_post_rejects_malformed_and_missing_bodies_as_problems()
    {
        using var client = factory.CreateClient();

        using var malformed = await client.SendAsync(
            HttpMethod.Post, WhatsAppCodeRoute, new StringContent("{", Encoding.UTF8, "application/json"), language: "es");
        using var missing = await client.SendAsync(HttpMethod.Post, WhatsAppCodeRoute, language: "es");

        await AssertInvalidRequestAsync(malformed);
        await AssertInvalidRequestAsync(missing);
    }

    [Fact]
    public async Task The_whatsapp_code_post_rejects_form_bodies()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, WhatsAppCodeRoute, new FormUrlEncodedContent([new("code", "123456")]), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_whatsapp_code_post_rejects_plain_text_bodies()
    {
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(
            HttpMethod.Post, WhatsAppCodeRoute, new StringContent("{}", Encoding.UTF8, "text/plain"), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_whatsapp_code_request_returns_json_without_a_location_header()
    {
        using var client = factory.CreateClient();

        using var whatsapp = await client.PostJsonAsync(
            WhatsAppCodeRoute, new { country = "AR", number = TestPhones.AsTypedLocally(TestPhones.Unique()) });

        Assert.Equal(HttpStatusCode.Accepted, whatsapp.StatusCode);
        Assert.Equal("application/json", whatsapp.Content.Headers.ContentType?.MediaType);
        Assert.Null(whatsapp.Headers.Location);
    }

    [Fact]
    public async Task The_whatsapp_code_route_rejects_an_unmapped_method()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Delete, WhatsAppCodeRoute, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains("POST", response.Content.Headers.Allow);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.MethodNotAllowed", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
