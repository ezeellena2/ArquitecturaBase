using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// La parte del módulo WhatsApp del documento OpenAPI (OpenApiTests): sus operaciones del perfil y del ingreso
/// llevan las etiquetas, los cuerpos y las respuestas como las del núcleo.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppOpenApiTests(ApiFactory factory)
{
    [Fact]
    public async Task The_module_operations_are_documented_with_their_tags_bodies_and_responses()
    {
        var paths = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("paths");

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

        var whatsAppLoginCode = paths.GetProperty("/account/login-code/whatsapp").GetProperty("post");
        Assert.Equal("Account", whatsAppLoginCode.GetProperty("tags")[0].GetString());
        Assert.True(whatsAppLoginCode.GetProperty("requestBody").GetProperty("content")
            .TryGetProperty("application/json", out _));
        Assert.True(whatsAppLoginCode.GetProperty("responses").TryGetProperty("202", out _));
    }
}
