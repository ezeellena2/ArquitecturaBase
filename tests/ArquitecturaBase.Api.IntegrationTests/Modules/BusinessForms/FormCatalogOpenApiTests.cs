using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

/// <summary>
/// La parte de los catálogos del documento OpenAPI (OpenApiTests): los esquemas de sus respuestas declaran lo que el
/// lector siempre escribe.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class FormCatalogOpenApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Catalog_item_response_requires_non_nullable_field_maps()
    {
        var schema = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("components").GetProperty("schemas")
            .GetProperty("FormCatalogItemResponse");
        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToArray()
            : [];

        // El lector siempre escribe los dos mapas, aunque estén vacíos.
        foreach (var name in new[] { "fields", "fieldTexts" })
        {
            var property = schema.GetProperty("properties").GetProperty(name);
            Assert.True(required.Contains(name), $"{name} is not required: {schema.GetRawText()}");
            Assert.False(AdmitsNull(property), $"{name} admits null: {property.GetRawText()}");
        }
    }

    [Theory]
    [InlineData("FormCatalogDetailResponse")]
    [InlineData("FormPersonAttributeResponse")]
    public async Task Catalog_details_and_person_attributes_require_their_original_culture(string schemaName)
    {
        var schema = (await OpenApiTests.ReadDocumentAsync(factory)).GetProperty("components").GetProperty("schemas")
            .GetProperty(schemaName);
        var required = schema.TryGetProperty("required", out var names)
            ? names.EnumerateArray().Select(name => name.GetString()).ToArray()
            : [];

        var property = schema.GetProperty("properties").GetProperty("originalCulture");
        Assert.True(required.Contains("originalCulture"), $"originalCulture is not required: {schema.GetRawText()}");
        Assert.False(AdmitsNull(property), $"originalCulture admits null: {property.GetRawText()}");
    }

    // OpenAPI 3.1 marca null en el tipo ("type": ["null", "object"]) o en una alternativa de oneOf/anyOf.
    private static bool AdmitsNull(JsonElement schema) =>
        (schema.TryGetProperty("nullable", out var nullable) && nullable.ValueKind == JsonValueKind.True)
        || (schema.TryGetProperty("type", out var type) && (type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Any(value => value.GetString() == "null")
            : type.GetString() == "null"))
        || new[] { "oneOf", "anyOf" }.Any(keyword => schema.TryGetProperty(keyword, out var alternatives)
            && alternatives.EnumerateArray().Any(AdmitsNull));
}
