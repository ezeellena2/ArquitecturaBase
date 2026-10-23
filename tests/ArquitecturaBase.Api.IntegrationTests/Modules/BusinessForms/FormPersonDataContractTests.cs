using System.Text.Json;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPersonDataContractTests
{
    [Fact]
    public void Attribute_creation_accepts_a_catalog_source_and_only_requires_the_original_name()
    {
        const string json = """{"key":"zone","name":{"es":"Zona"},"type":"Catalog","sourceCatalogId":"0199f0a1-0000-7000-8000-00000000c017"}""";
        var request = JsonSerializer.Deserialize<CreateFormPersonAttributeHttpRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        Assert.Equal("zone", request.Key);
        Assert.Equal("Catalog", request.Type);
        Assert.Equal("Zona", request.Name!["es"]);
    }

    [Fact]
    public void Attribute_updates_reject_a_key_change_and_unknown_properties()
    {
        const string json = """{"expectedRevision":1,"key":"other","name":{"es":"Zona"},"type":"Text","sourceCatalogId":null,"position":0,"isActive":true}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateFormPersonAttributeHttpRequest>(json, JsonSerializerOptions.Web));
    }

    [Fact]
    public void Attribute_updates_accept_an_optional_confirmation_to_remove_the_stored_values()
    {
        const string confirmed = """{"expectedRevision":1,"name":{"es":"Zona"},"type":"Text","sourceCatalogId":null,"position":0,"isActive":true,"confirmValueRemoval":true}""";
        const string omitted = """{"expectedRevision":1,"name":{"es":"Zona"},"type":"Text","sourceCatalogId":null,"position":0,"isActive":true}""";
        Assert.True(JsonSerializer.Deserialize<UpdateFormPersonAttributeHttpRequest>(confirmed, JsonSerializerOptions.Web)!.ConfirmValueRemoval);
        Assert.False(JsonSerializer.Deserialize<UpdateFormPersonAttributeHttpRequest>(omitted, JsonSerializerOptions.Web)!.ConfirmValueRemoval);
    }

    [Fact]
    public void Person_values_accept_null_to_clear_a_value_and_redact_the_personal_content()
    {
        const string json = """{"expectedRevision":null,"values":{"0199f0a1-0000-7000-8000-00000000c017":"Dato privado","0199f0a1-0000-7000-8000-00000000c018":null}}""";
        var request = JsonSerializer.Deserialize<UpdateFormPersonDataHttpRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        Assert.Null(request.ExpectedRevision);
        Assert.Equal("Dato privado", request.Values!["0199f0a1-0000-7000-8000-00000000c017"]);
        Assert.Null(request.Values["0199f0a1-0000-7000-8000-00000000c018"]);
        Assert.DoesNotContain("Dato privado", request.ToString(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Person_values_reject_client_supplied_user_identity_and_nested_values()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateFormPersonDataHttpRequest>(
            """{"expectedRevision":null,"userId":"0199f0a1-0000-7000-8000-00000000c017","values":{}}""", JsonSerializerOptions.Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateFormPersonDataHttpRequest>(
            """{"expectedRevision":null,"values":{"0199f0a1-0000-7000-8000-00000000c017":{"value":"private"}}}""", JsonSerializerOptions.Web));
    }
}
