using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>HTTP shape shared by the account routes before their MVC migration.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class AuthConnectAccountContractTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/account/login-code")]
    [InlineData("/account/login-code/whatsapp")]
    [InlineData("/account/login-code/verify")]
    [InlineData("/account/login-link/preview")]
    [InlineData("/account/login-link/redeem")]
    public async Task Json_account_posts_reject_malformed_and_missing_bodies_as_problems(string route)
    {
        using var client = factory.CreateClient();

        using var malformed = await client.SendAsync(
            HttpMethod.Post, route, new StringContent("{", Encoding.UTF8, "application/json"), language: "es");
        using var missing = await client.SendAsync(HttpMethod.Post, route, language: "es");

        await AssertInvalidRequestAsync(malformed);
        await AssertInvalidRequestAsync(missing);
    }

    [Theory]
    [InlineData("/account/login-code/whatsapp")]
    [InlineData("/account/login-code/verify")]
    public async Task Remaining_code_posts_reject_form_bodies(string route)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, route, new FormUrlEncodedContent([new("code", "123456")]), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Code_requests_return_json_without_a_location_header()
    {
        using var client = factory.CreateClient();

        using var email = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("contract") });
        using var whatsapp = await client.PostJsonAsync(
            "/account/login-code/whatsapp", new { country = "AR", number = TestPhones.AsTypedLocally(TestPhones.Unique()) });

        Assert.Equal(HttpStatusCode.Accepted, email.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, whatsapp.StatusCode);
        Assert.Equal("application/json", email.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", whatsapp.Content.Headers.ContentType?.MediaType);
        Assert.Null(email.Headers.Location);
        Assert.Null(whatsapp.Headers.Location);
    }

    [Fact]
    public async Task Verify_rate_limit_has_matching_retry_after_header_and_body()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginVerifyPermitLimit", "1"));
        using var client = api.CreateClient();
        var body = new { email = TestEmails.Unique("contract-limit"), code = "000000", returnUrl = "/connect/authorize?client_id=web" };

        using var first = await client.PostJsonAsync("/account/login-code/verify", body);
        using var rejected = await client.PostJsonAsync("/account/login-code/verify", body, language: "es");
        var problem = await rejected.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
        var retryAfter = rejected.Headers.RetryAfter?.Delta ?? throw new InvalidOperationException("Missing Retry-After header.");
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.Equal((int)retryAfter.TotalSeconds, problem.GetProperty("retryAfter").GetInt32());
    }

    [Theory]
    [InlineData("/account/login-methods", "GET")]
    [InlineData("/account/login-code", "POST")]
    [InlineData("/account/login-code/whatsapp", "POST")]
    [InlineData("/account/login-code/verify", "POST")]
    [InlineData("/account/login-link/preview", "POST")]
    [InlineData("/account/login-link/redeem", "POST")]
    [InlineData("/account/external/google", "GET")]
    [InlineData("/account/external/callback", "GET")]
    public async Task Every_account_route_rejects_an_unmapped_method(string route, string allowedMethod)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Delete, route, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Contains(allowedMethod, response.Content.Headers.Allow);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.MethodNotAllowed", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("/account/login-link/preview")]
    [InlineData("/account/login-link/redeem")]
    public async Task Login_link_routes_stay_mapped_when_whatsapp_is_off(string route)
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var token = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        using var response = await client.PostJsonAsync(route, new { token }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Auth.LoginLink.Invalid", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    private static async Task AssertInvalidRequestAsync(HttpResponseMessage response)
    {
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
