using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Persistence;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

[Collection(ApiTestGroup.Name)]
public sealed class RegistrationModeTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invite_only_answers_202_and_sends_nothing_to_an_email_without_an_account()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var unknown = TestEmails.Unique("uninvited");
        var invited = await CreateAccountAsync("invited");

        using var unknownResponse = await client.PostJsonAsync("/account/login-code", new { email = unknown });
        using var invitedResponse = await client.PostJsonAsync("/account/login-code", new { email = invited });

        // La cola de emails es FIFO y la vacía un solo lector: cuando llega el del correo invitado, el del correo
        // desconocido ya habría llegado si se hubiera encolado.
        await factory.EmailSender.WaitForAsync(invited);

        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, invitedResponse.StatusCode);

        // Byte a byte la misma respuesta: no hay nada en el cuerpo que distinga una dirección de la otra.
        Assert.Equal(
            await invitedResponse.Content.ReadAsStringAsync(Ct),
            await unknownResponse.Content.ReadAsStringAsync(Ct));

        Assert.Equal(0, factory.EmailSender.CountFor(unknown));

        // El código se emitió igual, aunque no se haya mandado: es lo que sostiene los límites por dirección.
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Destination == unknown, Ct)));
    }

    [Fact]
    public async Task Invite_only_stores_the_code_of_an_email_without_an_account_as_never_sent()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var unknown = TestEmails.Unique("unsent");
        var invited = await CreateAccountAsync("sent");

        using var unknownResponse = await client.PostJsonAsync("/account/login-code", new { email = unknown });
        using var invitedResponse = await client.PostJsonAsync("/account/login-code", new { email = invited });

        var sentAt = await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(code => code.Destination == unknown || code.Destination == invited)
            .ToDictionaryAsync(code => code.Destination, code => code.SentAtUtc, Ct));

        // Sin fecha de envío: lo que no salió no cuenta como un mensaje mandado.
        Assert.Null(sentAt[unknown]);
        Assert.NotNull(sentAt[invited]);
    }

    [Fact]
    public async Task Insisting_with_an_unknown_email_is_limited_exactly_like_a_registered_one()
    {
        // El modo se cambia antes de levantar la Api hija: tiene su propio contenedor, así que su caché de ajustes
        // arranca frío y lee la fila en la primera petición.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        var known = await CreateAccountAsync("limited");
        var unknown = TestEmails.Unique("limited");

        // El arnés no espera entre pedidos: para probar el límite hace falta el valor real.
        await using var api = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60"));
        using var client = api.CreateClient();

        using var firstUnknown = await client.PostJsonAsync("/account/login-code", new { email = unknown });
        using var firstKnown = await client.PostJsonAsync("/account/login-code", new { email = known });
        factory.Clock.Advance(TimeSpan.FromSeconds(15));
        using var secondUnknown = await client.PostJsonAsync("/account/login-code", new { email = unknown }, language: "es");
        using var secondKnown = await client.PostJsonAsync("/account/login-code", new { email = known }, language: "es");
        var unknownProblem = await secondUnknown.ReadJsonAsync();
        var knownProblem = await secondKnown.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Accepted, firstUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, firstKnown.StatusCode);

        // Lo que cierra el agujero: insistir con una dirección desconocida responde igual que con una registrada.
        // Si el código no se emitiera, acá la desconocida seguiría respondiendo 202 y la registrada, 429.
        Assert.Equal(HttpStatusCode.TooManyRequests, secondUnknown.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, secondKnown.StatusCode);
        Assert.Equal("Auth.LoginCode.ResendTooSoon", unknownProblem.GetProperty("code").GetString());
        Assert.Equal(
            knownProblem.GetProperty("code").GetString(),
            unknownProblem.GetProperty("code").GetString());
        Assert.Equal(45, unknownProblem.GetProperty("retryAfter").GetInt32());
        Assert.Equal(
            knownProblem.GetProperty("retryAfter").GetInt32(),
            unknownProblem.GetProperty("retryAfter").GetInt32());
        Assert.Equal(
            knownProblem.GetProperty("detail").GetString(),
            unknownProblem.GetProperty("detail").GetString());

        // Solo el `traceId` distingue los dos cuerpos, y cambia en cada petición.
        Assert.Equal(0, factory.EmailSender.CountFor(unknown));
    }

    [Fact]
    public async Task Open_mode_still_creates_the_account_of_an_unknown_email()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("open");

        var code = await client.RequestCodeAsync(factory, email);

        Assert.Matches("^[0-9]{6}$", code);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(stored => stored.Destination == email, Ct)));
    }

    [Fact]
    public async Task Invite_only_sends_a_google_sign_in_without_an_account_back_to_login_with_the_error()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("notinvited");
        using var external = await client.PostJsonAsync(
            "/test/external-login",
            new { providerKey = "google-" + email, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal("/login?error=Auth.Account.NotInvited", callback.Headers.Location!.OriginalString);
        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.LoginAudits.AnyAsync(audit => audit.Identifier == email && audit.FailureReason == "Auth.Account.NotInvited", Ct)));
    }

    [Fact]
    public async Task Invite_only_lets_google_in_when_the_account_already_exists()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = await CreateAccountAsync("googleinvited");
        var providerKey = "google-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        using var external = await client.PostJsonAsync(
            "/test/external-login", new { providerKey, email, name = "Ana", emailVerified = true });

        using var callback = await client.SendAsync(
            HttpMethod.Get, "/account/external/callback?returnUrl=" + Uri.EscapeDataString(AuthFlow.AuthorizeReturnUrl));

        Assert.True(external.IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.Equal(AuthFlow.AuthorizeReturnUrl, callback.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Invite_only_rejects_a_valid_code_of_an_email_without_an_account_and_creates_nothing()
    {
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("uninvitedverify");

        // En InviteOnly el pedido no le manda el código a un correo sin cuenta, pero un código válido puede existir
        // igual (uno pedido en Open justo antes de cerrar el registro, por ejemplo). Con él, el verify creaba la
        // cuenta: es el hallazgo 1 de la etapa 1.
        var code = await IssueSignInCodeAsync(email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(AccountErrors.NotInvitedCode, problem.GetProperty("code").GetString());
        Assert.Equal(
            "Todavía no tenés acceso al sistema. Pedile a un administrador que te dé de alta.",
            problem.GetProperty("detail").GetString());
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal)));

        Assert.False(await factory.ExecuteDbContextAsync(db => db.Users
            .IgnoreQueryFilters([ModelBuilderExtensions.SoftDeleteFilter])
            .AnyAsync(user => user.Email == email, Ct)));

        // El comando guarda aunque falle (IPersistChangesOnFailure): el código queda gastado y el rechazo, auditado.
        var stored = await factory.ExecuteDbContextAsync(db => db.LoginCodes.SingleAsync(loginCode => loginCode.Destination == email, Ct));
        Assert.NotNull(stored.ConsumedAtUtc);

        var audit = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(entry => entry.Identifier == email, Ct));
        Assert.False(audit.Succeeded);
        Assert.Equal(LoginMethod.Code, audit.Method);
        Assert.Equal(AccountErrors.NotInvitedCode, audit.FailureReason);
        Assert.Null(audit.UserId);
    }

    [Fact]
    public async Task Invite_only_lets_an_existing_account_sign_in_with_a_code()
    {
        // El modo decide quién puede crear una cuenta, no quién puede entrar.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.InviteOnly);
        using var client = factory.CreateClient();
        var email = await CreateAccountAsync("invitedverify");
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await factory.ExecuteDbContextAsync(db =>
            db.LoginAudits.AnyAsync(audit => audit.Identifier == email && audit.Succeeded, Ct)));
    }

    [Fact]
    public async Task Open_mode_creates_the_account_when_a_valid_code_of_an_unknown_email_is_verified()
    {
        // La contracara del rechazo en InviteOnly: el mismo código, emitido igual, con el registro abierto.
        await using var mode = await RegistrationModeScope.SetAsync(factory, RegistrationMode.Open);
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("openverify");
        var code = await IssueSignInCodeAsync(email);

        using var response = await client.PostJsonAsync(
            "/account/login-code/verify", new { email, code, returnUrl = AuthFlow.AuthorizeReturnUrl });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(await factory.ExecuteDbContextAsync(db => db.Users.AnyAsync(user => user.Email == email, Ct)));
    }

    /// <summary>
    /// Emite y guarda un código de ingreso válido para <paramref name="email"/> sin pasar por el pedido, así existe
    /// aunque el pedido no lo hubiera mandado. Devuelve el código en claro.
    /// </summary>
    private Task<string> IssueSignInCodeAsync(string email) =>
        factory.ExecuteScopeAsync(async services =>
        {
            const string code = "482913";
            var destination = LoginCodeDestination.ForEmail(Email.Create(email).Value);
            var codeHash = services.GetRequiredService<ILoginCodeHasher>().Hash(destination, LoginCodePurpose.SignIn, code);

            services.GetRequiredService<ILoginCodeRepository>().Add(LoginCode.Issue(
                destination,
                LoginCodePurpose.SignIn,
                requestedByUserId: null,
                codeHash,
                factory.Clock.GetUtcNow().UtcDateTime,
                TimeSpan.FromMinutes(10),
                maxAttempts: 5));
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Ct);

            return code;
        });

    private Task<string> CreateAccountAsync(string prefix)
    {
        var email = TestEmails.Unique(prefix);

        return factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>()
                .CreateAsync(Email.Create(email).Value, displayName: null, "es", Ct);

            return email;
        });
    }
}
