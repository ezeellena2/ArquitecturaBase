using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// El ingreso web con un código por WhatsApp (sección 10 del spec del ingreso con WhatsApp): las mismas reglas que el
/// correo, con el número como destino. Los mensajes quedan en <see cref="ApiFactory.WhatsApp"/>: nada sale hacia Meta.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppLoginCodeTests(ApiFactory factory)
{
    private const string RequestUrl = "/account/login-code/whatsapp";
    private const string VerifyUrl = "/account/login-code/verify";
    private const string Uruguay = "+59899123456";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Requesting_a_code_answers_202_with_the_number_and_queues_the_template()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();

        using var response = await client.PostJsonAsync(
            RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(phone) }, language: "en");
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(phone.Value, body.GetProperty("phone").GetString());
        Assert.Equal("+54 9 351 •••• " + phone.Value[^4..], body.GetProperty("maskedPhone").GetString());

        // Sin cuenta y con el registro abierto: el código sale, en el idioma de la petición.
        var message = Assert.IsType<WhatsAppLoginCodeMessage>(Assert.Single(factory.WhatsApp.SentTo(phone)));
        Assert.Equal("en", message.LanguageCode);
        Assert.Matches("^[0-9]{6}$", message.Code);

        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.SingleAsync(code => code.Destination == phone.Value, Ct));
        Assert.Equal(LoginCodeChannel.WhatsApp, stored.Channel);
        Assert.Equal(LoginCodePurpose.SignIn, stored.Purpose);
        Assert.Equal(stored.CreatedAtUtc, stored.SentAtUtc);
        Assert.DoesNotContain(message.Code, stored.CodeHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_answer_has_the_same_shape_for_a_number_with_an_account_and_one_without()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var known = await CreatePhoneAccountAsync(phoneConfirmed: true);
        var unknown = TestPhones.Unique();

        using var knownResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(known) });
        using var unknownResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(unknown) });
        var knownBody = await knownResponse.ReadJsonAsync();
        var unknownBody = await unknownResponse.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);

        // Solo cambia el número, que es el que cada uno escribió: nada del cuerpo dice si tiene cuenta.
        Assert.Equal(["maskedPhone", "phone", "resendAfterSeconds"], PropertyNames(knownBody));
        Assert.Equal(PropertyNames(knownBody), PropertyNames(unknownBody));
        Assert.Equal(
            knownBody.GetProperty("resendAfterSeconds").GetInt32(),
            unknownBody.GetProperty("resendAfterSeconds").GetInt32());
        Assert.Equal(known.Value, knownBody.GetProperty("phone").GetString());
        Assert.Equal(unknown.Value, unknownBody.GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Invite_only_queues_nothing_for_a_number_without_an_account_and_keeps_its_code_as_never_sent()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var known = await CreatePhoneAccountAsync(phoneConfirmed: true);
        var unknown = TestPhones.Unique();

        using var knownResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = known.Value });
        using var unknownResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = unknown.Value });

        Assert.Single(factory.WhatsApp.SentTo(known));
        Assert.Empty(factory.WhatsApp.SentTo(unknown));

        // El código del número sin cuenta se emitió igual: es lo que sostiene los límites por número.
        var sentAt = await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(code => code.Destination == known.Value || code.Destination == unknown.Value)
            .ToDictionaryAsync(code => code.Destination, code => code.SentAtUtc, Ct));
        Assert.NotNull(sentAt[known.Value]);
        Assert.Null(sentAt[unknown.Value]);
    }

    [Fact]
    public async Task Open_mode_creates_an_account_without_email_when_a_new_number_verifies_its_code()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();
        var code = await RequestCodeAsync(client, phone);

        using var response = await client.PostJsonAsync(
            VerifyUrl, new { phone = phone.Value, code, returnUrl = AuthFlow.AuthorizeReturnUrl });
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AuthFlow.AuthorizeReturnUrl, body.GetProperty("returnUrl").GetString());
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));

        var user = await factory.ExecuteDbContextAsync(db => db.Users.SingleAsync(user => user.PhoneNumber == phone.Value, Ct));
        Assert.Null(user.Email);
        Assert.True(user.PhoneNumberConfirmed);

        var audit = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(audit => audit.Identifier == phone.Value, Ct));
        Assert.True(audit.Succeeded);
        Assert.Equal(LoginMethod.WhatsAppCode, audit.Method);
        Assert.Equal(user.Id, audit.UserId);
    }

    [Fact]
    public async Task Verifying_the_code_of_a_number_loaded_by_an_administrator_verifies_the_number()
    {
        using var client = factory.CreateClient();
        var phone = await CreatePhoneAccountAsync(phoneConfirmed: false);
        var code = await RequestCodeAsync(client, phone);

        using var response = await client.PostJsonAsync(
            VerifyUrl, new { phone = phone.Value, code, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.Users.Where(user => user.PhoneNumber == phone.Value).Select(user => user.PhoneNumberConfirmed).SingleAsync(Ct)));
    }

    [Fact]
    public async Task Invite_only_rejects_a_valid_code_of_a_number_without_an_account_and_creates_nothing()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();

        // En InviteOnly el pedido no le manda el código a un número sin cuenta, pero uno válido puede existir igual
        // (pedido en Open justo antes de cerrar el registro, por ejemplo).
        var code = await IssueSignInCodeAsync(phone);

        using var response = await client.PostJsonAsync(
            VerifyUrl, new { phone = phone.Value, code, returnUrl = AuthFlow.AuthorizeReturnUrl }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.NotInvitedCode, problem.GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.PhoneNumber == phone.Value, Ct)));

        var audit = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(audit => audit.Identifier == phone.Value, Ct));
        Assert.False(audit.Succeeded);
        Assert.Equal(LoginMethod.WhatsAppCode, audit.Method);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
    }

    [Theory]
    [InlineData("UY", "099 123 456")]
    [InlineData("AR", "+598 99 123 456")]
    public async Task A_number_from_a_country_not_allowed_is_rejected_and_nothing_is_issued(string country, string number)
    {
        using var client = factory.CreateClient();

        using var response = await client.PostJsonAsync(RequestUrl, new { country, number }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Auth.WhatsApp.CountryNotSupported", problem.GetProperty("code").GetString());
        Assert.Equal("Todavía no mandamos códigos a números de ese país.", problem.GetProperty("detail").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Destination == Uruguay, Ct)));
        Assert.Empty(factory.WhatsApp.SentTo(PhoneNumber.Create(Uruguay).Value));
    }

    [Fact]
    public async Task A_number_that_is_not_a_mobile_is_rejected_when_asking_and_when_verifying()
    {
        using var client = factory.CreateClient();

        using var request = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = "123" }, language: "es");
        using var verify = await client.PostJsonAsync(
            VerifyUrl, new { phone = "351 555-1234", code = "123456", returnUrl = AuthFlow.AuthorizeReturnUrl }, language: "es");
        var requestProblem = await request.ReadJsonAsync();
        var verifyProblem = await verify.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
        Assert.Equal("Users.Phone.Invalid", requestProblem.GetProperty("code").GetString());
        Assert.Equal("Ingresá un número de celular válido.", requestProblem.GetProperty("detail").GetString());

        // El verify recibe el número que devolvió el pedido, en formato internacional: otro formato no se interpreta.
        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.Equal("Users.Phone.Invalid", verifyProblem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reaching_the_daily_limit_answers_429_and_neither_issues_nor_queues()
    {
        // Un día después, ninguno de los códigos que mandaron los demás tests queda en la ventana de 24 horas.
        factory.Clock.Advance(TimeSpan.FromHours(25));
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:DailyAuthCodeLimit", "2"));
        using var client = api.CreateClient();
        var (first, second, third) = (TestPhones.Unique(), TestPhones.Unique(), TestPhones.Unique());

        using var firstResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = first.Value });
        using var secondResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = second.Value });
        factory.Clock.Advance(TimeSpan.FromMinutes(10));
        using var thirdResponse = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = third.Value }, language: "es");
        var problem = await thirdResponse.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, thirdResponse.StatusCode);
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, problem.GetProperty("code").GetString());
        Assert.Equal("Pediste demasiados códigos. Probá de nuevo más tarde.", problem.GetProperty("detail").GetString());

        // El primero salió hace 10 minutos: deja la ventana dentro de 23 horas y 50 minutos.
        Assert.Equal((24 * 60 - 10) * 60, problem.GetProperty("retryAfter").GetInt32());
        Assert.Empty(factory.WhatsApp.SentTo(third));
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Destination == third.Value, Ct)));
    }

    [Fact]
    public async Task Asking_again_before_the_cooldown_returns_429_even_with_the_number_typed_another_way()
    {
        // El arnés no espera entre pedidos: para probar el límite hace falta el valor real.
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();

        using var first = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var second = await client.PostJsonAsync(
            RequestUrl, new { country = "AR", number = "+54 9 351 " + TestPhones.LocalPart(phone) }, language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(60, (await first.ReadJsonAsync()).GetProperty("resendAfterSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal(LoginCodeErrors.ResendTooSoonCode, problem.GetProperty("code").GetString());
        Assert.Equal(45, problem.GetProperty("retryAfter").GetInt32());
        Assert.Single(factory.WhatsApp.SentTo(phone));
    }

    [Fact]
    public async Task The_limit_per_number_counts_an_earlier_code_to_verify_the_same_number()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();

        // Un código que alguien pidió desde su perfil para vincular este número: los límites son por destino.
        await IssueCodeToVerifyTheNumberAsync(phone);

        for (var i = 0; i < 4; i++)
        {
            factory.Clock.Advance(TimeSpan.FromSeconds(61));
            using var accepted = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = phone.Value });
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(61));
        using var rejected = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = phone.Value });
        var problem = await rejected.ReadJsonAsync();

        // El de verificación se pidió hace 305 segundos: sale de la ventana de 15 minutos dentro de 595.
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal(LoginCodeErrors.TooManyRequestsCode, problem.GetProperty("code").GetString());
        Assert.Equal(595, problem.GetProperty("retryAfter").GetInt32());
        Assert.Equal(4, factory.WhatsApp.CountFor(phone));
    }

    [Fact]
    public async Task Verifying_needs_exactly_one_of_email_and_phone()
    {
        using var client = factory.CreateClient();
        var phone = TestPhones.Unique();

        using var both = await client.PostJsonAsync(
            VerifyUrl,
            new { email = TestEmails.Unique("both"), phone = phone.Value, code = "123456", returnUrl = AuthFlow.AuthorizeReturnUrl },
            language: "es");
        using var neither = await client.PostJsonAsync(
            VerifyUrl, new { code = "123456", returnUrl = AuthFlow.AuthorizeReturnUrl }, language: "es");
        var bothProblem = await both.ReadJsonAsync();
        var neitherProblem = await neither.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
        Assert.Equal("Validation.Failed", bothProblem.GetProperty("code").GetString());
        Assert.Equal(
            "Mandá el correo o el número, no los dos.",
            bothProblem.GetProperty("errors").GetProperty("phone")[0].GetString());

        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal("Validation.Failed", neitherProblem.GetProperty("code").GetString());
        Assert.True(neitherProblem.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task With_whatsapp_off_it_is_not_offered_and_asking_for_a_code_answers_404()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();

        using var methods = await client.SendAsync(HttpMethod.Get, "/account/login-methods");
        using var request = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = phone.Value }, language: "es");
        var body = await methods.ReadJsonAsync();
        var problem = await request.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, methods.StatusCode);
        Assert.False(body.GetProperty("whatsapp").GetBoolean());
        Assert.Empty(body.GetProperty("whatsappCountries").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsappNumber").ValueKind);

        // La ruta no existe: el mismo 404 que cualquier otra ruta desconocida.
        Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
        Assert.False(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Destination == phone.Value, Ct)));
    }

    [Fact]
    public async Task No_log_carries_the_code_or_the_full_number()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging
                .AddFakeLogging()
                .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace))));
        using var client = api.CreateClient();
        var phone = TestPhones.Unique();

        using var request = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        var code = CapturingWhatsAppOutbox.CodeOf(Assert.Single(factory.WhatsApp.SentTo(phone)));
        using var wrong = await client.PostJsonAsync(
            VerifyUrl, new { phone = phone.Value, code = code == "000000" ? "111111" : "000000", returnUrl = AuthFlow.AuthorizeReturnUrl });
        using var right = await client.PostJsonAsync(
            VerifyUrl, new { phone = phone.Value, code, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.Accepted, request.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);

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

        // Como palabra suelta: un trace id en hexadecimal puede tener seis dígitos seguidos por casualidad, y eso no es
        // el código. "code=482913" o "+5493515551234" en un texto sí cuentan.
        string[] secrets = [code, phone.Value, phone.Value[1..], TestPhones.AsTypedLocally(phone)];
        var leaks = secrets
            .SelectMany(secret => logged
                .Where(text => Regex.IsMatch(text, $"(?<![0-9A-Za-z]){Regex.Escape(secret)}(?![0-9A-Za-z])"))
                .Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    private static string[] PropertyNames(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    /// <summary>Pide un código para <paramref name="phone"/> como lo escribiría la persona y devuelve el que salió.</summary>
    private async Task<string> RequestCodeAsync(HttpClient client, PhoneNumber phone)
    {
        var previous = factory.WhatsApp.CountFor(phone);

        using var response = await client.PostJsonAsync(RequestUrl, new { country = "AR", number = TestPhones.AsTypedLocally(phone) });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var messages = factory.WhatsApp.SentTo(phone);
        Assert.Equal(previous + 1, messages.Count);

        return CapturingWhatsAppOutbox.CodeOf(messages[^1]);
    }

    /// <summary>Una cuenta con un número nuevo y un correo, como las que da de alta un administrador.</summary>
    private Task<PhoneNumber> CreatePhoneAccountAsync(bool phoneConfirmed)
    {
        var phone = TestPhones.Unique();
        var email = Email.Create(TestEmails.Unique("whatsapp")).Value;

        return factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>()
                .CreateAsync(email, phone, phoneConfirmed, displayName: "Laura", "es", Ct);

            return phone;
        });
    }

    /// <summary>
    /// Emite y guarda un código de ingreso válido para <paramref name="phone"/> sin pasar por el pedido, así existe
    /// aunque el pedido no lo hubiera mandado. Devuelve el código en claro.
    /// </summary>
    private Task<string> IssueSignInCodeAsync(PhoneNumber phone) =>
        IssueAsync(phone, LoginCodePurpose.SignIn, requestedByUserId: null);

    /// <summary>Un código que una cuenta pidió desde su perfil para vincular <paramref name="phone"/>.</summary>
    private Task<string> IssueCodeToVerifyTheNumberAsync(PhoneNumber phone) =>
        IssueAsync(phone, LoginCodePurpose.VerifyDestination, Guid.CreateVersion7());

    private Task<string> IssueAsync(PhoneNumber phone, LoginCodePurpose purpose, Guid? requestedByUserId) =>
        factory.ExecuteScopeAsync(async services =>
        {
            const string code = "482913";
            var destination = LoginCodeDestination.ForPhone(phone);
            var codeHash = services.GetRequiredService<ILoginCodeHasher>().Hash(destination, purpose, code);

            services.GetRequiredService<ILoginCodeRepository>().Add(LoginCode.Issue(
                destination,
                purpose,
                requestedByUserId,
                codeHash,
                factory.Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(10),
                maxAttempts: 5));
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

            return code;
        });
}
