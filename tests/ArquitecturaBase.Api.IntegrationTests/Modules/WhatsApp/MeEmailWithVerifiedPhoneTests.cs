using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// N11: alguien usa la sesión abierta de Marta, que entra con su WhatsApp, y pone su propio correo en «Mi perfil». Sin el
/// código que confirma el cambio, mandado al número de Marta (lo pide <c>POST /api/me/whatsapp/confirmation-code</c>), el
/// correo no se agrega; el de vincular un número no sirve para eso; y un correo verificado no se cambia desde el perfil
/// (cada medio confirma los cambios del otro, 2026-11-16). Desvincular el número sin el código del correo lo prueba
/// <c>MePhoneEndpointsTests</c>, y vincularlo sin él, <c>MeWhatsAppEndpointsTests</c>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MeEmailWithVerifiedPhoneTests(ApiFactory factory)
{
    private const string EmailCodeUrl = "/api/me/email/code";
    private const string EmailUrl = "/api/me/email";
    private const string PhoneCodeUrl = "/api/me/whatsapp/confirmation-code";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task With_martas_session_an_email_is_not_added_without_the_code_sent_to_her_number()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var marta = await CreateAccountAsync(phone: phone);
        var email = TestEmails.Unique("intruso");
        var emailCode = await RequestEmailCodeAsync(client, marta, email);

        using var withoutPhoneCode = await ConfirmAsync(client, marta, new { email, code = emailCode });
        var missing = await withoutPhoneCode.ReadJsonAsync();
        var phoneCode = await RequestPhoneCodeAsync(client, marta, phone);
        using var wrongPhoneCode = await ConfirmAsync(
            client, marta, new { email, code = emailCode, phoneCode = WrongCodeFor(phoneCode) });
        var wrong = await wrongPhoneCode.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, withoutPhoneCode.StatusCode);
        Assert.Equal(UserErrors.ConfirmationCodeRequiredCode, missing.GetProperty("code").GetString());
        Assert.Equal(
            "Falta el código que te mandamos para confirmar que sos vos.",
            missing.GetProperty("errors").GetProperty("phoneCode")[0].GetString());

        // El error es del código del número: va en su campo, con los intentos que le quedan.
        Assert.Equal(HttpStatusCode.BadRequest, wrongPhoneCode.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrong.GetProperty("code").GetString());
        Assert.True(wrong.GetProperty("errors").TryGetProperty("phoneCode", out _));
        Assert.Equal(4, wrong.GetProperty("attemptsLeft").GetInt32());

        var account = await AccountAsync(marta.Id);
        Assert.Null(account.Email);
        Assert.Equal(phone.Value, account.PhoneNumber);
    }

    [Fact]
    public async Task With_the_code_sent_to_her_number_the_email_is_added_verified_and_both_codes_are_spent()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var marta = await CreateAccountAsync(phone: phone);
        var email = TestEmails.Unique("marta");
        var phoneCode = await RequestPhoneCodeAsync(client, marta, phone);
        var emailCode = await RequestEmailCodeAsync(client, marta, email);

        // Un error de tipeo en el código del correo no le gasta el del número.
        using var wrongEmailCode = await ConfirmAsync(client, marta, new { email, code = WrongCodeFor(emailCode), phoneCode });
        using var right = await ConfirmAsync(client, marta, new { email, code = emailCode, phoneCode });
        using var again = await ConfirmAsync(client, marta, new { email, code = emailCode, phoneCode });

        Assert.Equal(HttpStatusCode.BadRequest, wrongEmailCode.StatusCode);
        Assert.False((await wrongEmailCode.ReadJsonAsync()).TryGetProperty("errors", out _));
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        var account = await AccountAsync(marta.Id);
        Assert.Equal(email, account.Email);
        Assert.True(account.EmailConfirmed);
        Assert.Equal(phone.Value, account.PhoneNumber);

        // Ya gastados: el del número se dice primero.
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, (await again.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_verified_email_is_not_replaced_from_the_profile_and_no_code_is_sent_for_it()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var current = TestEmails.Unique("actual");
        var marta = await CreateAccountAsync(email: current, phone: phone);
        var email = TestEmails.Unique("reemplazo");
        var sentToPhone = factory.WhatsApp.CountFor(phone);

        using var phoneCode = await client.SendAsync(HttpMethod.Post, PhoneCodeUrl, language: "es", userId: IdOf(marta));
        using var emailCode = await client.SendAsync(
            HttpMethod.Post, EmailCodeUrl, JsonContent.Create(new { email }), language: "es", userId: IdOf(marta));
        using var response = await ConfirmAsync(
            client, marta, new { email, code = "123456", phoneCode = "123456" }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, phoneCode.StatusCode);
        Assert.Equal(UserErrors.EmailAlreadySetCode, (await phoneCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, emailCode.StatusCode);
        Assert.Equal(UserErrors.EmailAlreadySetCode, (await emailCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(sentToPhone, factory.WhatsApp.CountFor(phone));
        Assert.Equal(0, factory.EmailSender.CountFor(email));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.EmailAlreadySetCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "Tu cuenta ya tiene un correo verificado, y desde tu perfil no se cambia. Si hay que cambiarlo, pedíselo a un administrador.",
            problem.GetProperty("detail").GetString());
        Assert.Equal(current, (await AccountAsync(marta.Id)).Email);
    }

    [Fact]
    public async Task The_code_to_link_the_number_does_not_confirm_adding_an_email()
    {
        // Cada código sirve para su propósito: el que pide POST /api/me/whatsapp/code no confirma agregar un correo.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var marta = await CreateAccountAsync(phone: phone);
        var email = TestEmails.Unique("otro-proposito");
        var previous = factory.WhatsApp.CountFor(phone);
        using var linkCodeResponse = await client.SendAsync(
            HttpMethod.Post, "/api/me/whatsapp/code", JsonContent.Create(new { number = phone.Value }), userId: IdOf(marta));
        Assert.Equal(HttpStatusCode.Accepted, linkCodeResponse.StatusCode);
        var linkCode = CapturingWhatsAppSendQueue.CodeOf(factory.WhatsApp.SentTo(phone)[previous]);
        var emailCode = await RequestEmailCodeAsync(client, marta, email);

        using var response = await ConfirmAsync(client, marta, new { email, code = emailCode, phoneCode = linkCode });
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("phoneCode", out _));
        Assert.Null((await AccountAsync(marta.Id)).Email);
    }

    [Fact]
    public async Task Without_a_verified_number_no_code_to_confirm_adding_an_email_is_sent()
    {
        using var client = factory.CreateClient();
        var marta = await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            email: null, TestPhones.Unique(), phoneConfirmed: false, displayName: null, "es", Ct));

        using var response = await client.SendAsync(HttpMethod.Post, PhoneCodeUrl, language: "es", userId: IdOf(marta));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.NoVerifiedPhoneCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "Tu cuenta no tiene un número de WhatsApp verificado al que mandarte el código.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task With_whatsapp_off_an_account_with_a_verified_number_cannot_add_an_email()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var marta = await CreateAccountAsync(phone: TestPhones.Unique());
        var email = TestEmails.Unique("apagado");
        var emailCode = await RequestEmailCodeAsync(client, marta, email);

        using var response = await ConfirmAsync(client, marta, new { email, code = emailCode, phoneCode = "123456" }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneCodeUnavailableCode, problem.GetProperty("code").GetString());
        Assert.Null((await AccountAsync(marta.Id)).Email);
    }

    private static string WrongCodeFor(string code) => code == "000000" ? "111111" : "000000";

    private static string IdOf(UserAccount user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, UserAccount user, object body, string? language = null) =>
        client.SendAsync(HttpMethod.Put, EmailUrl, JsonContent.Create(body), language, IdOf(user));

    private async Task<string> RequestEmailCodeAsync(HttpClient client, UserAccount user, string email)
    {
        var previous = factory.EmailSender.CountFor(email);

        using var response = await client.SendAsync(
            HttpMethod.Post, EmailCodeUrl, JsonContent.Create(new { email }), userId: IdOf(user));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
    }

    /// <summary>El código que confirma el cambio, al número verificado de la cuenta: la ruta no lleva cuerpo.</summary>
    private async Task<string> RequestPhoneCodeAsync(HttpClient client, UserAccount user, PhoneNumber phone)
    {
        var previous = factory.WhatsApp.CountFor(phone);

        using var response = await client.SendAsync(HttpMethod.Post, PhoneCodeUrl, userId: IdOf(user));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(string.IsNullOrEmpty((await response.ReadJsonAsync()).GetProperty("maskedPhone").GetString()));

        var messages = factory.WhatsApp.SentTo(phone);
        Assert.Equal(previous + 1, messages.Count);
        return CapturingWhatsAppSendQueue.CodeOf(messages[^1]);
    }

    /// <summary>Una cuenta con el número verificado, como la de quien entra con WhatsApp, y quizás un correo verificado.</summary>
    private Task<UserAccount> CreateAccountAsync(PhoneNumber phone, string? email = null) =>
        factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName: null, "es", Ct));

    private Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IUserReader>().FindByIdAsync(userId, Ct)));
}
