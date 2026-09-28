using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class OpenApiTests(ApiFactory factory)
{
    private const string ProblemDetailsSchema = "#/components/schemas/ProblemDetails";

    private static readonly HashSet<string> HttpMethods = new(StringComparer.Ordinal)
    {
        "get", "put", "post", "delete", "patch", "head", "options", "trace",
    };

    // Un 202 sin cuerpo: lo aceptado se procesa aparte y no queda un recurso para consultar
    // (ControllerResultExtensions.ToAcceptedResult). Es la única respuesta de éxito de /api, fuera de los 204, sin esquema.
    private static readonly HashSet<string> AcceptedWithoutBody = new(StringComparer.Ordinal)
    {
        "POST /api/users/{id}/invitation",
    };

    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_served_in_development()
    {
        await using var development = DevelopmentApi();
        using var client = development.CreateClient();

        using var swagger = await client.SendAsync(HttpMethod.Get, "/swagger/index.html");
        using var document = await client.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        var html = await swagger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        var paths = (await document.ReadJsonAsync()).GetProperty("paths");
        var settings = paths.GetProperty("/api/settings");
        Assert.Equal("Users", paths.GetProperty("/api/users").GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.Equal("Users", paths.GetProperty("/api/users/filter-counts").GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.Equal("Users", paths.GetProperty("/api/users/{id}").GetProperty("get").GetProperty("tags")[0].GetString());
        var me = paths.GetProperty("/api/me");
        Assert.Equal("Users", me.GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.True(me.GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        Assert.Equal("Users", me.GetProperty("put").GetProperty("tags")[0].GetString());
        Assert.True(me.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(me.GetProperty("put").GetProperty("responses").TryGetProperty("204", out _));

        var profileWhatsAppCode = paths.GetProperty("/api/me/whatsapp/code").GetProperty("post");
        Assert.Equal("Users", profileWhatsAppCode.GetProperty("tags")[0].GetString());
        Assert.True(profileWhatsAppCode.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(profileWhatsAppCode.GetProperty("responses").TryGetProperty("202", out _));
        var profileWhatsApp = paths.GetProperty("/api/me/whatsapp");
        Assert.Equal("Users", profileWhatsApp.GetProperty("put").GetProperty("tags")[0].GetString());
        Assert.True(profileWhatsApp.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(profileWhatsApp.GetProperty("put").GetProperty("responses").TryGetProperty("204", out _));
        Assert.Equal("Users", profileWhatsApp.GetProperty("delete").GetProperty("tags")[0].GetString());
        Assert.False(profileWhatsApp.GetProperty("delete").TryGetProperty("requestBody", out _));
        Assert.True(profileWhatsApp.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _));

        var userCreate = paths.GetProperty("/api/users").GetProperty("post");
        Assert.Equal("Users", userCreate.GetProperty("tags")[0].GetString());
        Assert.True(userCreate.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(userCreate.GetProperty("responses").TryGetProperty("201", out _));
        var userUpdate = paths.GetProperty("/api/users/{id}").GetProperty("put");
        Assert.Equal("Users", userUpdate.GetProperty("tags")[0].GetString());
        Assert.True(userUpdate.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(userUpdate.GetProperty("responses").TryGetProperty("204", out _));
        var userInvitation = paths.GetProperty("/api/users/{id}/invitation").GetProperty("post");
        Assert.Equal("Users", userInvitation.GetProperty("tags")[0].GetString());
        Assert.True(userInvitation.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(userInvitation.GetProperty("responses").TryGetProperty("202", out _));

        foreach (var action in new[] { "activate", "deactivate" })
        {
            var status = paths.GetProperty($"/api/users/{{id}}/{action}").GetProperty("post");
            Assert.Equal("Users", status.GetProperty("tags")[0].GetString());
            Assert.False(status.TryGetProperty("requestBody", out _));
            Assert.True(status.GetProperty("responses").TryGetProperty("204", out _));
        }
        Assert.Equal("Settings", settings.GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.Equal("Settings", settings.GetProperty("put").GetProperty("tags")[0].GetString());
        Assert.True(settings.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));

        var loginCode = paths.GetProperty("/account/login-code").GetProperty("post");
        Assert.Equal("Account", loginCode.GetProperty("tags")[0].GetString());
        Assert.True(loginCode.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(loginCode.GetProperty("responses").TryGetProperty("202", out _));

        var whatsAppLoginCode = paths.GetProperty("/account/login-code/whatsapp").GetProperty("post");
        Assert.Equal("Account", whatsAppLoginCode.GetProperty("tags")[0].GetString());
        Assert.True(whatsAppLoginCode.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(whatsAppLoginCode.GetProperty("responses").TryGetProperty("202", out _));

        var verifyLoginCode = paths.GetProperty("/account/login-code/verify").GetProperty("post");
        Assert.Equal("Account", verifyLoginCode.GetProperty("tags")[0].GetString());
        Assert.True(verifyLoginCode.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(verifyLoginCode.GetProperty("responses").TryGetProperty("200", out _));

        var roles = paths.GetProperty("/api/roles");
        Assert.Equal("Roles", roles.GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.Equal("Roles", roles.GetProperty("post").GetProperty("tags")[0].GetString());
        Assert.True(roles.GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));

        var roleById = paths.GetProperty("/api/roles/{id}");
        Assert.Equal("Roles", roleById.GetProperty("put").GetProperty("tags")[0].GetString());
        Assert.Equal("Roles", roleById.GetProperty("delete").GetProperty("tags")[0].GetString());
        Assert.True(roleById.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.Equal("Roles", paths.GetProperty("/api/permissions").GetProperty("get").GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Every_api_operation_declares_a_success_schema_and_its_errors_as_problem_details()
    {
        var paths = (await ReadDocumentAsync()).GetProperty("paths");

        var operations = paths.EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => HttpMethods.Contains(operation.Name))
                .Select(operation => (
                    Name: $"{operation.Name.ToUpperInvariant()} {path.Name}",
                    Responses: operation.Value.GetProperty("responses"))))
            .ToArray();
        var failures = operations.SelectMany(operation => DescribeFailures(operation.Name, operation.Responses)).ToArray();

        Assert.NotEmpty(operations);
        Assert.Empty(failures);
    }

    [Fact]
    public async Task Each_operation_declares_only_the_errors_it_can_answer()
    {
        var paths = (await ReadDocumentAsync()).GetProperty("paths");

        // Anónima y con cuerpo: sin 401 ni 403. El 429 sale de su [EnableRateLimiting].
        AssertErrors(paths, "post", "/account/login-code", "400", "429", "500");

        // Con permiso y sin entrada: sin 400 ni 404.
        AssertErrors(paths, "get", "/api/permissions", "401", "403", "500");

        // La query de un listado puede ser inválida.
        AssertErrors(paths, "get", "/api/users", "400", "401", "403", "500");

        // El recurso de la ruta puede no existir.
        AssertErrors(paths, "get", "/api/users/{id}", "401", "403", "404", "500");

        // El 404 y el 409 los declara la acción: un rol pedido que no existe, y el correo o el número de otra cuenta.
        AssertErrors(paths, "post", "/api/users", "400", "401", "403", "404", "409", "500");

        // [Authorize] solo pide sesión, así que nunca responde 403. El 404 lo declara el controller: la cuenta de la
        // sesión puede haberse borrado.
        AssertErrors(paths, "get", "/api/me", "401", "404", "500");
    }

    [Fact]
    public async Task Conflicts_rate_limits_and_refusals_of_anonymous_actions_are_declared()
    {
        var paths = (await ReadDocumentAsync()).GetProperty("paths");

        // Anónimas, pero rechazan una cuenta desactivada (y el código, una sin invitación): 403 sin 401. Las dos tienen
        // [EnableRateLimiting], así que declaran el 429.
        AssertErrors(paths, "post", "/account/login-code/verify", "400", "403", "429", "500");
        AssertErrors(paths, "post", "/account/login-link/redeem", "400", "403", "429", "500");

        // La vista previa del enlace no mira la cuenta: tiene rate limit, pero no 403.
        AssertErrors(paths, "post", "/account/login-link/preview", "400", "429", "500");

        // Con sesión y rate limit, pero sin permiso: 429 y el 409 del correo de otra cuenta, sin 403.
        AssertErrors(paths, "put", "/api/me/email", "400", "401", "404", "409", "429", "500");

        // El 409 de un rol con el mismo nombre, al editar como al crear.
        AssertErrors(paths, "put", "/api/roles/{id}", "400", "401", "403", "404", "409", "500");

        // Desactivar puede chocar con la propia cuenta o el último administrador; activar, no.
        AssertErrors(paths, "post", "/api/users/{id}/deactivate", "401", "403", "404", "409", "500");
        AssertErrors(paths, "post", "/api/users/{id}/activate", "401", "403", "404", "500");

        // Sin [EnableRateLimiting]: el 429 de la espera entre invitaciones lo declara la acción.
        AssertErrors(paths, "post", "/api/users/{id}/invitation", "400", "401", "403", "404", "429", "500");
    }

    [Theory]
    [InlineData("/swagger/index.html")]
    [InlineData("/openapi/v1.json")]
    public async Task Documentation_is_not_exposed_outside_development(string url)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// La Api en Development, que es donde se mapea el documento. Al arrancar aplica las migraciones y el seed: se le da
    /// una base vacía propia y el ApplicationDbContext de producción (el TestDbContext del arnés suma Widgets, que no
    /// están en las migraciones).
    /// </summary>
    private WebApplicationFactory<Program> DevelopmentApi() => factory.WithWebHostBuilder(builder => builder
        .UseEnvironment("Development")
        .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("development"))
        .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
            new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())))));

    private async Task<JsonElement> ReadDocumentAsync()
    {
        await using var development = DevelopmentApi();
        using var client = development.CreateClient();
        using var response = await client.SendAsync(HttpMethod.Get, "/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync();
    }

    private static IEnumerable<string> DescribeFailures(string operation, JsonElement responses)
    {
        var declared = responses.EnumerateObject().ToArray();
        var successes = declared.Where(response => response.Name.StartsWith('2')).ToArray();
        var errors = declared.Where(response => IsError(response.Name)).ToArray();

        if (successes.Length == 0)
        {
            yield return $"{operation}: declares no success response";
        }

        foreach (var success in successes.Where(success =>
            success.Name != "204" && !AcceptedWithoutBody.Contains(operation) && !HasSchema(success.Value)))
        {
            yield return $"{operation}: success {success.Name} declares no schema";
        }

        if (!errors.Any(error => error.Name == "500"))
        {
            yield return $"{operation}: declares no 500";
        }

        foreach (var error in errors.Where(error => !IsProblemDetails(error.Value)))
        {
            yield return $"{operation}: error {error.Name} is not a ProblemDetails";
        }
    }

    private static void AssertErrors(JsonElement paths, string method, string path, params string[] expected)
    {
        var responses = paths.GetProperty(path).GetProperty(method).GetProperty("responses");

        Assert.Equal(expected, responses.EnumerateObject().Select(response => response.Name).Where(IsError).Order(StringComparer.Ordinal));
    }

    private static bool IsError(string statusCode) => statusCode.StartsWith('4') || statusCode.StartsWith('5');

    private static bool HasSchema(JsonElement response) =>
        response.TryGetProperty("content", out var content)
        && content.EnumerateObject().Any(mediaType => mediaType.Value.TryGetProperty("schema", out _));

    private static bool IsProblemDetails(JsonElement response) =>
        response.TryGetProperty("content", out var content)
        && content.TryGetProperty("application/problem+json", out var mediaType)
        && mediaType.TryGetProperty("schema", out var schema)
        && schema.TryGetProperty("$ref", out var reference)
        && reference.GetString() == ProblemDetailsSchema;
}
