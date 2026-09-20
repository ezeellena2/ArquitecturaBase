using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Hosting;

/// <summary>
/// En producción la Api sirve el build del SPA: las rutas del navegador caen en el index.html, las del backend
/// siguen devolviendo ProblemDetails y toda respuesta lleva los encabezados de seguridad.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class SpaHostingTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/usuarios")]
    [InlineData("/login/codigo")]
    public async Task Spa_routes_are_served_with_the_index(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("spa", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/api/no-existe")]
    [InlineData("/account/no-existe")]
    [InlineData("/connect/no-existe")]
    [InlineData("/health/no-existe")]
    public async Task Backend_routes_keep_returning_a_problem(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
    }

    /// <summary>
    /// El fallback no puede ser candidato de las rutas del backend: si lo fuera, sería un candidato válido para
    /// cualquier método y cualquier tipo de contenido, y se comería el 405 del método incorrecto y el 415 del
    /// cuerpo que no es JSON (la protección contra el CSRF con enctype="text/plain").
    /// </summary>
    [Fact]
    public async Task Backend_routes_keep_rejecting_the_wrong_method_and_content_type()
    {
        using var client = factory.CreateClient();
        using var plainText = new StringContent("{}", Encoding.UTF8, "text/plain");

        using var wrongMethod = await client.SendAsync(HttpMethod.Delete, "/account/login-code");
        using var wrongContentType = await client.SendAsync(HttpMethod.Post, "/account/login-code", plainText);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongContentType.StatusCode);
    }

    /// <summary>
    /// Los assets con hash y la página del iframe de renovación se sirven como archivos, con su propio tipo de
    /// contenido: si cayeran en el index.html, el navegador rechazaría los módulos y el SPA no arrancaría.
    /// </summary>
    [Theory]
    [InlineData("/assets/main.js", "text/javascript", ApiFactory.AssetMarker)]
    [InlineData("/silent-renew.html", "text/html", ApiFactory.SilentRenewMarker)]
    public async Task Static_files_are_served_as_themselves(string url, string mediaType, string expected)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task Responses_carry_the_security_headers()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("form-action 'self' https://accounts.google.com", policy, StringComparison.Ordinal);
    }

    /// <summary>El index.html no se cachea: nombra los assets por hash y después de un despliegue cambia.</summary>
    [Fact]
    public async Task The_index_is_not_cached()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/usuarios");

        Assert.Equal("no-cache", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Api_responses_are_not_cached_by_mistake()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
    }
}
