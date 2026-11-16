using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.Contracts.Users;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.RateLimiting;
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

/// <summary>
/// Desvincular el número propio conserva otro medio de ingreso y las sesiones, e invalida los enlaces. Con un correo
/// verificado, lo confirma el código que se le manda a ese correo (N11).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MePhoneEndpointsTests(ApiFactory factory)
{
    private const string LinkUrl = "/api/me/whatsapp";
    private const string UnlinkCodeUrl = "/api/me/email/confirmation-code";
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
        var email = TestEmails.Unique("profile-unlink");
        var tokens = await client.LoginAsync(factory, email);
        using var before = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var userId = (await before.ReadJsonAsync()).GetProperty("id").GetGuid();
        await factory.InTransactionAsync(async services =>
        {
            await services.GetRequiredService<IUserRepository>().SetPhoneAsync(userId, TestPhones.Unique(), confirmed: true, Ct);
        });
        var pendingLink = await TestLoginLinks.AddPendingAsync(factory, userId);
        var previous = factory.EmailSender.CountFor(email);
        using var ask = await client.SendWithTokenAsync(HttpMethod.Post, UnlinkCodeUrl, tokens.AccessToken, new { change = "UnlinkPhone" });
        Assert.Equal(HttpStatusCode.Accepted, ask.StatusCode);
        var code = CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, LinkUrl, tokens.AccessToken, new { code });
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profile = await me.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phoneNumber").ValueKind);
        Assert.False(profile.GetProperty("phoneNumberConfirmed").GetBoolean());
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(Pkce.CreateVerifier()));
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith(ApiFactory.WebRedirectUri + "?code=", authorize.Headers.Location?.ToString(), StringComparison.Ordinal);
        Assert.False(await TestLoginLinks.IsActiveAsync(factory, pendingLink));
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
    public async Task With_martas_session_the_number_is_not_unlinked_without_the_code_sent_to_her_email()
    {
        // N11: quien usa la sesión abierta de Marta no le desvincula el WhatsApp sin el código que llega a su correo.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var marta = await CreateAccountAsync(email: TestEmails.Unique("marta"), phone: phone);

        using var withoutCode = await UnlinkAsync(client, marta, language: "es");
        var missing = await withoutCode.ReadJsonAsync();
        var code = await RequestUnlinkCodeAsync(client, marta);
        using var wrong = await client.SendAsync(HttpMethod.Delete, LinkUrl,
            JsonContent.Create(new { code = code == "000000" ? "111111" : "000000" }), userId: IdOf(marta));
        var wrongProblem = await wrong.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, withoutCode.StatusCode);
        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, missing.GetProperty("code").GetString());
        Assert.Equal(
            "Falta el código que te mandamos para confirmar que sos vos.",
            missing.GetProperty("errors").GetProperty("code")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrongProblem.GetProperty("code").GetString());
        Assert.Equal(4, wrongProblem.GetProperty("attemptsLeft").GetInt32());
        Assert.Equal(phone.Value, (await AccountAsync(marta.Id)).PhoneNumber);

        using var right = await client.SendAsync(HttpMethod.Delete, LinkUrl, JsonContent.Create(new { code }), userId: IdOf(marta));

        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        Assert.Null((await AccountAsync(marta.Id)).PhoneNumber);
    }

    [Fact]
    public async Task The_unlink_code_goes_to_the_verified_email_and_says_what_it_is_for()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("aviso");
        var user = await CreateAccountAsync(email: email, phone: TestPhones.Unique());

        using var response = await client.SendAsync(HttpMethod.Post, UnlinkCodeUrl, UnlinkChange(), userId: IdOf(user));
        var body = await response.ReadJsonAsync();
        var message = await factory.EmailSender.WaitForAsync(email);
        var code = CapturingEmailSender.CodeOf(message);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(body.GetProperty("resendAfterSeconds").GetInt32() >= 0);
        Assert.Equal($"{code} es tu código para desvincular tu WhatsApp de Acceso", message.Subject);
        Assert.Contains("alguien está usando tu sesión", message.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_number_asking_for_the_unlink_code_answers_409_and_sends_nothing()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("sin-numero");
        var user = await CreateAccountAsync(email: email);

        using var response = await client.SendAsync(HttpMethod.Post, UnlinkCodeUrl, UnlinkChange(), language: "es", userId: IdOf(user));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.NoPhoneCode, problem.GetProperty("code").GetString());
        Assert.Equal("Tu cuenta no tiene un número de WhatsApp para desvincular.", problem.GetProperty("detail").GetString());
        Assert.Equal(0, factory.EmailSender.CountFor(email));
    }

    [Fact]
    public async Task A_code_to_verify_an_email_does_not_confirm_unlinking_the_number()
    {
        // Cada código sirve para su propósito: el que llega con «confirmá tu correo» no desvincula el número.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var email = TestEmails.Unique("proposito");
        var user = await CreateAccountAsync(email: email, phone: phone);
        var code = await factory.InTransactionAsync(async services =>
        {
            var issued = await services.GetRequiredService<ArquitecturaBase.Application.Services.Auth.LoginCodeIssuer>()
                .IssueVerificationCodeAsync(LoginCodeDestination.ForEmail(Email.Create(email).Value), user.Id, Ct);
            return issued.Value.Code;
        });

        using var response = await client.SendAsync(HttpMethod.Delete, LinkUrl, JsonContent.Create(new { code }), userId: IdOf(user));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(phone.Value, (await AccountAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task Without_a_verified_email_asking_for_the_unlink_code_answers_409()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());

        using var response = await client.SendAsync(HttpMethod.Post, UnlinkCodeUrl, UnlinkChange(), language: "es", userId: IdOf(user));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.NoVerifiedEmailCode, problem.GetProperty("code").GetString());
        Assert.Equal("Tu cuenta no tiene un correo verificado al que mandarte el código.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public void Unlink_requests_hide_the_code_in_action_logs()
    {
        Assert.Equal(nameof(UnlinkOwnPhoneHttpRequest), new UnlinkOwnPhoneHttpRequest("123456").ToString());
    }

    [Fact]
    public void Profile_unlink_requires_only_a_session_and_is_limited_like_a_code_check()
    {
        var endpoint = Assert.Single(factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText == "api/me/whatsapp"
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.Contains("DELETE") == true);

        var authorization = Assert.Single(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Null(authorization.Policy);
        Assert.Null(authorization.Roles);
        Assert.Null(endpoint.Metadata.GetMetadata<IAllowAnonymous>());
        Assert.Equal(
            RateLimitingExtensions.LoginVerifyPolicy,
            endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
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

    private static string IdOf(UserAccount user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    /// <summary>El cuerpo del pedido del código al correo que confirma desvincular el número.</summary>
    private static JsonContent UnlinkChange() => JsonContent.Create(new { change = "UnlinkPhone" });

    /// <summary>Pide el código al correo de la cuenta para desvincular el número y devuelve el que llegó.</summary>
    private async Task<string> RequestUnlinkCodeAsync(HttpClient client, UserAccount user)
    {
        var email = (await AccountAsync(user.Id)).Email!;
        var previous = factory.EmailSender.CountFor(email);

        using var response = await client.SendAsync(HttpMethod.Post, UnlinkCodeUrl, UnlinkChange(), userId: IdOf(user));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
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
