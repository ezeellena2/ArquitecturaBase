using System.Net;
using System.Net.Http.Json;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Authorization;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>Contratos HTTP comunes de las rutas de administración, fuera de las rutas específicas de WhatsApp.</summary>
[Collection(ApiTestGroup.Name)]
public sealed class ApiAdministrationHttpContractsTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("GET", "/api/settings")]
    [InlineData("PUT", "/api/settings")]
    [InlineData("GET", "/api/users")]
    [InlineData("POST", "/api/users")]
    [InlineData("GET", "/api/users/filter-counts")]
    [InlineData("GET", "/api/users/00000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/users/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/users/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/users/00000000-0000-0000-0000-000000000001/invitation")]
    [InlineData("POST", "/api/users/00000000-0000-0000-0000-000000000001/activate")]
    [InlineData("POST", "/api/users/00000000-0000-0000-0000-000000000001/deactivate")]
    [InlineData("GET", "/api/me")]
    [InlineData("PUT", "/api/me")]
    [InlineData("POST", "/api/me/email/code")]
    [InlineData("PUT", "/api/me/email")]
    [InlineData("GET", "/api/roles")]
    [InlineData("POST", "/api/roles")]
    [InlineData("PUT", "/api/roles/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/roles/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/permissions")]
    public async Task Every_route_requires_a_bearer_and_returns_a_translated_problem(string method, string route)
    {
        using var client = factory.CreateClient();
        using HttpContent? content = method is "POST" or "PUT" ? JsonContent.Create(new { }) : null;

        using var response = await client.SendAsync(new HttpMethod(method), route, content, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.Unauthorized", problem.GetProperty("code").GetString());
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
        Assert.Equal("Tenés que iniciar sesión para continuar.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Theory]
    [InlineData("PUT", "/api/settings")]
    [InlineData("PUT", "/api/roles/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/roles/00000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/users/00000000-0000-0000-0000-000000000001/activate")]
    public async Task Remaining_management_routes_reject_a_bearer_without_permission(string method, string route)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, TestEmails.Unique("contract-denied"));

        using var response = await client.SendWithTokenAsync(new HttpMethod(method), route, tokens.AccessToken, new { }, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Http.Forbidden", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task Unsupported_method_reports_405_and_the_methods_allowed_for_settings()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Patch, "/api/settings", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(["GET", "PUT"], response.Headers.Allow.Order(StringComparer.Ordinal));
        Assert.Equal("Http.MethodNotAllowed", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Settings_success_keeps_json_enum_language_and_security_headers()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync("/api/settings", tokens.AccessToken, language: "en");
        var settings = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains(settings.GetProperty("registrationMode").GetString(), new[] { "Open", "InviteOnly" });
        Assert.Contains("en", response.Content.Headers.ContentLanguage);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains(
            "default-src 'self'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_guid_user_id_does_not_match_the_route()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/api/users/not-a-guid", language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("page=wrong")]
    [InlineData("isActive=wrong")]
    public async Task Invalid_user_list_query_values_return_a_binding_problem(string query)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users?{query}", tokens.AccessToken, language: "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("role=", "role")]
    [InlineData("createdWithinDays=0", "createdWithinDays")]
    public async Task Filter_counts_validate_the_same_filter_fields_as_the_user_list(string query, string field)
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync($"/api/users/filter-counts?{query}", tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out _));
    }

    [Fact]
    public async Task Filter_counts_ignore_list_only_paging_and_sort_parameters()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.GetWithTokenAsync(
            "/api/users/filter-counts?page=wrong&pageSize=wrong&sort=passwordHash",
            tokens.AccessToken);
        var counts = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(counts.TryGetProperty("status", out _));
        Assert.True(counts.TryGetProperty("roles", out _));
        Assert.True(counts.TryGetProperty("createdWithin", out _));
    }

    [Fact]
    public async Task Settings_rejects_a_non_json_body_with_415()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/settings", UriKind.Relative))
        {
            Content = new StringContent("{\"registrationMode\":\"Open\"}", Encoding.UTF8, "text/plain"),
        };
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);

        using var response = await client.SendAsync(request, Ct);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Settings_rejects_missing_and_malformed_json_with_400()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var missing = await client.SendWithTokenAsync(HttpMethod.Put, "/api/settings", tokens.AccessToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, new Uri("/api/settings", UriKind.Relative))
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new("Bearer", tokens.AccessToken);
        using var malformed = await client.SendAsync(request, Ct);

        foreach (var response in new[] { missing, malformed })
        {
            var problem = await response.ReadJsonAsync();
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Invitation_accepted_response_has_no_body_or_location()
    {
        using var client = factory.CreateClient();
        var admin = await AdminUsersApi.SignInAsync(factory, client);
        var userId = await admin.CreateOkAsync(new { email = TestEmails.Unique("contract-invite") });

        using var response = await admin.InviteAsync(userId, new { channel = "Email" });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Ct));
        Assert.Null(response.Content.Headers.ContentType);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Activating_an_unknown_user_returns_a_not_found_problem()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);

        using var response = await client.SendWithTokenAsync(
            HttpMethod.Post,
            $"/api/users/{Guid.CreateVersion7()}/activate",
            tokens.AccessToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Users.User.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Omitting_permissions_from_role_update_clears_the_previous_permissions()
    {
        using var client = factory.CreateClient();
        var tokens = await client.LoginAsync(factory, ApiFactory.AdminEmail);
        var name = "contract-" + Guid.NewGuid().ToString("N");

        using var created = await client.SendWithTokenAsync(
            HttpMethod.Post,
            "/api/roles",
            tokens.AccessToken,
            new { name, permissions = new[] { Permissions.Users.Read } });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var roleId = (await created.ReadJsonAsync()).GetGuid();

        try
        {
            using var updated = await client.SendWithTokenAsync(
                HttpMethod.Put,
                $"/api/roles/{roleId}",
                tokens.AccessToken,
                new { name });
            using var listed = await client.GetWithTokenAsync("/api/roles", tokens.AccessToken);
            var role = (await listed.ReadJsonAsync()).EnumerateArray()
                .Single(item => item.GetProperty("id").GetGuid() == roleId);

            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
            Assert.Empty(await updated.Content.ReadAsByteArrayAsync(Ct));
            Assert.Null(updated.Content.Headers.ContentType);
            Assert.Empty(role.GetProperty("permissions").EnumerateArray());
        }
        finally
        {
            using var deleted = await client.SendWithTokenAsync(HttpMethod.Delete, $"/api/roles/{roleId}", tokens.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        }
    }

    [Theory]
    [InlineData("POST", "/api/me/email/code", "RateLimiting:LoginCodePermitLimit")]
    [InlineData("PUT", "/api/me/email", "RateLimiting:LoginVerifyPermitLimit")]
    public async Task Profile_email_routes_return_429_with_retry_after_when_the_ip_limit_is_reached(
        string method,
        string route,
        string setting)
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting(setting, "1"));
        using var client = api.CreateClient();

        using var first = await client.SendAsync(new HttpMethod(method), route, JsonContent.Create(new { }), language: "es");
        using var second = await client.SendAsync(new HttpMethod(method), route, JsonContent.Create(new { }), language: "es");
        var problem = await second.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("Http.TooManyRequests", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("retryAfter").GetInt32() > 0);
        Assert.True(second.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
