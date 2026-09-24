using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Auth;

/// <summary>
/// El enlace de ingreso que manda el bot (secciones 5, 6.4 y 11 del spec del ingreso con WhatsApp). Los enlaces se
/// emiten con el endpoint de prueba /test/login-links, que usa el mismo emisor que va a usar el bot.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class LoginLinkTests(ApiFactory factory)
{
    private const string PreviewUrl = "/account/login-link/preview";
    private const string RedeemUrl = "/account/login-link/redeem";
    private const string SessionCookie = ".AspNetCore.Identity.Application=";
    private const string DisplayName = "Ana Pérez";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Issued_link_opens_the_login_link_page_of_the_public_origin_and_only_its_hash_is_stored()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();

        var url = await IssueUrlAsync(client, account.Id);
        var token = TokenOf(url);

        Assert.Equal("https://localhost/ingresar#t=" + token, url);
        Assert.True(ISecureTokenGenerator.HasTokenFormat(token));

        var link = await factory.ExecuteDbContextAsync(db => db.LoginLinks.AsNoTracking().SingleAsync(link => link.UserId == account.Id, Ct));
        Assert.Equal(Sha256(token), link.TokenHash);
        Assert.Equal(link.CreatedAtUtc.AddMinutes(10), link.ExpiresAtUtc);
    }

    [Fact]
    public async Task Preview_shows_the_name_and_the_masked_number_without_consuming_the_link()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));

        using var first = await PreviewAsync(client, token);
        using var second = await PreviewAsync(client, token);
        var body = await first.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(["displayName", "maskedPhone"], PropertyNames(body));
        Assert.Equal(DisplayName, body.GetProperty("displayName").GetString());
        Assert.Equal("+54 9 351 •••• " + account.PhoneNumber![^4..], body.GetProperty("maskedPhone").GetString());
        Assert.False(HasSessionCookie(first));
        Assert.Null(await ConsumedAtAsync(token));

        // Mirarlo no lo gastó: todavía sirve para entrar.
        using var redeem = await RedeemAsync(client, token);
        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
    }

    [Fact]
    public async Task Preview_of_an_account_without_a_name_or_a_number_shows_its_email()
    {
        var email = TestEmails.Unique("linkemail");
        var account = await CreateAccountAsync(displayName: null, phone: null, email: email);
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));

        using var response = await PreviewAsync(client, token);
        var body = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(email, body.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("maskedPhone").ValueKind);
    }

    [Fact]
    public async Task Redeeming_opens_the_session_and_then_authorize_issues_the_code()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));

        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
        Assert.True(HasSessionCookie(redeem));
        Assert.NotNull(await ConsumedAtAsync(token));

        // Lo que hace el SPA después: signinRedirect → /connect/authorize, que encuentra la cookie.
        var verifier = Pkce.CreateVerifier();
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(verifier));
        var tokens = await client.ExchangeCodeAsync(AuthFlow.CodeFromRedirect(authorize), verifier);

        using var me = await client.GetWithTokenAsync("/api/me", tokens.AccessToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(account.Id, (await me.ReadJsonAsync()).GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Expired_used_invented_and_invalidated_links_answer_exactly_the_same_400()
    {
        using var client = factory.CreateClient();

        var expired = TokenOf(await IssueUrlAsync(client, (await CreateAccountAsync()).Id));
        factory.Clock.Advance(LoginLink.Lifetime);

        var used = TokenOf(await IssueUrlAsync(client, (await CreateAccountAsync()).Id));
        using (var other = factory.CreateClient())
        using (var redeemed = await RedeemAsync(other, used))
        {
            Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        }

        var invalidatedAccount = await CreateAccountAsync();
        var invalidated = TokenOf(await IssueUrlAsync(client, invalidatedAccount.Id));
        await IssueUrlAsync(client, invalidatedAccount.Id);

        var invented = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        var answers = new List<(string Case, HttpStatusCode Status, bool Session, string Body)>();

        foreach (var (name, token) in new[] { ("expired", expired), ("used", used), ("invented", invented), ("invalidated", invalidated) })
        {
            foreach (var url in new[] { PreviewUrl, RedeemUrl })
            {
                using var response = await client.PostJsonAsync(url, new { token }, language: "es");
                answers.Add(($"{name} {url}", response.StatusCode, HasSessionCookie(response), WithoutTraceId(await response.ReadJsonAsync())));
            }
        }

        Assert.All(answers, answer =>
        {
            Assert.Equal(HttpStatusCode.BadRequest, answer.Status);
            Assert.False(answer.Session, answer.Case);
            Assert.Equal(answers[0].Body, answer.Body);
        });

        var problem = JsonNode.Parse(answers[0].Body)!;
        Assert.Equal(LoginLinkErrors.InvalidCode, (string?)problem["code"]);
        Assert.Equal("Este enlace ya no sirve.", (string?)problem["detail"]);
        Assert.Equal(400, (int?)problem["status"]);
    }

    [Fact]
    public async Task Invalid_link_is_translated_to_english()
    {
        using var client = factory.CreateClient();
        var invented = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        using var response = await client.PostJsonAsync(RedeemUrl, new { token = invented }, language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("This link no longer works.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Link_of_a_deleted_account_is_just_an_invalid_link()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().DeleteAsync(account.Id, Ct);

            return true;
        });

        using var preview = await PreviewAsync(client, token);
        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await preview.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(HasSessionCookie(redeem));
    }

    [Fact]
    public async Task Disabled_account_is_reported_only_after_a_valid_link_and_the_link_is_used_up()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetActiveAsync(account.Id, isActive: false, Ct);

            return true;
        });

        using var preview = await PreviewAsync(client, token);
        using var redeem = await RedeemAsync(client, token);
        using var again = await RedeemAsync(client, token);
        var problem = await redeem.ReadJsonAsync();

        // El preview no dice nada de la cuenta: responde igual que con una activa (sección 6.4 del spec).
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(DisplayName, (await preview.ReadJsonAsync()).GetProperty("displayName").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, redeem.StatusCode);
        Assert.Equal(AccountErrors.DisabledCode, problem.GetProperty("code").GetString());
        Assert.Equal("Tu cuenta está deshabilitada. Contactá a un administrador.", problem.GetProperty("detail").GetString());
        Assert.False(HasSessionCookie(redeem));

        // El enlace se gastó igual: no sirve para probar de nuevo cuando la cuenta vuelva a estar activa.
        Assert.NotNull(await ConsumedAtAsync(token));
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await again.ReadJsonAsync()).GetProperty("code").GetString());
    }

    /// <summary>
    /// Cortar el acceso es en el momento (CLAUDE.md, Fase 4): un enlace que el bot mandó antes de desactivar la cuenta
    /// no vuelve a servir si un administrador la reactiva dentro de sus 10 minutos. Lo mismo vale para desvincular el
    /// número (Tarea 15): el enlace quedó en un chat que puede no ser más de esa persona.
    /// </summary>
    [Fact]
    public async Task Cutting_off_the_access_invalidates_the_pending_links()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        using var admin = factory.CreateClient();
        var adminTokens = await admin.LoginAsync(factory, ApiFactory.AdminEmail);

        using var deactivate = await admin.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{account.Id}/deactivate", adminTokens.AccessToken);
        using var activate = await admin.SendWithTokenAsync(
            HttpMethod.Post, $"/api/users/{account.Id}/activate", adminTokens.AccessToken);
        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, activate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await redeem.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(HasSessionCookie(redeem));
    }

    [Fact]
    public async Task Revoking_sessions_invalidates_an_expired_but_pending_link()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        var tokenHash = Sha256(token);
        factory.Clock.Advance(LoginLink.Lifetime + TimeSpan.FromSeconds(1));

        var pending = await factory.ExecuteDbContextAsync(db => db.LoginLinks
            .AsNoTracking()
            .SingleAsync(link => link.TokenHash == tokenHash, Ct));
        Assert.True(pending.ExpiresAtUtc < factory.Clock.GetUtcNow().UtcDateTime);
        Assert.Null(pending.ConsumedAtUtc);
        Assert.Null(pending.InvalidatedAtUtc);

        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().RevokeSessionsAsync(account.Id, Ct);

            return true;
        });

        var invalidatedAtUtc = await factory.ExecuteDbContextAsync(db => db.LoginLinks
            .AsNoTracking()
            .Where(link => link.TokenHash == tokenHash)
            .Select(link => link.InvalidatedAtUtc)
            .SingleAsync(Ct));
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime, invalidatedAtUtc);
    }

    [Fact]
    public async Task Locked_out_account_answers_429_after_a_valid_link_and_the_link_is_used_up()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(user => user.Id == account.Id, Ct);
            user.LockoutEnd = factory.Clock.GetUtcNow().AddHours(1);

            return await db.SaveChangesAsync(Ct);
        });

        using var preview = await PreviewAsync(client, token);
        using var redeem = await RedeemAsync(client, token);
        using var again = await RedeemAsync(client, token);
        var problem = await redeem.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, redeem.StatusCode);
        Assert.Equal(AccountErrors.LockedOutCode, problem.GetProperty("code").GetString());
        Assert.False(HasSessionCookie(redeem));
        Assert.NotNull(await ConsumedAtAsync(token));

        // Con el enlace ya gastado, la cuenta sigue bloqueada pero eso no se dice: es un enlace más que no sirve
        // (sección 6.4 del spec).
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(LoginLinkErrors.InvalidCode, (await again.ReadJsonAsync()).GetProperty("code").GetString());
        Assert.False(HasSessionCookie(again));
    }

    [Fact]
    public async Task Successful_redeem_is_audited_as_whatsapp_link_with_the_number_and_resets_the_failed_attempts()
    {
        var account = await CreateAccountAsync();
        await factory.ExecuteDbContextAsync(async db =>
        {
            var user = await db.Users.SingleAsync(user => user.Id == account.Id, Ct);
            user.AccessFailedCount = 3;

            return await db.SaveChangesAsync(Ct);
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ArquitecturaBase.Tests/1.0");
        var token = TokenOf(await IssueUrlAsync(client, account.Id));

        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);

        var audit = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(audit => audit.UserId == account.Id, Ct));
        Assert.True(audit.Succeeded);
        Assert.Equal(LoginMethod.WhatsAppLink, audit.Method);
        Assert.Equal(account.PhoneNumber, audit.Identifier);
        Assert.Equal("ArquitecturaBase.Tests/1.0", audit.UserAgent);

        var failedAttempts = await factory.ExecuteDbContextAsync(db => db.Users
            .Where(user => user.Id == account.Id)
            .Select(user => user.AccessFailedCount)
            .SingleAsync(Ct));
        Assert.Equal(0, failedAttempts);
    }

    [Fact]
    public async Task Redeem_of_an_account_without_a_number_is_audited_with_its_email()
    {
        var email = TestEmails.Unique("linkaudit");
        var account = await CreateAccountAsync(phone: null, email: email);
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));

        using var redeem = await RedeemAsync(client, token);

        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
        var audit = await factory.ExecuteDbContextAsync(db => db.LoginAudits.SingleAsync(audit => audit.UserId == account.Id, Ct));
        Assert.Equal(email, audit.Identifier);
        Assert.Equal(LoginMethod.WhatsAppLink, audit.Method);
    }

    [Fact]
    public async Task Failures_with_a_link_of_an_account_are_audited_and_an_invented_link_is_not()
    {
        var account = await CreateAccountAsync();
        using var client = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(client, account.Id));
        await factory.ExecuteScopeAsync(async services =>
        {
            await services.GetRequiredService<IIdentityService>().SetActiveAsync(account.Id, isActive: false, Ct);

            return true;
        });

        using var disabled = await RedeemAsync(client, token);
        using var reused = await RedeemAsync(client, token);

        var audits = await factory.ExecuteDbContextAsync(db => db.LoginAudits
            .Where(audit => audit.UserId == account.Id)
            .Select(audit => new { audit.Succeeded, audit.Method, audit.Identifier, audit.FailureReason })
            .ToListAsync(Ct));

        Assert.Equal(2, audits.Count);
        Assert.All(audits, audit =>
        {
            Assert.False(audit.Succeeded);
            Assert.Equal(LoginMethod.WhatsAppLink, audit.Method);
            Assert.Equal(account.PhoneNumber, audit.Identifier);
        });
        Assert.Equal(
            [AccountErrors.DisabledCode, LoginLinkErrors.InvalidCode],
            audits.Select(audit => audit.FailureReason).Order(StringComparer.Ordinal));

        // Un enlace inventado no es de nadie: no hay a quién atarlo, y no deja fila.
        var before = await factory.ExecuteDbContextAsync(db => db.LoginAudits.CountAsync(Ct));
        var invented = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();
        using var inventedResponse = await RedeemAsync(client, invented);
        var after = await factory.ExecuteDbContextAsync(db => db.LoginAudits.CountAsync(Ct));

        Assert.Equal(HttpStatusCode.BadRequest, inventedResponse.StatusCode);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Simultaneous_redeems_of_the_same_link_open_a_single_session()
    {
        var account = await CreateAccountAsync();
        using var issuer = factory.CreateClient();
        var token = TokenOf(await IssueUrlAsync(issuer, account.Id));
        var clients = Enumerable.Range(0, 5).Select(_ => factory.CreateClient()).ToList();
        HttpResponseMessage[] responses = [];

        try
        {
            responses = await Task.WhenAll(clients.Select(client => RedeemAsync(client, token)));

            HttpStatusCode[] expected = [HttpStatusCode.NoContent, .. Enumerable.Repeat(HttpStatusCode.BadRequest, 4)];
            Assert.Equal(expected, responses.Select(response => response.StatusCode).Order());
            Assert.Single(responses, HasSessionCookie);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }

            foreach (var client in clients)
            {
                client.Dispose();
            }
        }

        var successes = await factory.ExecuteDbContextAsync(db => db.LoginAudits.CountAsync(audit => audit.UserId == account.Id && audit.Succeeded, Ct));
        Assert.Equal(1, successes);
    }

    [Theory]
    [InlineData(PreviewUrl)]
    [InlineData(RedeemUrl)]
    public async Task Malformed_or_missing_token_is_a_validation_error(string url)
    {
        using var client = factory.CreateClient();

        using var missing = await client.PostJsonAsync(url, new { token = (string?)null }, language: "es");
        using var truncated = await client.PostJsonAsync(url, new { token = "AAAAAAAAAAAAAAAAAAAAAA" }, language: "es");
        var missingProblem = await missing.ReadJsonAsync();
        var truncatedProblem = await truncated.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("Validation.Failed", missingProblem.GetProperty("code").GetString());
        Assert.Equal("Este campo es obligatorio.", missingProblem.GetProperty("errors").GetProperty("token")[0].GetString());

        Assert.Equal(HttpStatusCode.BadRequest, truncated.StatusCode);
        Assert.Equal("Validation.Failed", truncatedProblem.GetProperty("code").GetString());
        Assert.Equal(
            "El enlace está incompleto. Abrilo de nuevo desde WhatsApp.",
            truncatedProblem.GetProperty("errors").GetProperty("token")[0].GetString());
    }

    [Theory]
    [InlineData(PreviewUrl)]
    [InlineData(RedeemUrl)]
    public async Task Link_endpoints_only_accept_json(string url)
    {
        using var client = factory.CreateClient();
        var token = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        using var form = await client.SendAsync(HttpMethod.Post, url, new FormUrlEncodedContent([new("token", token)]));
        using var plain = await client.SendAsync(
            HttpMethod.Post, url, new StringContent($$"""{"token":"{{token}}"}""", Encoding.UTF8, "text/plain"));

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, form.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, plain.StatusCode);
    }

    [Fact]
    public async Task Link_endpoints_share_the_rate_limit_of_code_verifications()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:LoginVerifyPermitLimit", "2"));
        using var client = api.CreateClient();
        var token = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        using var preview = await client.PostJsonAsync(PreviewUrl, new { token });
        using var redeem = await client.PostJsonAsync(RedeemUrl, new { token });
        using var verify = await client.PostJsonAsync(
            "/account/login-code/verify", new { email = TestEmails.Unique("linklimit"), code = "000000", returnUrl = AuthFlow.AuthorizeReturnUrl });
        var problem = await verify.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, verify.StatusCode);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task No_log_carries_the_token_its_hash_the_url_or_the_number()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddLogging(logging => logging
                .AddFakeLogging()
                .AddFilter<FakeLoggerProvider>(category: null, LogLevel.Trace))));
        using var client = api.CreateClient();
        var account = await CreateAccountAsync();
        var url = await IssueUrlAsync(client, account.Id);
        var token = TokenOf(url);
        var invented = factory.Services.GetRequiredService<ISecureTokenGenerator>().Generate();

        using var preview = await PreviewAsync(client, token);
        using var redeem = await RedeemAsync(client, token);
        using var reused = await RedeemAsync(client, token);
        using var inventedPreview = await PreviewAsync(client, invented);
        using var inventedRedeem = await RedeemAsync(client, invented);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, redeem.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inventedRedeem.StatusCode);

        var records = api.Services.GetFakeLogCollector().GetSnapshot();
        var logged = records
            .Select(record => string.Join(
                "\n",
                [
                    record.Category ?? string.Empty,
                    record.Message,
                    record.Exception?.ToString() ?? string.Empty,
                    .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? [],
                    .. record.Scopes.Select(scope => scope?.ToString() ?? string.Empty),
                ]))
            .ToList();

        // Que el log se haya capturado de verdad: los casos de uso dejan su línea.
        Assert.Contains(records, record => record.Message.Contains("RedeemLoginLinkCommand", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Message.Contains("PreviewLoginLinkQuery", StringComparison.Ordinal));

        string[] secrets =
        [
            token,
            url,
            Sha256(token),
            Sha256(token).ToLowerInvariant(),
            invented,
            Sha256(invented),
            Sha256(invented).ToLowerInvariant(),
            account.PhoneNumber!,
            account.PhoneNumber![1..],
        ];
        var leaks = secrets
            .SelectMany(secret => logged.Where(text => text.Contains(secret, StringComparison.Ordinal)).Select(text => secret + " in: " + text))
            .ToList();

        Assert.True(leaks.Count == 0, string.Join(Environment.NewLine + "---" + Environment.NewLine, leaks));
    }

    private static string TokenOf(string url) => url[(url.IndexOf("#t=", StringComparison.Ordinal) + "#t=".Length)..];

    private static string Sha256(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool HasSessionCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(cookie => cookie.StartsWith(SessionCookie, StringComparison.Ordinal));

    private static string[] PropertyNames(JsonElement body) =>
        [.. body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)];

    private static string WithoutTraceId(JsonElement problem)
    {
        var node = JsonNode.Parse(problem.GetRawText())!.AsObject();
        node.Remove("traceId");

        return node.ToJsonString();
    }

    private static Task<HttpResponseMessage> PreviewAsync(HttpClient client, string token) =>
        client.PostJsonAsync(PreviewUrl, new { token }, language: "es");

    private static Task<HttpResponseMessage> RedeemAsync(HttpClient client, string token) =>
        client.PostJsonAsync(RedeemUrl, new { token }, language: "es");

    private static async Task<string> IssueUrlAsync(HttpClient client, Guid userId)
    {
        using var response = await client.PostJsonAsync("/test/login-links", new { userId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("url").GetString()!;
    }

    /// <summary>Por defecto, una cuenta con nombre y solo con un número verificado, como las que crea el bot.</summary>
    private Task<UserAccount> CreateAccountAsync(string? displayName = DisplayName, PhoneNumber? phone = null, string? email = null)
    {
        var number = email is null ? phone ?? TestPhones.Unique() : phone;

        return factory.ExecuteScopeAsync(services => services.GetRequiredService<IIdentityService>().CreateAsync(
            email is null ? null : Email.Create(email).Value,
            number,
            phoneConfirmed: number is not null,
            displayName,
            "es",
            Ct));
    }

    private Task<DateTime?> ConsumedAtAsync(string token)
    {
        var tokenHash = Sha256(token);

        return factory.ExecuteDbContextAsync(db => db.LoginLinks
            .Where(link => link.TokenHash == tokenHash)
            .Select(link => link.ConsumedAtUtc)
            .SingleAsync(Ct));
    }
}
