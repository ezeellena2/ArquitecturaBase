using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests;

[Collection(ApiTestGroup.Name)]
public sealed class LocalizationTests(ApiFactory factory)
{
    [Fact]
    public async Task Problem_is_in_spanish_without_accept_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }));
        var problem = await response.ReadJsonAsync();

        Assert.Equal("Datos inválidos", problem.GetProperty("title").GetString());
        Assert.Equal("Revisá los campos marcados.", problem.GetProperty("detail").GetString());
        Assert.Equal("Este campo es obligatorio.", problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Theory]
    [InlineData("en", "Invalid data", "Check the highlighted fields.", "This field is required.")]
    [InlineData("en-US", "Invalid data", "Check the highlighted fields.", "This field is required.")]
    [InlineData("es-AR", "Datos inválidos", "Revisá los campos marcados.", "Este campo es obligatorio.")]
    public async Task Problem_follows_accept_language(string language, string title, string detail, string fieldMessage)
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(
            HttpMethod.Post, "/test/widgets", JsonContent.Create(new { name = "" }), language);
        var problem = await response.ReadJsonAsync();

        Assert.Equal(title, problem.GetProperty("title").GetString());
        Assert.Equal(detail, problem.GetProperty("detail").GetString());
        Assert.Equal(fieldMessage, problem.GetProperty("errors").GetProperty("name")[0].GetString());
    }

    [Fact]
    public async Task Not_found_has_translated_title_code_and_content_language()
    {
        using var client = factory.CreateClient();

        using var response = await client.SendAsync(HttpMethod.Get, $"/test/widgets/{Guid.CreateVersion7()}", language: "en");
        var problem = await response.ReadJsonAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Not found", problem.GetProperty("title").GetString());
        Assert.Equal("Widget not found.", problem.GetProperty("detail").GetString());
        Assert.Equal("Test.Widget.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal("en", Assert.Single(response.Content.Headers.ContentLanguage));
    }
}
