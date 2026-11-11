using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

/// <summary>
/// «¿La emito?» from a channel (EM2-A with IV3): the document, the receiver, the total with the amounts the document itemizes
/// between parentheses (the net and the VAT of an invoice A, the VAT an invoice B contains), the date with numbers and the question.
/// </summary>
public sealed class FormIssueConfirmationPolicyTests
{
    private static Dictionary<string, string> Text(string es, string en) => new() { ["es"] = es, ["en"] = en };

    [Fact]
    public void The_summary_puts_the_itemized_amounts_between_parentheses_after_the_total()
    {
        var preview = new StructuredIssuePreview(Text("Factura A", "Invoice A"), "Agro del Sur S.A.", "1210")
        {
            Date = "2026-11-09",
            Amounts = [new(Text("Importe neto gravado", "Taxed net amount"), "1000"), new(Text("IVA 21 %", "VAT 21%"), "210")],
        };

        Assert.Equal("Factura A para Agro del Sur S.A. por $ 1.210,00 (Importe neto gravado: $ 1.000,00; IVA 21 %: $ 210,00), con fecha 09/11/2026.\n¿La emito?",
            FormIssueConfirmationPolicy.Body(preview, "es"));
        Assert.Equal("Invoice A for Agro del Sur S.A., $ 1,210.00 (Taxed net amount: $ 1,000.00; VAT 21%: $ 210.00), dated 11/09/2026.\nShall I issue it?",
            FormIssueConfirmationPolicy.Body(preview, "en"));
    }

    [Fact]
    public void An_invoice_that_itemizes_nothing_says_only_its_total()
    {
        var preview = new StructuredIssuePreview(Text("Factura C", "Invoice C"), "Juana Pérez", "605500");

        Assert.Equal("Factura C para Juana Pérez por $ 605.500,00.\n¿La emito?", FormIssueConfirmationPolicy.Body(preview, "es"));
    }
}
