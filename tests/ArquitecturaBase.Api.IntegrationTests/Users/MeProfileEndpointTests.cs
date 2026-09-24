using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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
    public async Task Missing_profile_returns_a_not_found_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/api/me", userId: Guid.CreateVersion7().ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Users.User.NotFound", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unsupported_profile_method_reports_both_read_and_write_as_allowed()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Patch, "/api/me");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "PUT"], response.Content.Headers.Allow.Order(StringComparer.Ordinal));
        Assert.Equal("Http.MethodNotAllowed", problem.GetProperty("code").GetString());
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
    public async Task Missing_profile_update_returns_a_not_found_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Put,
            "/api/me",
            JsonContent.Create(new { displayName = "Ana", culture = "es", timeZoneId = "America/Argentina/Buenos_Aires" }),
            userId: Guid.CreateVersion7().ToString("D", System.Globalization.CultureInfo.InvariantCulture));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Users.User.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Profile_update_requires_a_valid_json_body()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("cuerpoperfil"));

        using var missing = await client.SendWithTokenAsync(HttpMethod.Put, "/api/me", tokens.AccessToken);
        using var malformed = await SendBodyAsync("{", "application/json");
        using var plain = await SendBodyAsync("{}", "text/plain");
        using var form = await SendBodyAsync("culture=es&timeZoneId=America%2FArgentina%2FBuenos_Aires", "application/x-www-form-urlencoded");

        foreach (var response in new[] { missing, malformed })
        {
            var problem = await response.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        foreach (var response in new[] { plain, form })
        {
            var problem = await response.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
            Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        async Task<HttpResponseMessage> SendBodyAsync(string body, string mediaType)
        {
            using var request = new HttpRequestMessage(HttpMethod.Put, "/api/me")
            {
                Content = new StringContent(body, Encoding.UTF8, mediaType),
            };
            request.Headers.Authorization = new("Bearer", tokens.AccessToken);
            return await client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Profile_update_does_not_log_the_display_name()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging
                .AddFakeLogging()
                .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace))));
        using var client = api.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("logperfil"));
        var privateName = "NombrePrivado" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new { displayName = privateName, culture = "es", timeZoneId = "America/Argentina/Buenos_Aires" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var logged = api.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => string.Join("\n",
            [
                record.Message,
                record.Exception?.ToString() ?? string.Empty,
                .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
            ]))
            .ToList();
        Assert.NotEmpty(logged);
        Assert.All(logged, entry => Assert.DoesNotContain(privateName, entry, StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_existing_endpoint_rejects_a_display_name_over_the_limit()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("nombremuylargo"));

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Put,
            "/api/me",
            tokens.AccessToken,
            new
            {
                displayName = new string('A', ValidationRules.DisplayNameMaxLength + 1),
                culture = "es",
                timeZoneId = "America/Argentina/Buenos_Aires",
            },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(problem.GetProperty("errors").TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task Repository_profile_update_truncates_the_name_and_autosaves_identity()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("perfilrepo");
        var tokens = await client.LoginAsync(factory, email);
        var longName = new string('A', ValidationRules.DisplayNameMaxLength + 10);

        await factory.ExecuteScopeAsync(async services =>
        {
            var user = await services.GetRequiredService<IUserReader>()
                .FindByEmailAsync(Email.Create(email).Value, TestContext.Current.CancellationToken);
            await services.GetRequiredService<IUserRepository>().UpdateProfileAsync(
                user!.Id, longName, "en", "America/Sao_Paulo", TestContext.Current.CancellationToken);
            return true;
        });

        using var response = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var me = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new string('A', ValidationRules.DisplayNameMaxLength), me.GetProperty("displayName").GetString());
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
