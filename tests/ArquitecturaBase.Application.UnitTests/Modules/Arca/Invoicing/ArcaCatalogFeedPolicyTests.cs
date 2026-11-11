using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaCatalogFeedPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    [Fact]
    public void Arca_keeps_five_catalogs_in_its_folder_with_their_value_types()
    {
        Assert.Equal([("arca.vat-conditions", "Integer"), ("arca.document-types", "Integer"), ("arca.concepts", "Integer"), ("arca.currencies", "Text"),
            ("arca.vat-rates", "Decimal")], ArcaCatalogFeedPolicy.Catalogs.Select(catalog => (catalog.Key, catalog.ValueType)));
        Assert.All(ArcaCatalogFeedPolicy.Catalogs, catalog => Assert.Equal(("arca", "ARCA"), (catalog.Folder.Key, catalog.Folder.Name["es"])));
        Assert.Equal("Condición frente al IVA", ArcaCatalogFeedPolicy.Catalogs[0].Name["es"]);
        Assert.Equal(ArcaParameterTable.VatRates, ArcaCatalogFeedPolicy.Table("arca.vat-rates"));
        Assert.Null(ArcaCatalogFeedPolicy.Table("arca.unknown"));
    }

    [Fact]
    public void A_condition_listed_once_per_voucher_class_is_one_item_with_its_code_as_value()
    {
        var items = ArcaCatalogFeedPolicy.Items("arca.vat-conditions",
            [Row("1", "IVA Responsable Inscripto", "A"), Row("1", "IVA Responsable Inscripto", "C"), Row("5", "Consumidor Final", "B")], Today);

        Assert.Equal([("1", "IVA Responsable Inscripto", "1"), ("5", "Consumidor Final", "5")], items.Select(item => (item.Code, item.Text["es"], item.Value!)));
    }

    [Fact]
    public void A_description_longer_than_an_option_allows_is_cut_with_an_ellipsis()
    {
        var items = ArcaCatalogFeedPolicy.Items("arca.vat-conditions",
            [Row("6", "Monotributo Trabajador Independiente Promovido"), Row("99", "Sin identificar venta global diaria x")], Today);

        Assert.Equal(["Monotributo Trabajador Indepe…", "Sin identificar venta global…"], items.Select(item => item.Text["es"]));
        Assert.All(items, item => Assert.True(item.Text["es"].Length <= StructuredFeedItem.MaxTextLength));
    }

    [Fact]
    public void Only_the_rows_valid_today_come_and_a_currency_has_no_value()
    {
        var items = ArcaCatalogFeedPolicy.Items("arca.currencies",
            [Row("PES", "Pesos Argentinos"), Row("DOL", "Dólar Estadounidense"), Row("OLD", "Austral", validTo: new DateOnly(1991, 12, 31))], Today);

        Assert.Equal([("PES", (string?)null), ("DOL", null)], items.Select(item => (item.Code, item.Value)));
    }

    [Fact]
    public void A_vat_rate_carries_its_percentage_and_exempt_and_not_taxed_come_too()
    {
        var items = ArcaCatalogFeedPolicy.Items("arca.vat-rates", [Row("5", "21%"), Row("4", "10.5%"), Row("3", "0%")], Today);

        Assert.Equal([("5", "21%", "21"), ("4", "10.5%", "10.5"), ("3", "0%", "0"), ("exempt", "Exento", "0"), ("not-taxed", "No gravado", "0")],
            items.Select(item => (item.Code, item.Text["es"], item.Value!)));
        Assert.Equal("Not taxed", items[^1].Text["en"]);
    }

    private static ArcaParameter Row(string id, string description, string? voucherClass = null, DateOnly? validTo = null) =>
        new(id, description, new DateOnly(2010, 1, 1), validTo, voucherClass);
}
