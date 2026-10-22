using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogTypedOfferTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, "1500")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1500.5")]
    [InlineData(BusinessFormCatalogValueType.Date, "2026-10-22")]
    public async Task A_typed_offer_carries_the_canonical_value_and_keeps_the_visible_option_text(BusinessFormCatalogValueType type, string value)
    {
        var f = new FormCatalogPreparationFixture(valueType: type);
        var item = f.Catalog.AddManualItem("ONE", "Primero", "First", "Detalle", "Detail", 1, value).Value;
        f.Items.Add(item);

        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "en", f.DefinitionId, f.Version.Id, true, Ct);

        Assert.True(result.CanSend);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal(type.ToString(), offer.ValueType);
        var option = Assert.Single(offer.Options);
        Assert.Equal(item.Id, option.Id);
        Assert.Equal(value, option.Value);
        Assert.Equal("First", option.Text);
        Assert.Equal("Detail", option.Detail);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(offer, JsonSerializerOptions.Web));
        Assert.Equal(type.ToString(), json.RootElement.GetProperty("valueType").GetString());
        Assert.Equal(value, json.RootElement.GetProperty("options")[0].GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(BusinessFormCatalogValueType.Integer, null)]
    [InlineData(BusinessFormCatalogValueType.Integer, "001")]
    [InlineData(BusinessFormCatalogValueType.Integer, "1.0")]
    [InlineData(BusinessFormCatalogValueType.Decimal, "1.00")]
    [InlineData(BusinessFormCatalogValueType.Date, "2026-02-30")]
    public async Task An_invalid_or_noncanonical_stored_value_never_becomes_an_offer(BusinessFormCatalogValueType type, string? value)
    {
        var f = new FormCatalogPreparationFixture(valueType: type);
        f.Items.Add(f.Catalog.AddManualItem("ONE", "Primero", "First", null, null, 1, value).Value);

        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);

        Assert.False(result.CanSend);
        Assert.Empty(result.Catalogs);
        Assert.Equal(FormRecordErrors.CatalogOptionInvalid.Code, Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Text_and_filter_offers_do_not_add_a_type_or_value_to_their_JSON(bool filtered)
    {
        var f = new FormCatalogPreparationFixture(filtered: filtered);
        if (filtered) f.Preview = new([new(Guid.NewGuid(), "Primero", null)], 1, false);
        else f.Items.Add(f.Catalog.AddManualItem("ONE", "Primero", "First", null, null, 1).Value);
        var result = await f.Policy.PrepareAsync(Guid.NewGuid(), "es", f.DefinitionId, f.Version.Id, false, Ct);

        Assert.True(result.CanSend);
        var offer = Assert.Single(result.Catalogs);
        Assert.Null(offer.ValueType);
        Assert.Null(Assert.Single(offer.Options).Value);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(offer, JsonSerializerOptions.Web));
        Assert.False(json.RootElement.TryGetProperty("valueType", out _));
        Assert.False(json.RootElement.GetProperty("options")[0].TryGetProperty("value", out _));
    }

    [Theory]
    [InlineData("Manual")]
    [InlineData("Filters")]
    public void Legacy_catalog_offers_keep_their_existing_JSON_bytes(string kind)
    {
        var offer = new StructuredFormCatalogOffer(Guid.Parse("11111111-1111-7111-8111-111111111111"), "field", "Option",
            Guid.Parse("22222222-2222-7222-8222-222222222222"), 3, kind, "Catalog",
            [new(Guid.Parse("33333333-3333-7333-8333-333333333333"), "First", null)]);
        var previousJSON = $$"""{"FieldId":"11111111-1111-7111-8111-111111111111","Key":"field","Label":"Option","CatalogId":"22222222-2222-7222-8222-222222222222","Revision":3,"Kind":"{{kind}}","CatalogName":"Catalog","Options":[{"Id":"33333333-3333-7333-8333-333333333333","Text":"First","Detail":null}]}""";
        Assert.Equal(previousJSON, JsonSerializer.Serialize(offer));
    }

    [Fact]
    public void Legacy_reference_rows_keep_their_existing_snapshot_JSON_bytes()
    {
        var reference = new FormRecordReferenceRow(Guid.Parse("11111111-1111-7111-8111-111111111111"),
            Guid.Parse("22222222-2222-7222-8222-222222222222"), Guid.Parse("33333333-3333-7333-8333-333333333333"), 3,
            Guid.Parse("44444444-4444-7444-8444-444444444444"), "First", null, "Catalog");
        const string previousJSON = """{"RecordId":"11111111-1111-7111-8111-111111111111","FieldId":"22222222-2222-7222-8222-222222222222","CatalogId":"33333333-3333-7333-8333-333333333333","CatalogRevision":3,"ItemId":"44444444-4444-7444-8444-444444444444","Text":"First","Detail":null,"CatalogName":"Catalog","TranslationsJson":null}""";
        Assert.Equal(previousJSON, JsonSerializer.Serialize(reference));
    }
}
