using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
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
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(code => code.Email == unknown, Ct)));
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
        Assert.True(await factory.ExecuteDbContextAsync(db => db.LoginCodes.AnyAsync(stored => stored.Email == email, Ct)));
    }

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
