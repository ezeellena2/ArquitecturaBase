using System.Text.Json;
using ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormCatalogValueContractTests
{
    [Fact]
    public void Create_contract_accepts_the_catalog_type_and_canonical_item_value()
    {
        const string json = """{"name":{"es":"Zona","en":"Zone"},"description":{"es":"Entrega","en":"Delivery"},"items":[{"localId":"one","code":"NORTH","text":{"es":"Norte"},"detail":null,"isActive":true,"value":"1500"}],"valueType":"Decimal"}""";
        var request = JsonSerializer.Deserialize<CreateManualCatalogHttpRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        var actual = JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web);
        Assert.Equal("Decimal", actual.GetProperty("valueType").GetString());
        Assert.Equal("1500", actual.GetProperty("items")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Update_contract_accepts_an_item_value_without_an_editable_catalog_type()
    {
        const string json = """{"expectedRevision":1,"name":{"es":"Zona","en":"Zone"},"description":{"es":"Entrega","en":"Delivery"},"items":[{"localId":"one","code":"NORTH","text":{"es":"Norte"},"detail":null,"isActive":true,"value":"1500"}]}""";
        var request = JsonSerializer.Deserialize<UpdateManualCatalogHttpRequest>(json, JsonSerializerOptions.Web);
        Assert.NotNull(request);
        var actual = JsonSerializer.SerializeToElement(request, JsonSerializerOptions.Web);
        Assert.False(actual.TryGetProperty("valueType", out _));
        Assert.Equal("1500", actual.GetProperty("items")[0].GetProperty("value").GetString());
    }

    [Fact]
    public void Update_contract_rejects_a_client_supplied_catalog_type()
    {
        const string json = """{"expectedRevision":1,"name":{"es":"Zona","en":"Zone"},"description":{"es":"Entrega","en":"Delivery"},"items":[],"valueType":"Date"}""";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateManualCatalogHttpRequest>(json, JsonSerializerOptions.Web));
    }
}
