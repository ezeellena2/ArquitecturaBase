using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class ErrorHandlingTests(ApiFactory factory)
{
    [Fact]
    public async Task Unhandled_exception_returns_a_generic_500_with_trace_id()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/boom", language: "es");
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("General.Unexpected", problem.GetProperty("code").GetString());
        Assert.Equal("Error del servidor", problem.GetProperty("title").GetString());
        Assert.Equal(
            "Ocurrió un error inesperado. Si el problema continúa, informá el código de seguimiento.",
            problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.DoesNotContain("Sensitive", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unhandled_exception_message_follows_accept_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, "/test/boom", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(
            "An unexpected error occurred. If the problem persists, report the trace id.",
            problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Malformed_json_returns_400_request_invalid()
    {
        using var client = factory.CreateClient();
        using var content = new StringContent("{", Encoding.UTF8, "application/json");

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", content, "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal("La solicitud tiene un formato inválido.", problem.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}
