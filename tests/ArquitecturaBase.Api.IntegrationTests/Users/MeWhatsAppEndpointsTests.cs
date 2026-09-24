using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Domain.WhatsApp;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Users;

/// <summary>
/// Vincular y desvincular el propio WhatsApp desde el perfil (sección 12 del spec del ingreso con WhatsApp): el código
/// de <see cref="LoginCodePurpose.VerifyDestination"/> sale por la misma plantilla que el de ingreso, y "este número
/// ya es de otra cuenta" se dice recién después de un código correcto. Los mensajes quedan en
/// <see cref="ApiFactory.WhatsApp"/>: nada sale hacia Meta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class MeWhatsAppEndpointsTests(ApiFactory factory)
{
    private const string CodeUrl = "/api/me/whatsapp/code";
    private const string LinkUrl = "/api/me/whatsapp";
    private const string LinkPrefix = "https://localhost/ingresar#t=";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Linking_sends_the_code_by_the_template_and_the_right_code_links_the_number_verified_with_its_contact()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var bsuid = await WriteToTheBotAsync(client, phone);
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("vincular"));
        var userId = await UserIdOfAsync(client, tokens);
        var previous = factory.WhatsApp.CountFor(phone);

        using var request = await client.SendWithTokenAsync(
            HttpMethod.Post, CodeUrl, tokens.AccessToken, new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        var body = await request.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(["maskedPhone", "phone", "resendAfterSeconds"], PropertyNames(body));
        Assert.Equal(phone.Value, body.GetProperty("phone").GetString());
        Assert.Equal("+54 9 351 •••• " + phone.Value[^4..], body.GetProperty("maskedPhone").GetString());

        // La misma plantilla de autenticación que el ingreso, en el idioma de la cuenta.
        var messages = factory.WhatsApp.SentTo(phone);
        Assert.Equal(previous + 1, messages.Count);
        var message = Assert.IsType<WhatsAppLoginCodeMessage>(messages[^1]);
        Assert.Equal("es", message.LanguageCode);

        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.AsNoTracking().SingleAsync(
            code => code.Destination == phone.Value && code.Purpose == LoginCodePurpose.VerifyDestination, Ct));
        Assert.Equal(LoginCodeChannel.WhatsApp, stored.Channel);
        Assert.Equal(userId, stored.RequestedByUserId);
        Assert.NotNull(stored.SentAtUtc);

        using var confirm = await client.SendWithTokenAsync(
            HttpMethod.Put, LinkUrl, tokens.AccessToken, new { phone = phone.Value, code = message.Code });
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profile = await me.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(phone.Value, profile.GetProperty("phoneNumber").GetString());
        Assert.True(profile.GetProperty("phoneNumberConfirmed").GetBoolean());
        Assert.Equal(userId, (await ContactAsync(bsuid)).UserId);
    }

    [Fact]
    public async Task The_code_to_link_a_number_goes_in_the_language_of_the_account()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("english"), culture: "en");
        var phone = TestPhones.Unique();

        // La petición llega en español, pero el mensaje es para la persona de la cuenta.
        using var response = await AskCodeAsync(client, user, phone, language: "es");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("en", Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(factory.WhatsApp.SentTo(phone))).LanguageCode);
    }

    [Fact]
    public async Task Asking_for_the_number_of_another_account_answers_the_same_202_and_sends_the_code_the_same()
    {
        using var client = factory.CreateClient();
        var taken = TestPhones.Unique();
        var free = TestPhones.Unique();
        await CreateAccountAsync(phone: taken);
        var requester = await CreateAccountAsync(email: TestEmails.Unique("pide"));

        using var takenResponse = await AskCodeAsync(client, requester, taken);
        using var freeResponse = await AskCodeAsync(client, requester, free);
        var takenBody = await takenResponse.ReadJsonAsync();
        var freeBody = await freeResponse.ReadJsonAsync();

        // Nada distingue al número que ya tiene cuenta: la respuesta, el código y el envío son los mismos.
        Assert.Equal(HttpStatusCode.Accepted, takenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, freeResponse.StatusCode);
        Assert.Equal(PropertyNames(freeBody), PropertyNames(takenBody));
        Assert.Equal(
            freeBody.GetProperty("resendAfterSeconds").GetInt32(),
            takenBody.GetProperty("resendAfterSeconds").GetInt32());
        Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(factory.WhatsApp.SentTo(taken)));
        Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(factory.WhatsApp.SentTo(free)));
    }

    [Fact]
    public async Task The_number_of_another_account_answers_409_only_after_the_right_code_and_the_code_is_spent()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var owner = await CreateAccountAsync(phone: phone);
        var requester = await CreateAccountAsync(email: TestEmails.Unique("otra"));
        var code = await RequestCodeAsync(client, requester, phone);

        using var wrong = await ConfirmAsync(client, requester, phone, WrongCodeFor(code), language: "es");
        using var right = await ConfirmAsync(client, requester, phone, code, language: "es");
        using var again = await ConfirmAsync(client, requester, phone, code, language: "es");
        var wrongProblem = await wrong.ReadJsonAsync();
        var rightProblem = await right.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, wrongProblem.GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Conflict, right.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, rightProblem.GetProperty("code").GetString());
        Assert.Equal("Ya existe una cuenta con ese número.", rightProblem.GetProperty("detail").GetString());

        // El código quedó gastado aunque la respuesta sea un error.
        Assert.Equal(LoginCodeErrors.AlreadyUsedCode, (await again.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(requester.Id)).PhoneNumber);
        Assert.Equal(phone.Value, (await AccountAsync(owner.Id)).PhoneNumber);
    }

    [Fact]
    public async Task The_number_of_a_deleted_account_also_answers_409_after_the_right_code()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var deleted = await CreateAccountAsync(phone: phone);
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(deleted.Id, Ct);

            return 0;
        });
        var requester = await CreateAccountAsync(email: TestEmails.Unique("borrada"));
        var code = await RequestCodeAsync(client, requester, phone);

        using var response = await ConfirmAsync(client, requester, phone, code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(requester.Id)).PhoneNumber);
    }

    [Fact]
    public async Task Confirming_the_number_the_account_already_had_without_verifying_verifies_it()
    {
        // Lo cargó un administrador: el número ya es de la cuenta, así que no es "de otra cuenta".
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            Email.Create(TestEmails.Unique("cargado")).Value, phone, phoneConfirmed: false, displayName: null, "es", Ct));

        using var response = await ConfirmAsync(client, user, phone, await RequestCodeAsync(client, user, phone));

        var account = await AccountAsync(user.Id);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task A_code_requested_by_one_account_does_not_work_for_another_and_is_not_spent()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var owner = await CreateAccountAsync(email: TestEmails.Unique("duena"));
        var other = await CreateAccountAsync(email: TestEmails.Unique("ajena"));
        var code = await RequestCodeAsync(client, owner, phone);

        using var foreign = await ConfirmAsync(client, other, phone, code);
        var failedAttempts = await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(stored => stored.Destination == phone.Value)
            .Select(stored => stored.FailedAttempts)
            .SingleAsync(Ct));
        using var own = await ConfirmAsync(client, owner, phone, code);

        // La misma respuesta que sin código: no dice que otra cuenta está vinculando el número, ni gasta sus intentos.
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await foreign.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(0, failedAttempts);
        Assert.Equal(HttpStatusCode.NoContent, own.StatusCode);
        Assert.Null((await AccountAsync(other.Id)).PhoneNumber);
        Assert.Equal(phone.Value, (await AccountAsync(owner.Id)).PhoneNumber);
    }

    [Fact]
    public async Task Another_account_asking_for_the_same_number_does_not_invalidate_the_code_of_the_first()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var first = await CreateAccountAsync(email: TestEmails.Unique("primera"));
        var second = await CreateAccountAsync(email: TestEmails.Unique("segunda"));
        var firstCode = await RequestCodeAsync(client, first, phone);
        var secondCode = await RequestCodeAsync(client, second, phone);

        using var firstConfirm = await ConfirmAsync(client, first, phone, firstCode);
        using var secondConfirm = await ConfirmAsync(client, second, phone, secondCode);

        Assert.Equal(HttpStatusCode.NoContent, firstConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondConfirm.StatusCode);
        Assert.Equal(phone.Value, (await AccountAsync(first.Id)).PhoneNumber);
    }

    [Fact]
    public async Task A_new_code_of_the_same_account_invalidates_its_previous_one()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("otrocodigo"));
        var previousCode = await RequestCodeAsync(client, user, phone);
        var latestCode = await RequestCodeAsync(client, user, phone);

        using var previous = await ConfirmAsync(client, user, phone, previousCode);
        using var latest = await ConfirmAsync(client, user, phone, latestCode);

        // Si los dos códigos coinciden por azar, el anterior es el mismo que el último y vale.
        Assert.Equal(previousCode == latestCode ? HttpStatusCode.NoContent : HttpStatusCode.BadRequest, previous.StatusCode);
        Assert.Equal(previousCode == latestCode ? HttpStatusCode.BadRequest : HttpStatusCode.NoContent, latest.StatusCode);
    }

    [Fact]
    public async Task A_sign_in_code_does_not_link_a_number_and_a_code_to_link_it_does_not_sign_in()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("proposito"));

        using var signInRequest = await client.PostJsonAsync("/account/login-code/whatsapp", new { country = "AR", number = phone.Value });
        var signInCode = CapturingWhatsAppOutbox.CodeOf(factory.WhatsApp.SentTo(phone)[^1]);
        using var linkWithSignInCode = await ConfirmAsync(client, user, phone, signInCode);

        var linkCode = await RequestCodeAsync(client, user, phone);
        using var signInWithLinkCode = await client.PostJsonAsync(
            "/account/login-code/verify", new { phone = phone.Value, code = linkCode, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.Accepted, signInRequest.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, linkWithSignInCode.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await linkWithSignInCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);

        Assert.Equal(HttpStatusCode.BadRequest, signInWithLinkCode.StatusCode);
        Assert.Equal(LoginCodeErrors.InvalidCode, (await signInWithLinkCode.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(signInWithLinkCode.Headers.Contains("Set-Cookie"));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(account => account.PhoneNumber == phone.Value, Ct)));
    }

    [Fact]
    public async Task Failed_confirmations_count_against_the_code_but_neither_lock_the_account_nor_leave_a_login_audit()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("intentos"));
        var code = await RequestCodeAsync(client, user, phone);
        var statuses = new List<string?>();

        // Cinco intentos por código, como el ingreso: el quinto lo agota.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await ConfirmAsync(client, user, phone, WrongCodeFor(code));
            statuses.Add((await wrong.ReadJsonAsync()).GetProperty("code").GetString());
        }

        using var right = await ConfirmAsync(client, user, phone, code);

        Assert.Equal([.. Enumerable.Repeat(LoginCodeErrors.InvalidCode, 4), LoginCodeErrors.TooManyAttemptsCode], statuses);
        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, (await right.ReadJsonAsync()).GetProperty("code").GetString());

        // La persona ya está adentro: no es un ingreso, así que ni suma a los fallos de la cuenta ni se audita.
        var account = await factory.ExecuteDbContextAsync(db => db.Users.AsNoTracking().SingleAsync(stored => stored.Id == user.Id, Ct));
        Assert.Equal(0, account.AccessFailedCount);
        Assert.Null(account.LockoutEnd);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginAudits.AnyAsync(
            audit => audit.Identifier == phone.Value || audit.UserId == user.Id, Ct)));
    }

    [Fact]
    public async Task Replacing_the_number_releases_the_contact_of_the_previous_one()
    {
        using var client = factory.CreateClient();
        var (previousPhone, newPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var previousContact = await WriteToTheBotAsync(client, previousPhone);
        var newContact = await WriteToTheBotAsync(client, newPhone);
        var user = await CreateAccountAsync(email: TestEmails.Unique("reemplaza"));

        using var linkPrevious = await ConfirmAsync(client, user, previousPhone, await RequestCodeAsync(client, user, previousPhone));
        using var linkNew = await ConfirmAsync(client, user, newPhone, await RequestCodeAsync(client, user, newPhone));

        Assert.Equal(HttpStatusCode.NoContent, linkPrevious.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, linkNew.StatusCode);

        var account = await AccountAsync(user.Id);
        Assert.Equal(newPhone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
        Assert.Null((await ContactAsync(previousContact)).UserId);
        Assert.Equal(user.Id, (await ContactAsync(newContact)).UserId);
    }

    [Fact]
    public async Task Replacing_the_number_with_one_that_never_wrote_to_the_bot_also_releases_the_previous_contact()
    {
        using var client = factory.CreateClient();
        var previousPhone = TestPhones.Unique();
        var newPhone = TestPhones.Unique();
        var previousContact = await WriteToTheBotAsync(client, previousPhone);
        var user = await CreateAccountAsync(email: TestEmails.Unique("sinchat"));
        using var linkPrevious = await ConfirmAsync(client, user, previousPhone, await RequestCodeAsync(client, user, previousPhone));
        Assert.Equal(user.Id, (await ContactAsync(previousContact)).UserId);

        using var linkNew = await ConfirmAsync(client, user, newPhone, await RequestCodeAsync(client, user, newPhone));

        // Nadie le escribió al bot desde el número nuevo: no hay contacto que vincular, pero el del anterior se suelta.
        Assert.Equal(HttpStatusCode.NoContent, linkNew.StatusCode);
        Assert.Equal(newPhone.Value, (await AccountAsync(user.Id)).PhoneNumber);
        Assert.Null((await ContactAsync(previousContact)).UserId);
    }

    [Fact]
    public async Task Replacing_the_number_voids_the_links_already_sent_to_the_chat_of_the_previous_one()
    {
        using var client = factory.CreateClient();
        var (previousPhone, newPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var user = await CreateAccountAsync(email: TestEmails.Unique("cambia"), phone: previousPhone);

        // El bot le manda un enlace al chat del número que la cuenta tiene hoy.
        await WriteToTheBotAsync(client, previousPhone);
        var token = TokenOf(factory.WhatsApp.SentTo(previousPhone)[^1]);

        using var replace = await ConfirmAsync(client, user, newPhone, await RequestCodeAsync(client, user, newPhone));
        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, replace.StatusCode);
        Assert.Equal(newPhone.Value, (await AccountAsync(user.Id)).PhoneNumber);

        // Para el número anterior es lo mismo que desvincularlo: su chat puede no ser más de esta persona.
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Confirming_the_number_the_account_already_has_keeps_the_links_sent_to_its_chat()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("mismo"), phone: phone);
        await WriteToTheBotAsync(client, phone);
        var token = TokenOf(factory.WhatsApp.SentTo(phone)[^1]);

        using var confirm = await ConfirmAsync(client, user, phone, await RequestCodeAsync(client, user, phone));
        using var redeem = await RedeemAsync(client, token);

        // El chat es el mismo: no hay número que soltar.
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
    }

    [Fact]
    public async Task Replacing_the_number_while_the_bot_answers_the_previous_chat_waits_for_it_and_voids_the_link_it_sent()
    {
        using var client = factory.CreateClient();
        var (previousPhone, newPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var user = await CreateAccountAsync(email: TestEmails.Unique("cruce"), phone: previousPhone);
        var bsuid = await WriteToTheBotAsync(client, previousPhone);
        var code = await RequestCodeAsync(client, user, newPhone);
        var sentBefore = factory.WhatsApp.CountFor(previousPhone);

        // El bot tiene la fila del contacto del número anterior, y todavía no emitió el enlace.
        await SayHelloAsync(client, previousPhone, bsuid);
        await using var bot = await StartHeldBotAsync(bsuid);
        var replace = Task.Run(() => ConfirmAsync(client, user, newPhone, code), Ct);
        await WaitUntilSomeoneWaitsForALockAsync();
        await bot.ReleaseAsync();
        using var response = await replace.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // El perfil espera al bot, no al revés: si cada uno esperara al otro, Postgres cortaría uno (40P01) y sería un
        // 500. El enlace que el bot mandó mientras tanto, al chat del número que se va, tampoco sirve.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(previousPhone));
        using var redeem = await RedeemAsync(client, TokenOf(factory.WhatsApp.SentTo(previousPhone)[^1]));
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(newPhone.Value, (await AccountAsync(user.Id)).PhoneNumber);
        Assert.Null((await ContactAsync(bsuid)).UserId);
    }

    [Fact]
    public async Task Replacing_the_number_while_the_bot_answers_a_first_chat_from_the_previous_one_leaves_that_chat_without_the_account()
    {
        // La cuenta tiene el número anterior por la web, y desde ahí nunca se le escribió al bot: no tiene contacto.
        using var client = factory.CreateClient();
        var (previousPhone, newPhone) = (TestPhones.Unique(), TestPhones.Unique());
        var newContact = await WriteToTheBotAsync(client, newPhone);
        var user = await CreateAccountAsync(email: TestEmails.Unique("primerchat"), phone: previousPhone);
        var code = await RequestCodeAsync(client, user, newPhone);
        var bsuid = MetaWebhook.UniqueBsuid();
        var sentBefore = factory.WhatsApp.CountFor(previousPhone);

        // El primer mensaje desde el número anterior: el bot ya encontró la cuenta por el número y todavía no tiene su
        // lock. El perfil no espera a nadie (ese contacto no es de la cuenta) y termina antes.
        await SayHelloAsync(client, previousPhone, bsuid);
        await using var bot = await StartBotHeldAtTheAccountAsync(user.Id, AccountLockHoldPoint.BeforeTakingIt);
        using var response = await ConfirmAsync(client, user, newPhone, code);
        await bot.ReleaseAsync();

        // Con el lock, el bot vuelve a mirar la cuenta: ya no tiene ese número, así que ese chat ni recibe un enlace de ella
        // ni queda vinculado a ella (cada mensaje nuevo desde ahí traería otro enlace), y el contacto del número nuevo no
        // se suelta.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(newPhone.Value, (await AccountAsync(user.Id)).PhoneNumber);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(previousPhone));
        Assert.IsNotType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(previousPhone)[^1]);
        Assert.Null((await ContactAsync(bsuid)).UserId);
        Assert.Equal(user.Id, (await ContactAsync(newContact)).UserId);
        Assert.False(await HasActiveLinkAsync(user.Id));
    }

    [Fact]
    public async Task Confirming_the_unverified_number_while_the_bot_verifies_it_waits_for_the_bot_and_links_it()
    {
        // Lo cargó un administrador, y la persona le escribe al bot por primera vez mientras lo confirma desde el perfil.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            Email.Create(TestEmails.Unique("verifican")).Value, phone, phoneConfirmed: false, displayName: null, "es", Ct));
        var code = await RequestCodeAsync(client, user, phone);
        var bsuid = MetaWebhook.UniqueBsuid();
        var sentBefore = factory.WhatsApp.CountFor(phone);

        // El bot tiene la fila del contacto nuevo y, cuando conteste, va a verificar el número de la cuenta.
        await SayHelloAsync(client, phone, bsuid);
        await using var bot = await StartHeldBotAsync(bsuid);
        var confirm = Task.Run(() => ConfirmAsync(client, user, phone, code), Ct);
        await WaitUntilSomeoneWaitsForALockAsync();
        await bot.ReleaseAsync();
        using var response = await confirm.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // El perfil lee la cuenta después de esperar al bot: con lo que leyó antes, su guardado chocaría con el que hizo el
        // bot mientras tanto (el ConcurrencyStamp de Identity) y sería un 500.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(phone));
        var account = await AccountAsync(user.Id);
        Assert.Equal(phone.Value, account.PhoneNumber);
        Assert.True(account.PhoneNumberConfirmed);
        Assert.Equal(user.Id, (await ContactAsync(bsuid)).UserId);

        // Es el mismo número: el enlace que el bot mandó a su chat sigue sirviendo.
        using var redeem = await RedeemAsync(client, TokenOf(factory.WhatsApp.SentTo(phone)[^1]));
        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
    }

    [Fact]
    public async Task Linking_takes_the_contact_of_the_number_from_an_account_it_was_left_linked_to()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var previousOwner = await CreateAccountAsync(phone: phone);

        // El bot vincula el contacto a la cuenta del número. Después la cuenta pierde el número y el contacto queda
        // apuntándole: un vínculo viejo.
        var bsuid = await WriteToTheBotAsync(client, phone);
        Assert.Equal(previousOwner.Id, (await ContactAsync(bsuid)).UserId);
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().RemovePhoneAsync(previousOwner.Id, Ct);

            return 0;
        });
        var user = await CreateAccountAsync(email: TestEmails.Unique("hereda"));

        using var response = await ConfirmAsync(client, user, phone, await RequestCodeAsync(client, user, phone));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(user.Id, (await ContactAsync(bsuid)).UserId);
    }

    [Fact]
    public async Task Two_accounts_confirming_the_same_number_at_once_end_in_one_link_and_one_409()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var first = await CreateAccountAsync(email: TestEmails.Unique("carrera1"));
        var second = await CreateAccountAsync(email: TestEmails.Unique("carrera2"));
        var firstCode = await RequestCodeAsync(client, first, phone);
        var secondCode = await RequestCodeAsync(client, second, phone);

        var responses = await Task.WhenAll(
            ConfirmAsync(client, first, phone, firstCode),
            ConfirmAsync(client, second, phone, secondCode));

        try
        {
            Assert.Equal(
                [HttpStatusCode.NoContent, HttpStatusCode.Conflict],
                responses.Select(response => response.StatusCode).Order());
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.Users.CountAsync(account => account.PhoneNumber == phone.Value, Ct)));
    }

    [Fact]
    public async Task A_clash_with_the_unique_index_after_the_check_answers_409_and_keeps_the_code_spent()
    {
        // Otra cuenta se queda con el número entre la búsqueda y el guardado: la lectura vieja lo simula sin depender
        // de cómo se crucen dos pedidos, y el guardado choca de verdad con el índice único.
        var phone = TestPhones.Unique();
        var requester = await CreateAccountAsync(email: TestEmails.Unique("indice"));
        using var client = factory.CreateClient();
        var code = await RequestCodeAsync(client, requester, phone);
        var owner = await CreateAccountAsync(phone: phone);
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            StaleIdentityReads.Replace(services, phone)));
        using var staleClient = api.CreateClient();

        using var response = await ConfirmAsync(staleClient, requester, phone, code);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.PhoneAlreadyExistsCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.NotNull(await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(stored => stored.Destination == phone.Value && stored.RequestedByUserId == requester.Id)
            .Select(stored => stored.ConsumedAtUtc)
            .SingleAsync(Ct)));
        Assert.Null((await AccountAsync(requester.Id)).PhoneNumber);
        Assert.Equal(phone.Value, (await AccountAsync(owner.Id)).PhoneNumber);
    }

    [Fact]
    public async Task The_limits_per_number_are_shared_between_accounts()
    {
        // Protegen a quien recibe los mensajes: que los pida otra cuenta no reinicia la espera.
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();
        var first = await CreateAccountAsync(email: TestEmails.Unique("limite1"));
        var second = await CreateAccountAsync(email: TestEmails.Unique("limite2"));

        using var firstResponse = await AskCodeAsync(client, first, phone);
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var secondResponse = await AskCodeAsync(client, second, phone);
        var problem = await secondResponse.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(60, (await firstResponse.ReadJsonAsync()).GetProperty("resendAfterSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, problem.GetProperty("code").GetString());
        Assert.Equal(45, problem.GetProperty("retryAfter").GetInt32());
        Assert.Single(factory.WhatsApp.SentTo(phone));
    }

    [Fact]
    public async Task The_code_to_link_a_number_respects_the_daily_limit_of_whatsapp_codes()
    {
        // Un día después, ninguno de los códigos que mandaron los demás tests queda en la ventana de 24 horas.
        factory.Clock.Advance(TimeSpan.FromHours(25));
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:DailyAuthCodeLimit", "1"));
        using var client = api.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("tope"));
        var (first, second) = (TestPhones.Unique(), TestPhones.Unique());

        using var firstResponse = await AskCodeAsync(client, user, first);
        using var secondResponse = await AskCodeAsync(client, user, second);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, (await secondResponse.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Empty(factory.WhatsApp.SentTo(second));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Destination == second.Value, Ct)));
    }

    [Fact]
    public async Task Only_numbers_of_the_allowed_countries_and_valid_mobiles_can_be_linked()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("pais"));

        using var uruguay = await SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "UY", number = "099 123 456" }, "es");
        using var invalid = await SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "AR", number = "123" }, "es");
        var uruguayProblem = await uruguay.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, uruguay.StatusCode);
        Assert.Equal(WhatsAppErrors.CountryNotSupportedCode, uruguayProblem.GetProperty("code").GetString());
        Assert.Equal("Todavía no mandamos códigos a números de ese país.", uruguayProblem.GetProperty("detail").GetString());
        Assert.Empty(factory.WhatsApp.SentTo(PhoneNumber.Create("+59899123456").Value));

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(UserErrors.PhoneInvalidCode, (await invalid.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_country_a_number_a_phone_or_a_code_with_the_wrong_shape_is_rejected()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("forma"));
        var phone = TestPhones.Unique();
        var number = TestPhones.AsTypedLocally(phone);

        using var badCountry = await SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "ARG", number }, "es");
        using var noNumber = await SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "AR", number = "" }, "es");
        using var longNumber = await SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "AR", number = new string('1', 33) }, "es");
        using var noPhone = await SendAsync(client, HttpMethod.Put, LinkUrl, user, new { code = "123456" }, "es");
        using var badCode = await ConfirmAsync(client, user, phone, "12ab", language: "es");

        Assert.Equal("Elegí un país de la lista.", await ValidationMessageAsync(badCountry, "country"));
        Assert.Equal("Este campo es obligatorio.", await ValidationMessageAsync(noNumber, "number"));
        Assert.Equal("Ingresá como máximo 32 caracteres.", await ValidationMessageAsync(longNumber, "number"));
        Assert.Equal("Este campo es obligatorio.", await ValidationMessageAsync(noPhone, "phone"));

        // El diálogo es el de WhatsApp: el mensaje habla del código que llegó por WhatsApp, no por email.
        Assert.Equal("Ingresá el código que te enviamos por WhatsApp.", await ValidationMessageAsync(badCode, "code"));
        Assert.Empty(factory.WhatsApp.SentTo(phone));
    }

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
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetEmailAsync(
                user.Id, Email.Create(TestEmails.Unique("sinverificar")).Value, confirmed: false, Ct);

            return 0;
        });

        using var response = await UnlinkAsync(client, user);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(UserErrors.LastLoginMethodCode, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(phone.Value, (await AccountAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task With_a_verified_email_unlinking_removes_the_number_releases_the_contact_and_voids_pending_links_but_keeps_the_session()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("desvincula");
        var tokens = await client.LoginAsync(factory, email);
        var userId = await UserIdOfAsync(client, tokens);
        var phone = TestPhones.Unique();
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetPhoneAsync(userId, phone, confirmed: true, Ct);

            return 0;
        });

        // El bot le manda un enlace al chat y vincula el contacto a la cuenta.
        var bsuid = await WriteToTheBotAsync(client, phone);
        var link = Assert.IsType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        Assert.Equal(userId, (await ContactAsync(bsuid)).UserId);

        using var response = await client.SendWithTokenAsync(HttpMethod.Delete, LinkUrl, tokens.AccessToken);
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        var profile = await me.ReadJsonAsync();
        using var redeem = await client.PostJsonAsync("/account/login-link/redeem", new { token = link.Url[LinkPrefix.Length..] });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Las sesiones siguen: lo desvinculó la misma persona, que sigue adentro.
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(JsonValueKind.Null, profile.GetProperty("phoneNumber").ValueKind);
        Assert.False(profile.GetProperty("phoneNumberConfirmed").GetBoolean());
        Assert.Null((await ContactAsync(bsuid)).UserId);

        // "Ya no vas a poder entrar con ese número ni desde el chat": el enlace que ya estaba en el chat no sirve más.
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unlinking_while_the_bot_answers_the_same_chat_waits_for_it_and_voids_the_link_it_sent()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("cruza"), phone: phone);
        var bsuid = await WriteToTheBotAsync(client, phone);
        var sentBefore = factory.WhatsApp.CountFor(phone);

        // La persona le escribe al bot y, antes de que le conteste, toca Desvincular en la web.
        await SayHelloAsync(client, phone, bsuid);
        await using var bot = await StartHeldBotAsync(bsuid);
        var unlink = Task.Run(() => UnlinkAsync(client, user), Ct);
        await WaitUntilSomeoneWaitsForALockAsync();
        await bot.ReleaseAsync();
        using var response = await unlink.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // El perfil espera al bot, no al revés: si cada uno esperara al otro, Postgres cortaría uno (40P01) y sería un
        // 500. El bot contesta, y el enlace que mandó queda invalidado por el desvincular que vino después.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(phone));
        using var redeem = await RedeemAsync(client, TokenOf(factory.WhatsApp.SentTo(phone)[^1]));
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);
        Assert.Null((await ContactAsync(bsuid)).UserId);
    }

    [Fact]
    public async Task Unlinking_while_the_bot_verifies_the_number_waits_for_the_bot_and_unlinks_it()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("reverifica"), phone: phone);
        var bsuid = await WriteToTheBotAsync(client, phone);

        // Un administrador vuelve a cargar el número sin verificar: el bot lo verifica la próxima vez que le conteste.
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetPhoneAsync(user.Id, phone, confirmed: false, Ct);

            return 0;
        });
        var sentBefore = factory.WhatsApp.CountFor(phone);

        await SayHelloAsync(client, phone, bsuid);
        await using var bot = await StartHeldBotAsync(bsuid);
        var unlink = Task.Run(() => UnlinkAsync(client, user), Ct);
        await WaitUntilSomeoneWaitsForALockAsync();
        await bot.ReleaseAsync();
        using var response = await unlink.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // El perfil lee la cuenta después de esperar al bot: con lo que leyó antes, su guardado chocaría con el que hizo el
        // bot mientras tanto (el ConcurrencyStamp de Identity) y sería un 500.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(phone));
        using var redeem = await RedeemAsync(client, TokenOf(factory.WhatsApp.SentTo(phone)[^1]));
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);
        Assert.Null((await ContactAsync(bsuid)).UserId);
    }

    [Fact]
    public async Task Unlinking_while_the_bot_answers_a_first_chat_from_the_number_leaves_that_chat_without_the_account()
    {
        // La cuenta tiene el número por la web, y desde ahí nunca se le escribió al bot: no tiene contacto.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("primerchat"), phone: phone);
        var bsuid = MetaWebhook.UniqueBsuid();
        var sentBefore = factory.WhatsApp.CountFor(phone);

        // El primer mensaje desde el número: el bot ya encontró la cuenta por el número y todavía no tiene su lock. El
        // perfil no espera a nadie (ese contacto no es de la cuenta) y termina antes.
        await SayHelloAsync(client, phone, bsuid);
        await using var bot = await StartBotHeldAtTheAccountAsync(user.Id, AccountLockHoldPoint.BeforeTakingIt);
        using var response = await UnlinkAsync(client, user);
        await bot.ReleaseAsync();

        // Con el lock, el bot vuelve a mirar la cuenta: ya no tiene el número, así que el chat ni recibe un enlace de ella
        // ni queda vinculado a ella. Si no, cada mensaje nuevo desde ese número (el teléfono robado) traería otro enlace.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(phone));
        Assert.IsNotType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        Assert.Null((await ContactAsync(bsuid)).UserId);
        Assert.False(await HasActiveLinkAsync(user.Id));
    }

    [Fact]
    public async Task Unlinking_while_the_bot_moves_the_account_to_another_chat_of_the_number_does_not_end_in_a_deadlock()
    {
        // El mismo número con otro BSUID: la cuenta tiene vinculado el contacto de antes, y el nuevo le escribe al bot.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("dosbsuid"), phone: phone);
        var linked = await WriteToTheBotAsync(client, phone);
        var newcomer = MetaWebhook.UniqueBsuid();
        var sentBefore = factory.WhatsApp.CountFor(phone);

        // El bot tiene la fila del contacto nuevo y el lock de la cuenta, y todavía no soltó el de antes. El perfil toma
        // la fila del de antes y espera el lock de la cuenta.
        await SayHelloAsync(client, phone, newcomer);
        await using var bot = await StartBotHeldAtTheAccountAsync(user.Id, AccountLockHoldPoint.AfterTakingIt);
        var unlink = Task.Run(() => UnlinkAsync(client, user), Ct);
        await WaitUntilSomeoneWaitsForALockAsync();
        await bot.ReleaseAsync();
        using var response = await unlink.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // El bot no espera la fila que tiene el perfil: si cada uno esperara al otro, Postgres cortaría uno (40P01) y, si
        // le tocara al perfil, sería un 500. El bot deja la vuelta sin guardar ni mandar nada, con el mensaje pendiente.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(sentBefore, factory.WhatsApp.CountFor(phone));
        Assert.Null((await AccountAsync(user.Id)).PhoneNumber);
        Assert.Null((await ContactAsync(linked)).UserId);

        // En la vuelta siguiente el número ya no tiene cuenta: el chat nuevo no recibe un enlace.
        await factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
        Assert.Equal(sentBefore + 1, factory.WhatsApp.CountFor(phone));
        Assert.IsNotType<WhatsAppLinkButtonMessage>(factory.WhatsApp.SentTo(phone)[^1]);
        Assert.Null((await ContactAsync(newcomer)).UserId);
        Assert.False(await HasActiveLinkAsync(user.Id));
    }

    [Fact]
    public async Task With_google_linked_the_number_can_be_unlinked()
    {
        using var client = factory.CreateClient();
        var user = await CreateAccountAsync(phone: TestPhones.Unique());
        await factory.ExecuteScopeAsync(async services =>
        {
            var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
            await services.GetRequiredService<IIdentityService>().AddExternalLoginAsync(
                user.Id, new ExternalLogin(ExternalLoginProviders.Google, providerKey, Email: null, EmailVerified: false, DisplayName: null), Ct);

            return 0;
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
    public async Task Unlinking_without_a_number_still_releases_a_chat_left_linked_and_voids_its_links()
    {
        // Un vínculo viejo: la cuenta perdió el número por otro camino y su chat le quedó vinculado, con un enlace que
        // todavía sirve.
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var user = await CreateAccountAsync(email: TestEmails.Unique("residuo"), phone: phone);
        var bsuid = await WriteToTheBotAsync(client, phone);
        var token = TokenOf(factory.WhatsApp.SentTo(phone)[^1]);
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().RemovePhoneAsync(user.Id, Ct);

            return 0;
        });

        using var response = await UnlinkAsync(client, user);
        using var redeem = await RedeemAsync(client, token);

        // Desvincular también lo limpia: si no, la persona no tendría cómo cortar ese chat.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await ContactAsync(bsuid)).UserId);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task With_whatsapp_off_asking_for_a_code_to_link_answers_404()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var user = await CreateAccountAsync(email: TestEmails.Unique("apagado"));
        var phone = TestPhones.Unique();

        using var response = await AskCodeAsync(client, user, phone);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Http.NotFound", (await response.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Empty(factory.WhatsApp.SentTo(phone));
    }

    [Fact]
    public async Task No_log_carries_a_code_or_the_full_number()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging
                .AddFakeLogging()
                .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace))));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();
        var email = TestEmails.Unique("logs");
        var user = await CreateAccountAsync(email: TestEmails.Unique("registro"));

        var code = await RequestCodeAsync(client, user, phone);
        using var wrong = await ConfirmAsync(client, user, phone, WrongCodeFor(code));
        using var right = await ConfirmAsync(client, user, phone, code);
        using var emailRequest = await SendAsync(client, HttpMethod.Post, "/api/me/email/code", user, new { email });
        var emailCode = CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email));
        using var emailConfirm = await SendAsync(client, HttpMethod.Put, "/api/me/email", user, new { email, code = emailCode });
        using var unlink = await UnlinkAsync(client, user);

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, right.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, emailConfirm.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unlink.StatusCode);

        var logged = api.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => string.Join(
                "\n",
                [
                    record.Message,
                    record.Exception?.ToString() ?? string.Empty,
                    .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                    .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                ]))
            .ToList();

        Assert.NotEmpty(logged);

        // Como palabra suelta: un trace id en hexadecimal puede tener seis dígitos seguidos por casualidad.
        string[] secrets = [code, emailCode, phone.Value, phone.Value[1..], TestPhones.AsTypedLocally(phone)];
        var leaks = secrets
            .SelectMany(secret => logged
                .Where(text => Regex.IsMatch(text, $"(?<![0-9A-Za-z]){Regex.Escape(secret)}(?![0-9A-Za-z])"))
                .Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    [Theory]
    [InlineData("POST", "/api/me/whatsapp/code")]
    [InlineData("PUT", "/api/me/whatsapp")]
    [InlineData("DELETE", "/api/me/whatsapp")]
    [InlineData("POST", "/api/me/email/code")]
    [InlineData("PUT", "/api/me/email")]
    public async Task Without_a_bearer_every_route_answers_401(string method, string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            new HttpMethod(method), url, method == "DELETE" ? null : JsonContent.Create(new { }));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", (await response.ReadJsonAsync()).GetProperty("code").GetString());
    }

    private static string[] PropertyNames(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static string WrongCodeFor(string code) => code == "000000" ? "111111" : "000000";

    private static string IdOf(UserAccount user) => user.Id.ToString("D", CultureInfo.InvariantCulture);

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, UserAccount user, object? body = null, string? language = null) =>
        client.SendAsync(method, url, body is null ? null : JsonContent.Create(body), language, IdOf(user));

    private static Task<HttpResponseMessage> AskCodeAsync(
        HttpClient client, UserAccount user, PhoneNumber phone, string? language = null) =>
        SendAsync(client, HttpMethod.Post, CodeUrl, user, new { country = "AR", number = TestPhones.AsTypedLocally(phone) }, language);

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client, UserAccount user, PhoneNumber phone, string code, string? language = null) =>
        SendAsync(client, HttpMethod.Put, LinkUrl, user, new { phone = phone.Value, code }, language);

    private static Task<HttpResponseMessage> UnlinkAsync(HttpClient client, UserAccount user, string? language = null) =>
        SendAsync(client, HttpMethod.Delete, LinkUrl, user, body: null, language);

    /// <summary>Pide el código para vincular <paramref name="phone"/> a la cuenta y devuelve el que salió.</summary>
    private async Task<string> RequestCodeAsync(HttpClient client, UserAccount user, PhoneNumber phone)
    {
        var previous = factory.WhatsApp.CountFor(phone);

        using var response = await AskCodeAsync(client, user, phone);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var messages = factory.WhatsApp.SentTo(phone);
        Assert.Equal(previous + 1, messages.Count);

        return CapturingWhatsAppOutbox.CodeOf(messages[^1]);
    }

    /// <summary>Una cuenta con un correo (verificado), un número o los dos, en español si no se pide otro idioma.</summary>
    private Task<UserAccount> CreateAccountAsync(string? email = null, PhoneNumber? phone = null, string culture = "es") =>
        factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            email is null ? null : Email.Create(email).Value, phone, phoneConfirmed: true, displayName: null, culture, Ct));

    private Task<UserAccount> AccountAsync(Guid userId) =>
        factory.ExecuteScopeAsync(async services =>
            Assert.IsType<UserAccount>(await services.GetRequiredService<IIdentityService>().FindByIdAsync(userId, Ct)));

    private static async Task<Guid> UserIdOfAsync(HttpClient client, TokenResponse tokens)
    {
        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);

        return (await me.ReadJsonAsync()).GetProperty("id").GetGuid();
    }

    private Task<WhatsAppContact> ContactAsync(string bsuid) =>
        factory.ExecuteDbContextAsync(db => db.WhatsAppContacts.AsNoTracking().SingleAsync(contact => contact.UserIdentifier == bsuid, Ct));

    /// <summary>Una vuelta del bot frenada en un contacto o en el lock de una cuenta, en su propia Api.</summary>
    private sealed class HeldBot(BotHold hold, IAsyncDisposable host, Task round) : IAsyncDisposable
    {
        /// <summary>Lo suelta y espera a que termine la vuelta.</summary>
        public async Task ReleaseAsync()
        {
            hold.Release();

            await round.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        }

        public async ValueTask DisposeAsync()
        {
            // Si el test falló antes de soltarlo, la vuelta no queda colgada ni corre contra una Api ya descartada.
            hold.Release();
            await round.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            await host.DisposeAsync();
        }
    }

    /// <summary>
    /// La persona le escribe "Hola" al bot desde <paramref name="phone"/>, con un webhook firmado como los de Meta, y el
    /// bot le contesta: así queda su contacto, y su mensaje no queda pendiente para la vuelta de otro test. Devuelve el
    /// BSUID del contacto.
    /// </summary>
    private async Task<string> WriteToTheBotAsync(HttpClient client, PhoneNumber phone)
    {
        var bsuid = MetaWebhook.UniqueBsuid();

        await SayHelloAsync(client, phone, bsuid);
        await factory.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);

        return bsuid;
    }

    /// <summary>Llega un "Hola" del contacto <paramref name="bsuid"/>, que queda pendiente hasta que el bot lo procese.</summary>
    private async Task SayHelloAsync(HttpClient client, PhoneNumber phone, string bsuid)
    {
        var waId = phone.Value[1..];
        var body = MetaWebhook.Build(
            contacts: [MetaWebhook.Contact(waId, bsuid, "Ana Pérez")],
            messages: [MetaWebhook.Text(
                MetaWebhook.UniqueWaMessageId(), waId, bsuid, MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime), "Hola")]);

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/whatsapp", UriKind.Relative))
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));

        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>El token del enlace que el bot mandó al chat, como lo lee el SPA del fragmento.</summary>
    private static string TokenOf(WhatsAppOutboundMessage message) =>
        Assert.IsType<WhatsAppLinkButtonMessage>(message).Url[LinkPrefix.Length..];

    private static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string token) =>
        client.PostJsonAsync("/account/login-link/redeem", new { token });

    /// <summary>
    /// El bot empieza a contestar los pendientes y se queda frenado con la fila de <paramref name="bsuid"/> tomada
    /// (<see cref="HeldContactRepository"/>), hasta que el test lo suelta.
    /// </summary>
    private async Task<HeldBot> StartHeldBotAsync(string bsuid)
    {
        var hold = new BotHold((await ContactAsync(bsuid)).Id);

        return await StartHeldBotAsync(hold, services => HeldContactRepository.Replace(services, hold));
    }

    /// <summary>
    /// El bot empieza a contestar los pendientes y, ya con la cuenta del chat encontrada, se queda frenado en el lock de
    /// esa cuenta, antes o después de tomarlo (<see cref="HeldLoginLinkRepository"/>), hasta que el test lo suelta.
    /// </summary>
    private Task<HeldBot> StartBotHeldAtTheAccountAsync(Guid userId, AccountLockHoldPoint point)
    {
        var hold = new BotHold(userId);

        return StartHeldBotAsync(hold, services => HeldLoginLinkRepository.Replace(services, hold, point));
    }

    private async Task<HeldBot> StartHeldBotAsync(BotHold hold, Action<IServiceCollection> holdIt)
    {
        var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(holdIt));
        var processor = host.Services.GetRequiredService<WhatsAppInboundProcessor>();
        var bot = new HeldBot(hold, host, Task.Run(() => processor.ProcessPendingAsync(Ct), Ct));

        await hold.Taken.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        return bot;
    }

    /// <summary>Si la cuenta tiene un enlace de ingreso que todavía sirve.</summary>
    private Task<bool> HasActiveLinkAsync(Guid userId)
    {
        var nowUtc = factory.Clock.GetUtcNow().UtcDateTime;

        return factory.ExecuteDbContextAsync(db => db.LoginLinks.AnyAsync(
            link => link.UserId == userId
                && link.ConsumedAtUtc == null
                && link.InvalidatedAtUtc == null
                && link.ExpiresAtUtc > nowUtc,
            Ct));
    }

    /// <summary>El primer mensaje de validación de <paramref name="field"/> en un 400.</summary>
    private static async Task<string?> ValidationMessageAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.ReadJsonAsync();
        Assert.Equal(ValidationError.ErrorCode, problem.GetProperty("code").GetString());

        return problem.GetProperty("errors").GetProperty(field)[0].GetString();
    }

    /// <summary>
    /// Espera a que algún pedido quede esperando un lock de Postgres: así el test sabe que el pedido del perfil ya llegó
    /// a la fila que tiene el bot, y que va a esperar primero.
    /// </summary>
    private async Task WaitUntilSomeoneWaitsForALockAsync()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        while (await factory.ExecuteDbContextAsync(db => db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'""")
            .SingleAsync(timeout.Token)) == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
