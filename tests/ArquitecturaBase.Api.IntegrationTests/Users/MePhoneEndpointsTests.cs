using System.Globalization;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>Desvincular el número propio conserva otro medio de ingreso y las sesiones, e invalida los enlaces.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class MePhoneEndpointsTests(ApiFactory factory)
{
    private const string LinkUrl = "/api/me/whatsapp";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unlinking_the_only_way_to_sign_in_answers_409_and_keeps_the_number()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(phone: phone);

        using var response = await UnlinkAsync(client, user, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.LastLoginMethodCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "Es tu único medio de ingreso: para desvincularlo, primero agregá un correo.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(phone.Value, (await AccountAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task An_email_that_is_not_verified_does_not_allow_unlinking_the_number()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(phone: phone);
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetEmailAsync(
                user.Id, Email.Create(TestEmails.Unique("sinverificar")).Value, confirmed: false, Ct);
        });

        using var response = await UnlinkAsync(client, user);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.LastLoginMethodCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(phone.Value, (await AccountAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task With_google_linked_the_number_can_be_unlinked()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());
        await factory.InTransactionAsync(async services =>
        {
            var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            await services.GetRequiredService<IUserRepository>().AddExternalLoginAsync(
                user.Id, new ExternalLogin(ExternalLoginProviders.Google, providerKey, Email: null, EmailVerified: false, DisplayName: null), Ct);
        });

        using var response = await UnlinkAsync(client, user);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task Unlinking_an_account_without_a_number_answers_204()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("sinnumero"));

        using var response = await UnlinkAsync(client, user);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task With_a_verified_email_unlinking_removes_the_number_and_voids_pending_links_but_keeps_the_sessions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("profile-unlink"));
        using var before = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var userId = (await before.ReadJsonAsync()).GetProperty("id").GetGuid();
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetPhoneAsync(userId, TestPhones.Unique(), confirmed: true, Ct);
        });
        var url = await TestLoginLinks.IssueUrlAsync(client, userId);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, LinkUrl, tokens.AccessToken);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profile = await me.ReadJsonAsync();
        using var redeem = await client.PostJsonAsync("/account/login-link/redeem", new { token = TestLoginLinks.TokenOf(url) });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phoneNumber").ValueKind);
        Assert.False(profile.GetProperty("phoneNumberConfirmed").GetBoolean());
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith(ApiFactory.WebRedirectUri + "?code=", authorize.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unlinking_requires_a_bearer_session()
    {
        using var client = factory.CreateClient();

        using var response = await client.DeleteAsync(LinkUrl, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public void Profile_unlink_requires_only_a_session_and_has_no_ip_rate_limit()
    {
        var endpoint = Assert.Single(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText == "api/me/whatsapp"
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("DELETE") == true);

        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(authorization.Policy);
        Assert.Null(authorization.Roles);
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Null(endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>());
    }

    [Fact]
    public async Task Unlinking_a_missing_account_answers_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Delete, LinkUrl,
            userId: Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(UserErrors.NotFoundCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private static Task<HttpResponseMessage> UnlinkAsync(HttpClient client, UserAccount user, string? language = null) =>
        client.SendAsync(HttpMethod.Delete, LinkUrl, language: language,
            userId: user.Id.ToString("D", CultureInfo.InvariantCulture));

    private Task<UserAccount> CreateAccountAsync(string? email = null, PhoneNumber? phone = null) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName: null, "es", Ct));

    private Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct)));
}
