using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class ValidationProblemTests(ApiFactory factory)
{
    [Fact]
    public async Task Invalid_command_returns_400_problem_with_errors_by_field()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(400, problem.GetProperty("status").GetInt32());
        Assert.Equal("Validation.Failed", problem.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
        Assert.Equal("name", Assert.Single(problem.GetProperty("errors").EnumerateObject()).Name);
    }

    [Fact]
    public async Task Too_long_name_reports_the_limit()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = new string('x', 51) }), "es");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(
            "Ingresá como máximo 50 caracteres.",
            problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Valid_command_is_saved_and_can_be_read_back()
    {
        using var client = factory.CreateClient();

        using var created = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "Tornillo" }));
        var id = await created.Content.ReadFromJsonAsync<Guid>(TestContext.Current.CancellationToken);
        using var read = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{id}");
        var widget = await read.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal("Tornillo", widget.GetProperty("name").GetString());
    }
}
