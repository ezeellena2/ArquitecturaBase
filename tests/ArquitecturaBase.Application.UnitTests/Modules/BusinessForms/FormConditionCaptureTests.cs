using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// CO1-A in the capture: a hidden question is not required and cannot bring an answer; a shown one follows its own rules.
public sealed class FormConditionCaptureTests
{
    private static readonly CanonicalFormChoice Goods = new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Productos" });
    private static readonly CanonicalFormChoice Services = new(Guid.CreateVersion7(), new Dictionary<string, string> { ["es"] = "Servicios" });

    private static readonly CanonicalFormField Kind = new(Guid.CreateVersion7(), "kind", "choice", new Dictionary<string, string> { ["es"] = "Concepto" }, null, true, null, [Goods, Services]);
    private static readonly CanonicalFormField Paid = new(Guid.CreateVersion7(), "paid", "boolean", new Dictionary<string, string> { ["es"] = "Pagado" }, null, true, null, null);
    private static readonly CanonicalFormField From = new(Guid.CreateVersion7(), "from", "date", new Dictionary<string, string> { ["es"] = "Desde" }, null, true, null, null,
        ShowWhen: new(Kind.FieldId, [Services.ChoiceId.ToString("D")]));
    private static readonly CanonicalFormField Receipt = new(Guid.CreateVersion7(), "receipt", "shortText", new Dictionary<string, string> { ["es"] = "Recibo" }, null, false, 40, null,
        ShowWhen: new(Paid.FieldId, ["true"]));

    private static readonly FormVersionSchema Schema = new(1, [Kind, Paid, From, Receipt]);

    private static IReadOnlyList<FormRecordAnswer>? Capture(Dictionary<string, string> answers) => FormRecordAnswerPolicy.Normalize(Schema, answers, []);

    [Fact]
    public void A_shown_question_keeps_its_rules()
    {
        Assert.Null(Capture(new() { ["kind"] = Services.ChoiceId.ToString("D"), ["paid"] = "false" }));
        var answers = Capture(new() { ["kind"] = Services.ChoiceId.ToString("D"), ["paid"] = "true", ["from"] = "2026-11-01", ["receipt"] = "R-1" })!;
        Assert.Contains(answers, answer => answer.FieldId == From.FieldId);
        Assert.Contains(answers, answer => answer.FieldId == Receipt.FieldId);
    }

    [Fact]
    public void A_hidden_question_is_not_required_and_cannot_bring_an_answer()
    {
        var answers = Capture(new() { ["kind"] = Goods.ChoiceId.ToString("D"), ["paid"] = "false" })!;
        Assert.DoesNotContain(answers, answer => answer.FieldId == From.FieldId);
        Assert.Null(Capture(new() { ["kind"] = Goods.ChoiceId.ToString("D"), ["paid"] = "false", ["from"] = "2026-11-01" }));
        Assert.NotNull(Capture(new() { ["kind"] = Goods.ChoiceId.ToString("D"), ["paid"] = "false", ["receipt"] = "" }));
    }
}
