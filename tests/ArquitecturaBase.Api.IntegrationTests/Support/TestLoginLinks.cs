using System.Net;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

/// <summary>
/// Los enlaces de ingreso del endpoint de prueba /test/login-links, que usa el mismo emisor que un canal de teléfono: un
/// test del núcleo tiene un enlace pendiente sin depender de un módulo.
/// </summary>
internal static class TestLoginLinks
{
    /// <summary>Emite un enlace para la cuenta y devuelve su URL, con el token en el fragmento.</summary>
    public static async Task<string> IssueUrlAsync(HttpClient client, Guid userId)
    {
        using var response = await client.PostJsonAsync("/test/login-links", new { userId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.ReadJsonAsync()).GetProperty("url").GetString()!;
    }

    /// <summary>El token de la URL de un enlace: lo que sigue a <c>#t=</c>.</summary>
    public static string TokenOf(string url) => url[(url.IndexOf("#t=", StringComparison.Ordinal) + "#t=".Length)..];
}
