using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

[Collection(ApiTestGroup.Name)]
public sealed class MeProfileEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Me_includes_the_last_successful_login()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("ultimo"));

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, me.GetProperty("lastLoginAtUtc").GetDateTime());
    }

    [Fact]
    public async Task Updating_the_profile_saves_the_name_the_language_and_the_time_zone()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("perfil"));

        using var update = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { displayName = "Ana", culture = "en", timeZoneId = "America/Sao_Paulo" });
        using var read = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
        Assert.Equal("Ana", me.GetProperty("displayName").GetString());
        Assert.Equal("en", me.GetProperty("culture").GetString());
        Assert.Equal("America/Sao_Paulo", me.GetProperty("timeZoneId").GetString());
    }

    [Fact]
    public async Task A_language_that_is_not_supported_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("idioma"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { culture = "fr", timeZoneId = "America/Argentina/Buenos_Aires" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Elegí un idioma disponible.", problem.GetProperty("errors").GetProperty("culture")[0].GetString());
    }

    [Fact]
    public async Task A_time_zone_that_does_not_exist_is_rejected()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("zona"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { culture = "es", timeZoneId = "Marte/Olympus" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Elegí una zona horaria válida.", problem.GetProperty("errors").GetProperty("timeZoneId")[0].GetString());
    }

    [Fact]
    public async Task Updating_the_profile_without_a_token_returns_a_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Put,
            "/api/me",
            System.Net.Http.Json.JsonContent.Create(new { culture = "es", timeZoneId = "America/Argentina/Buenos_Aires" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
    }
}
