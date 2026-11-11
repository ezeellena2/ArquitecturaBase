using System.Text.Json;
using ArquitecturaBase.Application.Modules.Arca.Channels.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoiceStandardSourceTests
{
    private static readonly string[] IssuerData = ["customer", "customerTaxId", "concept", "issueDate", "serviceFrom", "serviceTo", "paymentDue",
        "items", "description", "quantity", "unitPrice", "vatRate", "total", "sendCopy"];

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

    // IV2-A: «El precio incluye IVA» is a toggle of «Precio unitario», unchecked; the issuer reads it as the datum pricesIncludeVat.
    [Fact]
    public void The_unit_price_says_whether_prices_include_vat_unchecked_by_default()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        using var draft = JsonDocument.Parse(standard.DraftJson);
        var fields = draft.RootElement.GetProperty("fields").EnumerateArray().ToArray();
        var unitPrice = standard.Bindings.Single(pair => pair.Value == ArcaInvoiceStandard.UnitPrice).Key;
        var toggle = Assert.Single(fields.Single(field => field.GetProperty("localId").GetString() == unitPrice).GetProperty("toggles").EnumerateArray());

        Assert.Equal((ArcaInvoiceStandard.PricesIncludeVat, false), (toggle.GetProperty("key").GetString(), toggle.GetProperty("value").GetBoolean()));
        Assert.Equal(("El precio incluye IVA", "Prices include VAT"),
            (toggle.GetProperty("label").GetProperty("es").GetString(), toggle.GetProperty("label").GetProperty("en").GetString()));
        Assert.Equal(("Marcala si cargás precios finales, como en un mostrador: la factura separa el IVA de cada alícuota.",
            "Check it if you enter final prices, as at a counter: the invoice separates the VAT of each rate."),
            (toggle.GetProperty("help").GetProperty("es").GetString(), toggle.GetProperty("help").GetProperty("en").GetString()));
        // IV1-A: the (i) of «Precio unitario» in the entry says whether the prices are without or with VAT.
        Assert.Equal(("Sin IVA: la factura le suma el de su alícuota.", "Without VAT: the invoice adds the VAT of its rate."),
            (toggle.GetProperty("whenOff").GetProperty("es").GetString(), toggle.GetProperty("whenOff").GetProperty("en").GetString()));
        Assert.Equal(("Con IVA: la factura separa el de su alícuota.", "With VAT: the invoice separates the VAT of its rate."),
            (toggle.GetProperty("whenOn").GetProperty("es").GetString(), toggle.GetProperty("whenOn").GetProperty("en").GetString()));
        Assert.Single(fields, field => field.TryGetProperty("toggles", out _));
    }

    // IV1-A: «Alícuota» starts at 21 %, the ARCA code 5 of its catalog, in each new item.
    [Fact]
    public void The_vat_rate_starts_at_twenty_one_percent()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        var vatRate = standard.Bindings.Single(pair => pair.Value == ArcaInvoiceStandard.VatRate).Key;

        Assert.Equal(new Dictionary<string, string> { [vatRate] = "5" }, standard.Defaults);
        Assert.Equal("arca.vat-rates", standard.Catalogs![vatRate]);
    }

    // The copy to the customer: a yes or no that is not required, bound so that the issuer reads it as «sendCopy».
    [Fact]
    public void The_invoice_asks_whether_to_send_the_copy_to_the_customer_with_a_bound_yes_or_no()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        using var draft = JsonDocument.Parse(standard.DraftJson);
        var localId = standard.Bindings.Single(pair => pair.Value == ArcaInvoiceStandard.SendCopy).Key;
        var question = draft.RootElement.GetProperty("fields").EnumerateArray().Single(field => field.GetProperty("localId").GetString() == localId);

        Assert.Equal(("boolean", false, "Mandar copia al cliente", "Send a copy to the customer"), (question.GetProperty("type").GetString(),
            question.GetProperty("required").GetBoolean(), question.GetProperty("label").GetProperty("es").GetString(),
            question.GetProperty("label").GetProperty("en").GetString()));
    }

    // A copy starts with «Cuando llega»: issue, tell whoever loaded it with the fiscal PDF and send the copy, with the data of the result.
    [Fact]
    public void The_invoice_brings_its_rule_issue_then_tell_with_the_fiscal_pdf_then_send_the_copy()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        var rule = Assert.Single(standard.Rules!);
        var declared = ArcaInvoiceIssuePolicy.ResultFields.Select(field => "result:" + ArcaInvoiceStandard.Key + "." + field.Key).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(("Received", 0), (rule.Event, rule.Conditions.Count));
        Assert.Equal(["IssueDocument", "NotifyAuthor", "SendCopy"], rule.Actions.Select(action => action.Kind));
        Assert.Equal((ArcaInvoiceStandard.Key, (string?)null, (string?)null), (rule.Actions[0].IssuerId, rule.Actions[1].IssuerId, rule.Actions[2].IssuerId));
        Assert.Equal((false, true, false), (rule.Actions[0].IncludePdf, rule.Actions[1].IncludePdf, rule.Actions[2].IncludePdf));
        Assert.Equal("Listo: emitimos la {result:arca.invoice.voucher} {result:arca.invoice.number}.", rule.Actions[1].Text!["es"]);
        Assert.All(rule.Actions.Skip(1), action =>
        {
            Assert.Equal(["en", "es"], action.Text!.Keys.Order(StringComparer.Ordinal));
            Assert.All(action.Text.Values.SelectMany(text => System.Text.RegularExpressions.Regex.Matches(text, "{([^}]*)}").Select(match => match.Groups[1].Value)),
                token => Assert.Contains(token, declared));
        });
    }

    [Fact]
    public void Its_catalogs_are_the_arca_tables_and_the_customers_it_brings_with_their_first_options()
    {
        var standard = Assert.Single(new ArcaInvoiceStandardSource().Standards);
        var tables = ArcaCatalogFeedPolicy.Catalogs.Select(catalog => catalog.Key).ToHashSet(StringComparer.Ordinal);
        var customers = Assert.Single(standard.OwnCatalogs!);

        Assert.All(standard.Catalogs!.Values, key => Assert.True(tables.Contains(key) || key == ArcaCustomersCatalog.Key));
        Assert.Equal(ArcaCustomersCatalog.Key, customers.Key);
        Assert.Equal(["tax_id", "legal_name", "vat_condition", "address", "email"], customers.Fields.Select(field => field.Key));
        // The option text admits 30 characters: the whole legal name has its own text field, as long as a catalog text field admits.
        var legalName = customers.Fields.Single(field => field.Key == ArcaCustomersCatalog.LegalName);
        Assert.Equal(("text", (int?)200, "Razón social", "Legal name"), (legalName.Type, legalName.MaxLength, legalName.Name["es"], legalName.Name["en"]));
        Assert.Contains(customers.Fields.Single(field => field.Key == "vat_condition").SourceCatalog!, tables);
        Assert.Equal(["final", "other"], customers.Items!.Select(item => item.Code));
    }
}
