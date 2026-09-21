using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.WebUtilities;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal sealed record TokenResponse(string AccessToken, string RefreshToken, string? IdToken)
{
    public static async Task<TokenResponse> ReadAsync(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.ReadJsonAsync();

        return new TokenResponse(
            json.GetProperty("access_token").GetString()!,
            json.GetProperty("refresh_token").GetString()!,
            json.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null);
    }
}

/// <summary>Los pasos del ingreso, contra la Api real, en el mismo orden que el SPA (sección 5.2).</summary>
internal static class AuthFlow
{
    public const string AuthorizeReturnUrl = "/connect/authorize";
    public const string Scopes = "openid profile email roles offline_access api";

    /// <summary>Pide un código para <paramref name="email"/> y devuelve el que llegó por email.</summary>
    public static async Task<string> RequestCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        // Cuenta los emails antes de pedir y espera el siguiente. Si el test hizo antes un POST de código para la
        // misma dirección sin esperar su email, ese email puede llegar ahora y devolverse en lugar del de este pedido.
        var previous = factory.EmailSender.CountFor(email);

        using var response = await client.PostJsonAsync("/account/login-code", new { email });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        return CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(email, previous + 1));
    }

    /// <summary>Pide y verifica un código: el cliente queda con la cookie de sesión del servidor.</summary>
    public static async Task SignInWithCodeAsync(this HttpClient client, ApiFactory factory, string email)
    {
        var code = await client.RequestCodeAsync(factory, email);

        using var response = await client.PostJsonAsync("/account/login-code/verify", new { email, code, returnUrl = AuthorizeReturnUrl });

        if (response.StatusCode != HttpStatusCode.OK)
        {
            Assert.Fail($"verify {email} code={code} -> {response.StatusCode}: {await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)}");
        }
    }

    public static Task<HttpResponseMessage> AuthorizeAsync(this HttpClient client, string codeChallenge, string? prompt = null) =>
        client.SendAsync(HttpMethod.Get, QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = "web",
            ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["scope"] = Scopes,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"] = "test-state",
            ["prompt"] = prompt,
        }));

    /// <summary>El authorization code de la redirección a la redirect URI del cliente.</summary>
    public static string CodeFromRedirect(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.Equal(ApiFactory.WebRedirectUri, location.GetLeftPart(UriPartial.Path));

        return QueryHelpers.ParseQuery(location.Query)["code"].ToString();
    }

    public static async Task<TokenResponse> ExchangeCodeAsync(this HttpClient client, string code, string verifier)
    {
        using var response = await client.SendAsync(HttpMethod.Post, "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "web",
            ["code"] = code,
            ["redirect_uri"] = ApiFactory.WebRedirectUri,
            ["code_verifier"] = verifier,
        }));

        return await TokenResponse.ReadAsync(response);
    }

    public static Task<HttpResponseMessage> RefreshAsync(this HttpClient client, string refreshToken) =>
        client.SendAsync(HttpMethod.Post, "/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "web",
            ["refresh_token"] = refreshToken,
        }));

    /// <summary>El flujo completo: código por email, authorize con PKCE y canje en /connect/token.</summary>
    public static async Task<TokenResponse> LoginAsync(this HttpClient client, ApiFactory factory, string email)
    {
        await client.SignInWithCodeAsync(factory, email);

        var verifier = Pkce.CreateVerifier();
        using var authorize = await client.AuthorizeAsync(Pkce.ChallengeOf(verifier));

        return await client.ExchangeCodeAsync(CodeFromRedirect(authorize), verifier);
    }

    public static async Task<HttpResponseMessage> GetWithTokenAsync(
        this HttpClient client,
        string url,
        string accessToken,
        string? language = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (language is not null)
        {
            request.Headers.AcceptLanguage.ParseAdd(language);
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
