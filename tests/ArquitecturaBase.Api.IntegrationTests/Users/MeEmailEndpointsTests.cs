using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Agregar un correo desde el perfil (sección 12 del spec del ingreso con WhatsApp): las mismas reglas que vincular el
/// número, con el código por correo. "Ya existe una cuenta con ese correo" se dice recién después de un código
/// correcto. Los correos quedan en <see cref="ApiFactory.EmailSender"/>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MeEmailEndpointsTests(ApiFactory factory)
{
    private const string CodeUrl = "/api/me/email/code";
    private const string EmailUrl = "/api/me/email";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Adding_an_email_sends_it_a_code_and_the_right_code_adds_it_verified()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(phone: phone);
        var email = TestEmails.Unique("agrega");

        using var request = await AskCodeAsync(client, user, email);
        var body = await request.ReadJsonAsync();
        var message = await factory.EmailSender.WaitForAsync(email);
        var code = CapturingEmailSender.CodeOf(message);

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(["resendAfterSeconds"], PropertyNames(body));
        Assert.False(request.Headers.Contains("Set-Cookie"));

        // El texto dice para qué es: agregar el correo, no entrar.
        Assert.Equal($"{code} es tu código para agregar este correo a Arquitectura Base", message.Subject);
        Assert.Contains("agregar este correo a tu cuenta", message.TextBody, StringComparison.Ordinal);

        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.AsNoTracking().SingleAsync(
            stored => stored.Destination == email && stored.Purpose == LoginCodePurpose.VerifyDestination, Ct));
        Assert.Equal(LoginCodeChannel.Email, stored.Channel);
        Assert.Equal(user.Id, stored.RequestedByUserId);
        Assert.NotNull(stored.SentAtUtc);

        using var confirm = await ConfirmAsync(client, user, email, code);
        using var me = await client.SendAsync(HttpMethod.Get, "/api/me", userId: IdOf(user));
        var profile = await me.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.False(confirm.Headers.Contains("Set-Cookie"));
        Assert.Equal(email, profile.GetProperty("email").GetString());
        Assert.True(profile.GetProperty("emailConfirmed").GetBoolean());
        Assert.Equal(phone.Value, profile.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task The_email_goes_in_the_language_of_the_account()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique(), culture: "en");
        var email = TestEmails.Unique("english");

        // La petición llega en español, pero el correo es para la persona de la cuenta.
        using var request = await AskCodeAsync(client, user, email, language: "es");
        var message = await factory.EmailSender.WaitForAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal($"{CapturingEmailSender.CodeOf(message)} is your code to add this email to Arquitectura Base", message.Subject);
    }

    [Fact]
    public async Task Asking_for_the_email_of_another_account_answers_the_same_202_and_sends_the_code_the_same()
    {
        using var client = factory.CreateClient();
        var taken = TestEmails.Unique("tomado");
        var free = TestEmails.Unique("libre");
        await CreateAccountAsync(email: taken);
        var requester = await CreateAccountAsync(phone: TestPhones.Unique());

        using var takenResponse = await AskCodeAsync(client, requester, taken);
        using var freeResponse = await AskCodeAsync(client, requester, free);
        var takenMessage = await factory.EmailSender.WaitForAsync(taken);
        var freeMessage = await factory.EmailSender.WaitForAsync(free);

        // Nada distingue al correo que ya tiene cuenta: la respuesta, el código y el envío son los mismos.
        Assert.Equal(HttpStatusCode.Accepted, takenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, freeResponse.StatusCode);
        Assert.Equal(
            (await freeResponse.ReadJsonAsync()).GetRawText(),
            (await takenResponse.ReadJsonAsync()).GetRawText());
        Assert.Equal(
            freeMessage.Subject.Replace(CapturingEmailSender.CodeOf(freeMessage), "{code}", StringComparison.Ordinal),
            takenMessage.Subject.Replace(CapturingEmailSender.CodeOf(takenMessage), "{code}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_email_of_another_account_answers_409_only_after_the_right_code_and_the_code_is_spent()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("ajeno");
        var owner = await CreateAccountAsync(email: email);
        var requester = await CreateAccountAsync(phone: TestPhones.Unique());
        var code = await RequestCodeAsync(client, requester, email);

        using var wrong = await ConfirmAsync(client, requester, email, WrongCodeFor(code), language: "es");
        using var right = await ConfirmAsync(client, requester, email, code, language: "es");
        using var again = await ConfirmAsync(client, requester, email, code, language: "es");
        var rightProblem = await right.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await wrong.ReadJsonAsync()).GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Conflict, right.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, rightProblem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese correo.", rightProblem.GetProperty("detail").GetString());

        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, (await again.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(requester.Id)).Email);
        Assert.Equal(email, (await AccountAsync(owner.Id)).Email);
    }

    [Fact]
    public async Task The_email_of_a_deleted_account_also_answers_409_after_the_right_code()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("eliminado");
        var deleted = await CreateAccountAsync(email: email);
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(deleted.Id, Ct);

            return 0;
        });
        var requester = await CreateAccountAsync(phone: TestPhones.Unique());
        var code = await RequestCodeAsync(client, requester, email);

        using var response = await ConfirmAsync(client, requester, email, code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(requester.Id)).Email);
    }

    [Fact]
    public async Task A_new_email_replaces_one_that_was_not_verified_and_stays_verified()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetEmailAsync(
                user.Id, Email.Create(TestEmails.Unique("viejo")).Value, confirmed: false, Ct);

            return 0;
        });
        var email = TestEmails.Unique("nuevo");

        using var response = await ConfirmAsync(client, user, email, await RequestCodeAsync(client, user, email));

        var account = await AccountAsync(user.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(email, account.Email);
        Assert.True(account.EmailConfirmed);
    }

    [Fact]
    public async Task A_code_requested_by_one_account_does_not_add_the_email_to_another()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("pedido");
        var owner = await CreateAccountAsync(phone: TestPhones.Unique());
        var other = await CreateAccountAsync(phone: TestPhones.Unique());
        var code = await RequestCodeAsync(client, owner, email);

        using var foreign = await ConfirmAsync(client, other, email, code);
        using var own = await ConfirmAsync(client, owner, email, code);

        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await foreign.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Null((await AccountAsync(other.Id)).Email);
    }

    [Fact]
    public async Task A_sign_in_code_does_not_add_an_email_and_a_code_to_add_it_does_not_sign_in()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("proposito");
        var user = await CreateAccountAsync(phone: TestPhones.Unique());

        var signInCode = await client.RequestCodeAsync(factory, email);
        using var addWithSignInCode = await ConfirmAsync(client, user, email, signInCode);

        var addCode = await RequestCodeAsync(client, user, email);
        using var signInWithAddCode = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code = addCode, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.BadRequest, addWithSignInCode.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await addWithSignInCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(user.Id)).Email);

        Assert.Equal(HttpStatusCode.BadRequest, signInWithAddCode.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await signInWithAddCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(signInWithAddCode.Headers.Contains("Set-Cookie"));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(account => account.Email == email, Ct)));
    }

    [Fact]
    public async Task A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent()
    {
        var email = TestEmails.Unique("carrera");
        var requester = await CreateAccountAsync(phone: TestPhones.Unique());
        using var client = factory.CreateClient();
        var code = await RequestCodeAsync(client, requester, email);
        var owner = await CreateAccountAsync(email: email);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleIdentityReads.Replace(services, Email.Create(email).Value)));
        using var staleClient = api.CreateClient();

        using var response = await ConfirmAsync(staleClient, requester, email, code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.AlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.NotNull(await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(stored => stored.Destination == email && stored.RequestedByUserId == requester.Id)
            .Select(stored => stored.ConsumedAtUtc)
            .SingleAsync(Ct)));
        Assert.Null((await AccountAsync(requester.Id)).Email);
        Assert.Equal(email, (await AccountAsync(owner.Id)).Email);
    }

    [Fact]
    public async Task An_email_with_the_wrong_shape_or_a_code_with_the_wrong_shape_is_rejected()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());

        using var badEmail = await client.SendAsync(
            HttpMethod.Post, CodeUrl, JsonContent.Create(new { email = "no-es-un-correo" }), "es", IdOf(user));
        using var badCode = await ConfirmAsync(client, user, TestEmails.Unique("forma"), "12ab", language: "es");
        var emailProblem = await badEmail.ReadJsonAsync();
        var codeProblem = await badCode.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, badEmail.StatusCode);
        Assert.Equal("Ingresá un correo válido.", emailProblem.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, badCode.StatusCode);
        Assert.Equal(
            "Ingresá el código que te enviamos por email.",
            codeProblem.GetProperty("errors").GetProperty("code")[0].GetString());
    }

    [Theory]
    [InlineData("POST", CodeUrl)]
    [InlineData("PUT", EmailUrl)]
    public async Task Email_routes_reject_missing_and_malformed_json_with_400(string method, string route)
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());

        using var missing = await client.SendAsync(new HttpMethod(method), route, userId: IdOf(user));
        using var malformed = await SendBodyAsync("{", "application/json");

        foreach (var response in new[] { missing, malformed })
        {
            var problem = await response.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }

        async Task<HttpResponseMessage> SendBodyAsync(string body, string mediaType) =>
            await client.SendAsync(new HttpMethod(method), route,
                new StringContent(body, Encoding.UTF8, mediaType), userId: IdOf(user));
    }

    [Theory]
    [InlineData("POST", CodeUrl)]
    [InlineData("PUT", EmailUrl)]
    public async Task Email_routes_reject_non_json_with_415(string method, string route)
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());

        using var response = await client.SendAsync(new HttpMethod(method), route,
            new StringContent("{}", Encoding.UTF8, "text/plain"), userId: IdOf(user));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Email_requests_hide_destination_and_code_in_action_logs()
    {
        const string email = "private@example.test";
        const string code = "123456";

        Assert.Equal(nameof(RequestEmailCodeRequest), new RequestEmailCodeRequest(email).ToString());
        Assert.Equal(nameof(ConfirmEmailRequest), new ConfirmEmailRequest(email, code).ToString());
    }

    private static string[] PropertyNames(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static string WrongCodeFor(string code) => code == "000000" ? "111111" : "000000";

    private static string IdOf(UserAccount user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> AskCodeAsync(HttpClient client, UserAccount user, string email, string? language = null) =>
        client.SendAsync(HttpMethod.Post, CodeUrl, JsonContent.Create(new { email }), language, IdOf(user));

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client, UserAccount user, string email, string code, string? language = null) =>
        client.SendAsync(HttpMethod.Put, EmailUrl, JsonContent.Create(new { email, code }), language, IdOf(user));

    /// <summary>Pide el código para agregar <paramref name="email"/> a la cuenta y devuelve el que llegó.</summary>
    private async Task<string> RequestCodeAsync(HttpClient client, UserAccount user, string email)
    {
        // El correo sale por una cola en segundo plano: se espera el siguiente a esa dirección.
        var previous = factory.EmailSender.CountFor(email);

        using var response = await AskCodeAsync(client, user, email);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
    }

    /// <summary>Una cuenta con un correo (verificado), un número (verificado) o los dos.</summary>
    private Task<UserAccount> CreateAccountAsync(string? email = null, PhoneNumber? phone = null, string culture = "es") =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName: null, culture, Ct));

    private Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IIdentityService>().FindByIdAsync(userId, Ct)));
}
