using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using InfrastructureSetup = ArquitecturaBase.Infrastructure.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class OpenApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_served_in_development()
    {
        // En Development la Api aplica las migraciones y el seed al arrancar: se le da una base vacía propia y el
        // ApplicationDbContext de producción (el TestDbContext del arnés suma Widgets, que no están en las migraciones).
        await using var development = factory.WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting($"ConnectionStrings:{InfrastructureSetup.DatabaseConnectionName}", factory.NewDatabaseConnectionString("development"))
            .ConfigureTestServices(services => services.Replace(ServiceDescriptor.Scoped<ApplicationDbContext>(serviceProvider =>
                new ApplicationDbContext(serviceProvider.GetRequiredService<DbContextOptions<ApplicationDbContext>>())))));
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
