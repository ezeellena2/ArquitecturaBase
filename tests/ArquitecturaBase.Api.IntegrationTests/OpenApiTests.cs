using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using Microsoft.AspNetCore.Hosting;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class OpenApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_served_in_development()
    {
        await using var development = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = development.CreateClient();

        using var swagger = await client.SendAsync(HttpMethod.Get, "/swagger/index.html");
        using var document = await client.SendAsync(HttpMethod.Get, "/openapi/v1.json");
        var html = await swagger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Contains("swagger-ui", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
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
}
