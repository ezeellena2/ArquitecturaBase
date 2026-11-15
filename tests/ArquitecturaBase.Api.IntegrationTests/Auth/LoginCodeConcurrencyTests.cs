using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authentication;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// Pedidos y verificaciones simultáneos para un mismo email: los límites de la sección 5.3 se cumplen igual que si
/// llegaran de a uno.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class LoginCodeConcurrencyTests(ApiFactory factory)
{
    private const string ReturnUrl = "/connect/authorize";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Parallel_wrong_codes_count_every_attempt()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("parallelnew");
        var code = await client.RequestCodeAsync(factory, email);

        var statuses = await StatusesOfParallelAsync(WrongCodes(code, count: 8)
            .Select(wrongCode => client.PostJsonAsync("/account/login-code/verify", new { email, code = wrongCode, returnUrl = ReturnUrl })));

        var failedAttempts = await factory.ExecuteDbContextAsync(db => db.LoginCodes
            .Where(loginCode => loginCode.Destination == email)
            .Select(loginCode => loginCode.FailedAttempts)
            .SingleAsync(Ct));
        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits.CountAsync(audit => audit.Identifier == email, Ct));

        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        var problem = await right.ReadJsonAsync();

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 8), statuses);
        Assert.Equal(5, failedAttempts);
        Assert.Equal(8, audits);
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);
        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// Diez fallos simultáneos contra códigos activos bloquean la cuenta. Sólo cuentan los que van contra un código activo
    /// (N3): cada código admite 5, así que hacen falta dos. El bloqueo se dice recién con el código correcto del tercero.
    /// </summary>
    [Fact]
    public async Task Parallel_wrong_codes_lock_an_existing_account()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("parallelexisting");
        await client.SignInWithCodeAsync(factory, email);
        List<HttpStatusCode> statuses = [];

        for (var round = 0; round < 2; round++)
        {
            // El reloj del arnés no avanza solo: así cada código es el más nuevo sin empatar con el anterior.
            factory.Clock.Advance(TimeSpan.FromSeconds(1));
            var code = await client.RequestCodeAsync(factory, email);
            statuses.AddRange(await StatusesOfParallelAsync(WrongCodes(code, count: 5)
                .Select(wrongCode => client.PostJsonAsync("/account/login-code/verify", new { email, code = wrongCode, returnUrl = ReturnUrl }))));
        }

        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var lastCode = await client.RequestCodeAsync(factory, email);
        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code = lastCode, returnUrl = ReturnUrl });
        var problem = await right.ReadJsonAsync();

        // Todas 400: ninguna choca con otra al contar los fallos de la cuenta (el choque respondía 500).
        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 10), statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// N3: dos canjes simultáneos del código correcto, desde dos navegadores, abren una sola sesión. El lock del destino
    /// los ordena: el segundo encuentra el código gastado y, como coincide, responde que ya se usó, sin cookie.
    /// </summary>
    [Fact]
    public async Task Two_simultaneous_redemptions_of_the_right_code_open_a_single_session()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        var email = TestEmails.Unique("parallelright");
        await first.SignInWithCodeAsync(factory, email);
        // Más nuevo que el del primer ingreso: si empataran, el segundo canje podría verificar el código anterior.
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        var code = await first.RequestCodeAsync(factory, email);

        HttpResponseMessage[] responses = [];
        try
        {
            responses = await Task.WhenAll(new[] { first, second }
                .Select(client => client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl })));

            var signedIn = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var rejected = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(LoginCodeErrors.AlreadyUsedCode, (await rejected.ReadJsonAsync()).GetProperty("code").GetString());
            Assert.True(SetsTheSessionCookie(signedIn));
            Assert.False(SetsTheSessionCookie(rejected));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        var successes = await factory.ExecuteDbContextAsync(db => db.LoginAudits
            .CountAsync(audit => audit.Identifier == email && audit.Succeeded, Ct));
        Assert.Equal(2, successes);
    }

    private static bool SetsTheSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));

    [Fact]
    public async Task Parallel_code_requests_for_the_same_email_issue_a_single_code()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:LoginCode:ResendCooldownSeconds", "60")
            .UseSetting("Authentication:LoginCode:MaxRequestsPerWindow", "5"));
        using var client = api.CreateClient();
        var email = TestEmails.Unique("parallelrequest");

        var statuses = await StatusesOfParallelAsync(Enumerable.Range(0, 10)
            .Select(_ => client.PostJsonAsync("/account/login-code", new { email })));

        var codes = await factory.ExecuteDbContextAsync(db => db.LoginCodes.CountAsync(loginCode => loginCode.Destination == email, Ct));

        HttpStatusCode[] expected = [HttpStatusCode.Accepted, .. Enumerable.Repeat(HttpStatusCode.TooManyRequests, 9)];
        Assert.Equal(expected, statuses.Order());
        Assert.Equal(1, codes);
    }

    /// <summary>
    /// Manda los requests a la vez (<see cref="Task.WhenAll{TResult}(IEnumerable{Task{TResult}})"/> los inicia al
    /// recorrer la secuencia) y devuelve sus status. Las respuestas se liberan siempre.
    /// </summary>
    private static async Task<HttpStatusCode[]> StatusesOfParallelAsync(IEnumerable<Task<HttpResponseMessage>> requests)
    {
        HttpResponseMessage[] responses = [];

        try
        {
            responses = await Task.WhenAll(requests);

            return [.. responses.Select(response => response.StatusCode)];
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    // Distintos entre sí y del correcto.
    private static IEnumerable<string> WrongCodes(string code, int count)
    {
        var right = int.Parse(code, CultureInfo.InvariantCulture);

        return Enumerable.Range(1, count)
            .Select(offset => ((right + offset) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture));
    }
}
