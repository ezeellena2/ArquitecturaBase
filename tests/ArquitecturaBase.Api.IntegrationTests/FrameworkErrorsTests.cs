using System.Globalization;
using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

/// <summary>
/// Los errores que arma el propio ASP.NET (ruta inexistente, método incorrecto, autorización, respuestas vacías)
/// salen con el mismo formato ProblemDetails que los nuestros.
/// Las rutas de acá son del backend a propósito: el resto del sitio lo sirve el SPA (ver SpaHostingTests).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FrameworkErrorsTests(ApiFactory factory)
{
    [Fact]
    public async Task Unknown_route_returns_a_translated_404_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/does-not-exist", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("No encontrado", problem.GetProperty("title").GetString());
        Assert.Equal("No encontramos lo que buscás.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Wrong_http_method_returns_a_translated_405_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Delete, "/test/widgets", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("Http.MethodNotAllowed", problem.GetProperty("code").GetString());
        Assert.Equal("Operation not allowed", problem.GetProperty("title").GetString());
        Assert.Equal("This operation is not allowed on this resource.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Anonymous_request_to_a_protected_endpoint_returns_a_401_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/protected", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
        Assert.Equal("Tenés que iniciar sesión para continuar.", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task User_without_the_required_role_gets_a_403_problem()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/test/admin", language: "es", userId: Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.Equal("Acceso denegado", problem.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(429, "Http.TooManyRequests")]
    [InlineData(503, "General.Unexpected")]
    public async Task Empty_error_responses_are_completed_as_problems(int status, string code)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/test/status/" + status.ToString(CultureInfo.InvariantCulture), language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(code, problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Successful_empty_responses_are_not_touched()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get, "/test/protected", userId: Guid.CreateVersion7().ToString("D", CultureInfo.InvariantCulture));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
    }
}
