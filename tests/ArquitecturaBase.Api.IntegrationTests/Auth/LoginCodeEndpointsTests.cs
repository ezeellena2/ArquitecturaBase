using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class LoginCodeEndpointsTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize?client_id=web";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requesting_a_code_returns_202_and_emails_the_code()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("request");

        using var response = await client.PostJsonAsync("/account/login-code", new { email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var code = CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email));
        Assert.Matches("^[0-9]{6}$", code);

        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.SingleAsync(loginCode => loginCode.Email == email, Ct));
        Assert.DoesNotContain(code, stored.CodeHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Right_code_starts_a_persistent_secure_session()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("verify");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ReturnUrl, body.GetProperty("returnUrl").GetString());

        var cookie = Assert.Single(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_code_returns_the_attempts_left_in_the_requested_language()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("wrong");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code = code == "000000" ? "111111" : "000000", returnUrl = ReturnUrl },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.LoginCode.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("El código no es válido.", problem.GetProperty("detail").GetString());
        Assert.Equal(4, problem.GetProperty("attemptsLeft").GetInt32());
    }

    [Fact]
    public async Task Return_url_outside_the_authorize_endpoint_is_rejected()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("returnurl");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify",
            new { email, code, returnUrl = "https://evil.example/connect/authorize" },
            language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("La dirección de retorno no es válida.", problem.GetProperty("errors").GetProperty("returnUrl")[0].GetString());
    }

    [Fact]
    public async Task Account_endpoints_only_accept_json()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post,
            "/account/login-code",
            new FormUrlEncodedContent([new("email", TestEmails.Unique("form"))]));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_429_with_the_seconds_left()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var email = TestEmails.Unique("cooldown");

        using var first = await client.PostJsonAsync("/account/login-code", new { email });
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var second = await client.PostJsonAsync("/account/login-code", new { email }, language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(60, (await first.ReadJsonAsync()).GetProperty("resendAfterSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Auth.LoginCode.ResendTooSoon", problem.GetProperty("code").GetString());
        Assert.Equal(45, problem.GetProperty("retryAfter").GetInt32());
        Assert.Equal("Esperá un momento antes de pedir otro código.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Rate_limiter_rejects_with_a_problem_and_retry_after()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginCodePermitLimit", "1"));
        using var client = api.CreateClient();

        using var first = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("limit") });
        using var second = await client.PostJsonAsync("/account/login-code", new { email = TestEmails.Unique("limit") }, language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryAfter").GetInt32() > 0);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
