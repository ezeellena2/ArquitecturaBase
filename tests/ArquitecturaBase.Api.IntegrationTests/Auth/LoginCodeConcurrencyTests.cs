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
            .Where(loginCode => loginCode.Email == email)
            .Select(loginCode => loginCode.FailedAttempts)
            .SingleAsync(Ct));
        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits.CountAsync(audit => audit.Email == email, Ct));

        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        var problem = await right.ReadJsonAsync();

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 8), statuses);
        Assert.Equal(5, failedAttempts);
        Assert.Equal(8, audits);
        Assert.Equal(HttpStatusCode.BadRequest, right.StatusCode);
        Assert.Equal(LoginCodeErrors.TooManyAttemptsCode, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Parallel_wrong_codes_lock_an_existing_account()
    {
        using var client = factory.CreateClient();
        var email = TestEmails.Unique("parallelexisting");
        await client.SignInWithCodeAsync(factory, email);
        var code = await client.RequestCodeAsync(factory, email);

        var statuses = await StatusesOfParallelAsync(WrongCodes(code, count: 10)
            .Select(wrongCode => client.PostJsonAsync("/account/login-code/verify", new { email, code = wrongCode, returnUrl = ReturnUrl })));

        using var right = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = ReturnUrl });
        var problem = await right.ReadJsonAsync();

        // Todas 400: ninguna choca con otra al contar los fallos de la cuenta (el choque respondía 500).
        Assert.Equal(Enumerable.Repeat(HttpStatusCode.BadRequest, 10), statuses);
        Assert.Equal(HttpStatusCode.TooManyRequests, right.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
    }

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

        var codes = await factory.ExecuteDbContextAsync(db => db.LoginCodes.CountAsync(loginCode => loginCode.Email == email, Ct));

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
