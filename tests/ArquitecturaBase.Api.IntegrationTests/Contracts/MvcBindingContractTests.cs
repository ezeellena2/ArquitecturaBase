using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

[Collection(ApiTestGroup.Name)]
public sealed class MvcBindingContractTests(ApiFactory factory)
{
    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("invalid-date")]
    public async Task Invalid_json_body_uses_the_existing_request_problem(string kind)
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/_mvc-binding/body");
        request.Content = kind switch
        {
            "malformed" => new StringContent("{", Encoding.UTF8, "application/json"),
            "invalid-date" => new StringContent("{\"atUtc\":\"2026-09-24T12:00:00\"}", Encoding.UTF8, "application/json"),
            _ => null,
        };

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        await AssertRequestProblemAsync(response, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("es", "Datos inválidos", "La solicitud tiene un formato inválido.")]
    [InlineData("en", "Invalid data", "The request has an invalid format.")]
    public async Task Invalid_query_uses_the_existing_request_problem(
        string language,
        string title,
        string detail)
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Get,
            "/api/_mvc-binding/query?page=wrong",
            language: language);

        await AssertRequestProblemAsync(response, HttpStatusCode.BadRequest);
        var problem = await response.ReadJsonAsync();
        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
        Assert.Contains(language, response.Content.Headers.ContentLanguage);
    }

    [Fact]
    public async Task Non_json_body_and_invalid_guid_keep_their_framework_status()
    {
        await using var api = ProbeApi();
        using var client = api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/_mvc-binding/body")
        {
            Content = new StringContent("{}", Encoding.UTF8, "text/plain"),
        };

        using var unsupported = await client.SendAsync(request, TestContext.Current.CancellationToken);
        using var missing = await client.GetAsync("/api/_mvc-binding/guid/not-a-guid", TestContext.Current.CancellationToken);

        await AssertRequestProblemAsync(unsupported, HttpStatusCode.UnsupportedMediaType);
        var problem = await missing.ReadJsonAsync();
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> ProbeApi()
    {
        _ = factory.Services.GetRequiredService<ApplicationPartManager>();
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(MvcBindingProbeController).Assembly)));
    }

    private static async Task AssertRequestProblemAsync(HttpResponseMessage response, HttpStatusCode status)
    {
        var problem = await response.ReadJsonAsync();
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Request.Invalid", problem.GetProperty("code").GetString());
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        var propertyNames = problem.EnumerateObject().Select(property => property.Name)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["code", "detail", "status", "title", "traceId", "type"], propertyNames);
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("type").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("traceId").GetString()));
    }
}

[ApiController]
[Route("api/_mvc-binding")]
public sealed class MvcBindingProbeController : ControllerBase
{
    [HttpPut("body")]
    public IActionResult Body([FromBody] MvcBindingProbeRequest request) => Ok(request);

    [HttpGet("query")]
    public IActionResult Query([FromQuery] int page) => Ok(page);

    [HttpGet("guid/{id:guid}")]
    public IActionResult ById(Guid id) => Ok(id);
}

public sealed record MvcBindingProbeRequest(DateTime AtUtc);
