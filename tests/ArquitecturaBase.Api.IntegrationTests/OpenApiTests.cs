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
        await using var development = DevelopmentApi(factory);
        using var client = development.CreateClient();

        using var swagger = await client.SendAsync(HttpMethod.Get, "/swagger/index.html");
        using var document = await client.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        var html = await swagger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);

        var openApi = await document.ReadJsonAsync();
        var paths = openApi.GetProperty("paths");
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
        Assert.Equal("Settings", settings.GetProperty("patch").GetProperty("tags")[0].GetString());
        Assert.True(settings.GetProperty("patch").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));

        // El PUT de un solo ajuste se retiró (N2): la configuración se guarda con PATCH y su revisión.
        Assert.False(settings.TryGetProperty("put", out _));

        // Las ediciones llevan la revisión que se leyó y responden 409 si cambió (N2).
        Assert.True(me.GetProperty("put").GetProperty("responses").TryGetProperty("409", out _));
        var schemas = openApi.GetProperty("components").GetProperty("schemas");
        foreach (var (schema, property) in new[]
        {
            ("UpdateUserHttpRequest", "expectedRevision"), ("UpdateRoleHttpRequest", "expectedRevision"),
            ("UpdateProfileHttpRequest", "expectedRevision"), ("UserDetailResponse", "revision"),
            ("RoleResponse", "revision"), ("CurrentUserResponse", "revision"),
        })
        {
            Assert.True(
                schemas.GetProperty(schema).GetProperty("properties").TryGetProperty(property, out _),
                $"{schema} has no {property}.");
        }

        var loginCode = paths.GetProperty("/account/login-code").GetProperty("post");
        Assert.Equal("Account", loginCode.GetProperty("tags")[0].GetString());
        Assert.True(loginCode.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(loginCode.GetProperty("responses").TryGetProperty("202", out _));

        var verifyLoginCode = paths.GetProperty("/account/login-code/verify").GetProperty("post");
        Assert.Equal("Account", verifyLoginCode.GetProperty("tags")[0].GetString());
        Assert.True(verifyLoginCode.GetProperty("requestBody").GetProperty("content").TryGetProperty("application/json", out _));
        Assert.True(verifyLoginCode.GetProperty("responses").TryGetProperty("200", out _));

        var roles = paths.GetProperty("/api/roles");
        Assert.Equal("Roles", roles.GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.Equal("Roles", roles.GetProperty("post").GetProperty("tags")[0].GetString());
        Assert.True(roles.GetProperty("post").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        // El alta responde 201 con el id y el Location a GET /api/roles/{id}; ya no declara el 200.
        var roleCreated = roles.GetProperty("post").GetProperty("responses");
        Assert.True(roleCreated.TryGetProperty("201", out var roleCreatedResponse));
        Assert.True(HasSchema(roleCreatedResponse));
        Assert.False(roleCreated.TryGetProperty("200", out _));

        var rolesPaged = paths.GetProperty("/api/roles/paged").GetProperty("get");
        Assert.Equal("Roles", rolesPaged.GetProperty("tags")[0].GetString());
        Assert.True(rolesPaged.GetProperty("responses").TryGetProperty("200", out var rolesPage));
        Assert.True(HasSchema(rolesPage));

        var roleById = paths.GetProperty("/api/roles/{id}");
        Assert.Equal("Roles", roleById.GetProperty("get").GetProperty("tags")[0].GetString());
        Assert.True(roleById.GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        Assert.Equal("Roles", roleById.GetProperty("put").GetProperty("tags")[0].GetString());
        Assert.Equal("Roles", roleById.GetProperty("delete").GetProperty("tags")[0].GetString());
        Assert.True(roleById.GetProperty("put").GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.Equal("Roles", paths.GetProperty("/api/permissions").GetProperty("get").GetProperty("tags")[0].GetString());
    }

    [Fact]
    public async Task Every_api_operation_declares_a_success_schema_and_its_errors_as_problem_details()
    {
        var paths = (await ReadDocumentAsync(factory)).GetProperty("paths");

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
        var paths = (await ReadDocumentAsync(factory)).GetProperty("paths");

        // Anónimas y con cuerpo: sin 401. El 403 rechaza una cuenta deshabilitada antes de emitir el código.
        AssertErrors(paths, "post", "/account/login-code", "400", "403", "429", "500");
        AssertErrors(paths, "post", "/account/login-code/whatsapp", "400", "403", "429", "500");

        // Con permiso y sin entrada: sin 400 ni 404.
        AssertErrors(paths, "get", "/api/permissions", "401", "403", "500");

        // La query de un listado puede ser inválida.
        AssertErrors(paths, "get", "/api/users", "400", "401", "403", "500");
        AssertErrors(paths, "get", "/api/roles/paged", "400", "401", "403", "500");

        // El catálogo de roles no recibe query: sin 400.
        AssertErrors(paths, "get", "/api/roles", "401", "403", "500");

        // El recurso de la ruta puede no existir.
        AssertErrors(paths, "get", "/api/users/{id}", "401", "403", "404", "500");
        AssertErrors(paths, "get", "/api/roles/{id}", "401", "403", "404", "500");

        // El 404 y el 409 los declara la acción: un rol pedido que no existe, y el correo o el número de otra cuenta.
        AssertErrors(paths, "post", "/api/users", "400", "401", "403", "404", "409", "429", "500");

        // [Authorize] solo pide sesión, así que nunca responde 403. El 404 lo declara el controller: la cuenta de la
        // sesión puede haberse borrado.
        AssertErrors(paths, "get", "/api/me", "401", "404", "500");

        var unlinkPhone = paths.GetProperty("/api/me/whatsapp").GetProperty("delete");
        Assert.Equal("Users", unlinkPhone.GetProperty("tags")[0].GetString());
        // Con un correo verificado lleva el código que confirma el cambio, mandado a ese correo; sin él, puede ir vacío.
        Assert.True(unlinkPhone.TryGetProperty("requestBody", out _));
        Assert.True(unlinkPhone.GetProperty("responses").TryGetProperty("204", out _));
        AssertErrors(paths, "delete", "/api/me/whatsapp", "400", "401", "404", "409", "429", "500");

        // El código al correo que confirma vincular o desvincular el número: 409 sin correo verificado, sin número para
        // desvincular o con un número verificado, que no se reemplaza.
        var confirmationCode = paths.GetProperty("/api/me/email/confirmation-code").GetProperty("post");
        Assert.Equal("Users", confirmationCode.GetProperty("tags")[0].GetString());
        Assert.True(confirmationCode.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(confirmationCode.GetProperty("responses").TryGetProperty("202", out _));
        AssertErrors(paths, "post", "/api/me/email/confirmation-code", "400", "401", "404", "409", "429", "500");

        // Pedir el código de un correo: 409 si la cuenta ya tiene uno verificado, que desde el perfil no se cambia.
        AssertErrors(paths, "post", "/api/me/email/code", "400", "401", "404", "409", "429", "500");
    }

    [Fact]
    public async Task Conflicts_rate_limits_and_refusals_of_anonymous_actions_are_declared()
    {
        var paths = (await ReadDocumentAsync(factory)).GetProperty("paths");

        // Anónima, pero rechaza una cuenta desactivada o sin invitación: 403 sin 401. Tiene [EnableRateLimiting], así
        // que declara el 429.
        AssertErrors(paths, "post", "/account/login-code/verify", "400", "403", "429", "500");

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
    private static WebApplicationFactory<Program> DevelopmentApi(ApiFactory factory) => factory.WithWebHostBuilder(builder => builder
        .UseEnvironment("Development")
        .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("development"))
        .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
            new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())))));

    /// <summary>
    /// El documento OpenAPI de la Api en Development (<see cref="DevelopmentApi"/>). Lo leen también los tests del
    /// documento de un módulo.
    /// </summary>
    internal static async Task<JsonElement> ReadDocumentAsync(ApiFactory factory)
    {
        await using var development = DevelopmentApi(factory);
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

    [Fact]
    public async Task User_creation_answers_the_id_and_whether_it_restored_a_deleted_account()
    {
        // N4: la pantalla dice «restaurado» en vez de «creado» cuando el correo o el número eran de una cuenta borrada.
        var document = await ReadDocumentAsync(factory);
        var created = document.GetProperty("paths").GetProperty("/api/users").GetProperty("post")
            .GetProperty("responses").GetProperty("201");

        Assert.Equal("#/components/schemas/CreateUserResponse", created.GetProperty("content")
            .GetProperty("application/json").GetProperty("schema").GetProperty("$ref").GetString());
        var properties = document.GetProperty("components").GetProperty("schemas").GetProperty("CreateUserResponse")
            .GetProperty("properties");
        Assert.Equal(["id", "restored"], properties.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
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
