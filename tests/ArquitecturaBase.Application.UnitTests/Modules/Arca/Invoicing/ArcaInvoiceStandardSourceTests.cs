using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoiceStandardSourceTests
{
    private static readonly string[] IssuerData = ["customer", "customerTaxId", "concept", "issueDate", "serviceFrom", "serviceTo", "paymentDue",
        "items", "description", "quantity", "unitPrice", "vatRate", "total"];

    [Fact]
    public void The_invoice_binds_every_datum_the_issuer_reads_to_a_question_of_its_draft()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        using var draft = JsonDocument.Parse(standard.DraftJson);
        var questions = draft.RootElement.GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("localId").GetString()!);

        Assert.Equal(("arca.invoice", "arca"), (standard.Key, standard.Folder!.Key));
        Assert.Equal(IssuerData.Order(StringComparer.Ordinal), standard.Bindings.Values.Order(StringComparer.Ordinal));
        Assert.All(standard.Bindings.Keys, localId => Assert.True(questions.ContainsKey(localId)));
        Assert.All(questions.Values.Where(question => question.TryGetProperty("showWhen", out _)),
            question => Assert.True(questions.ContainsKey(question.GetProperty("showWhen").GetProperty("questionId").GetString()!)));
    }

    [Fact]
    public void Its_catalogs_are_the_arca_tables_and_the_customers_it_brings_with_their_first_options()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        var tables = ArcaCatalogFeedPolicy.Catalogs.Select(catalog => catalog.Key).ToHashSet(StringComparer.Ordinal);
        var customers = Assert.Single(standard.OwnCatalogs!);

        Assert.All(standard.Catalogs!.Values, key => Assert.True(tables.Contains(key) || key == ArcaCustomersCatalog.Key));
        Assert.Equal(ArcaCustomersCatalog.Key, customers.Key);
        Assert.Equal(["tax_id", "vat_condition", "address", "email"], customers.Fields.Select(field => field.Key));
        Assert.Contains(customers.Fields.Single(field => field.Key == "vat_condition").SourceCatalog!, tables);
        Assert.Equal(["final", "other"], customers.Items!.Select(item => item.Code));
    }
}
