using System.Net;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>Los pasos del ingreso, contra la Api real, para reutilizar en los tests.</summary>
internal static class AuthFlow
{
    public const string AuthorizeReturnUrl = "/connect/authorize";

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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
