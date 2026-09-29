using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// Guarda las combinaciones verbo/ruta antes y durante la migración a controllers. Un módulo opcional suma las suyas con
/// el gancho <c>AddModuleRoutes</c>, en la parte de esta clase que vive en su carpeta <c>Modules/&lt;M&gt;</c>.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class ExplicitRouteInventoryTests(ApiFactory factory)
{
    private static readonly string[] CoreRoutes =
    [
        "GET /account/login-methods",
        "POST /account/login-code",
        "POST /account/login-code/verify",
        "POST /account/login-link/preview",
        "POST /account/login-link/redeem",
        "GET /account/external/google",
        "GET /account/external/callback",
        "GET /connect/authorize",
        "POST /connect/authorize",
        "POST /connect/token",
        "GET /connect/logout",
        "POST /connect/logout",
        "GET /connect/userinfo",
        "POST /connect/userinfo",
        "GET /api/settings",
        "PUT /api/settings",
        "GET /api/users",
        "POST /api/users",
        "GET /api/users/filter-counts",
        "GET /api/users/{id:guid}",
        "PUT /api/users/{id:guid}",
        "DELETE /api/users/{id:guid}",
        "POST /api/users/{id:guid}/invitation",
        "POST /api/users/{id:guid}/activate",
        "POST /api/users/{id:guid}/deactivate",
        "DELETE /api/users/{id:guid}/whatsapp",
        "GET /api/me",
        "PUT /api/me",
        "POST /api/me/email/code",
        "PUT /api/me/email",
        "GET /api/roles",
        "GET /api/roles/paged",
        "GET /api/roles/{id:guid}",
        "POST /api/roles",
        "PUT /api/roles/{id:guid}",
        "DELETE /api/roles/{id:guid}",
        "GET /api/permissions",
    ];

    [Fact]
    public void The_explicit_business_routes_have_no_missing_or_duplicate_method_path_pairs()
    {
        var actual = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var path = "/" + endpoint.RoutePattern.RawText?.Trim('/');
                var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
                return methods.Select(method => $"{method.ToUpperInvariant()} {path.ToLowerInvariant()}");
            })
            .Where(route => route.Contains(" /account/", StringComparison.Ordinal)
                || route.Contains(" /connect/", StringComparison.Ordinal)
                || route.Contains(" /api/", StringComparison.Ordinal)
                || route.Contains(" /webhooks/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        List<string> expected = [.. CoreRoutes];
        AddModuleRoutes(expected);

        Assert.Equal(37, CoreRoutes.Length);
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    /// <summary>Las combinaciones verbo/ruta de cada módulo, con el mismo formato que <see cref="CoreRoutes"/>.</summary>
    static partial void AddModuleRoutes(List<string> routes);
}
