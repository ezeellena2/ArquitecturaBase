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
            Date = new DateOnly(2026, 11, 9),
            Amounts = [new(Text("Importe neto gravado", "Taxed net amount"), "1000"), new(Text("IVA 21%", "VAT 21%"), "210")],
        };

        Assert.Equal("Factura A para Agro del Sur S.A. por $ 1.210,00 (Importe neto gravado: $ 1.000,00; IVA 21%: $ 210,00), con fecha 09/11/2026.\n¿La emito?",
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

    // Decision 4: a summary that could not be made (a new customer the registry does not have) says why, in the language of the
    // person, with «Volver a probar» and «No emitir».
    [Fact]
    public void A_summary_that_failed_says_why_in_the_language_of_the_person_with_try_again_and_do_not_issue()
    {
        var failed = new StructuredIssuePreview(null, null, null, "Arca.Registry.PersonNotFound");

        Assert.Equal("Todavía no puedo emitirla: ARCA no tiene datos de esa CUIT en el padrón.", FormIssueConfirmationPolicy.Failed(failed, "es"));
        Assert.Equal("I can't issue it yet: ARCA has no registry data for that CUIT.", FormIssueConfirmationPolicy.Failed(failed, "en"));
        Assert.Equal([("retry", "Volver a probar"), ("skip", "No emitir")],
            FormIssueConfirmationPolicy.Choices(true, "es", null).Select(choice => (choice.Id, choice.Title)));
        Assert.Equal([("issue", "Issue"), ("skip", "Don't issue")], FormIssueConfirmationPolicy.Choices(false, "en", null).Select(choice => (choice.Id, choice.Title)));
    }

    // The channel keeps one message per id and person: the first of a run goes by the run, each one after «Volver a probar» by an
    // id of its own that the same decision always repeats, and its buttons name the run (within the 32 characters of an id).
    [Fact]
    public void A_message_after_trying_again_has_an_id_of_its_own_and_its_buttons_name_the_run()
    {
        var run = Guid.NewGuid();

        Assert.Equal(run, FormIssueConfirmationPolicy.MessageId(run, null));
        var later = FormIssueConfirmationPolicy.MessageId(run, 3);
        Assert.Equal(later, FormIssueConfirmationPolicy.MessageId(run, 3));
        Assert.NotEqual(run, later);
        Assert.NotEqual(later, FormIssueConfirmationPolicy.MessageId(run, 4));
        var choices = FormIssueConfirmationPolicy.Choices(true, "es", run);
        Assert.All(choices, choice => Assert.InRange(choice.Id.Length, 1, StructuredRecordActionChoice.MaxIdLength));

        Assert.True(FormIssueConfirmationPolicy.TryReadChoice(later, choices[0].Id, out var retried, out var retry));
        Assert.Equal((run, FormIssueConfirmationPolicy.Retry), (retried, retry));
        Assert.True(FormIssueConfirmationPolicy.TryReadChoice(run, "skip", out var skipped, out var skip));
        Assert.Equal((run, FormIssueConfirmationPolicy.Skip), (skipped, skip));
        Assert.False(FormIssueConfirmationPolicy.TryReadChoice(run, "other", out _, out _));
        Assert.False(FormIssueConfirmationPolicy.TryReadChoice(run, "issue.not-a-run", out _, out _));
    }
}
