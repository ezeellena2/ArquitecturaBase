using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// RE1-A in the messages: a datum of a result is written {result:<source>.<key>}. It is valid only after an action of the same rule
// that declares it, the text keeps it marked when the response arrives and the datum is filled in when the message is sent.
public sealed class FormActionResultTokenTests
{
    private static readonly FormActionResultDatum Voucher = new("arca.invoice", "voucher");
    private static readonly FormActionResultDatum ExpiresOn = new("arca.invoice", "expiresOn");

    private static FormRecordResultRow Row(params StructuredRecordResultValue[] values) => new()
    {
        RecordId = Guid.CreateVersion7(), RunId = Guid.CreateVersion7(), Source = "arca.invoice", RecordedAtUtc = new DateTime(2026, 11, 9, 20, 0, 0, DateTimeKind.Utc),
        SourceNameJson = """{"es":"Emitir comprobante en ARCA"}""", ValuesJson = FormDraftPolicy.Serialize(values),
    };

    private static StructuredRecordResultValue Value(string key, string kind, string? value) =>
        new(key, new Dictionary<string, string> { ["es"] = key }, kind, value);

    [Fact]
    public void A_datum_parses_by_its_last_dot()
    {
        Assert.True(FormActionResultTokenPolicy.TryParse("result:arca.invoice.voucher", out var datum));
        Assert.Equal(Voucher, datum);
        Assert.False(FormActionResultTokenPolicy.TryParse("result:arca", out _));
        Assert.False(FormActionResultTokenPolicy.TryParse("número", out _));
    }

    [Fact]
    public void A_datum_is_valid_only_when_an_earlier_action_of_the_rule_declares_it()
    {
        Assert.True(FormRulePolicy.ValidPlaceholders("Emitimos la {result:arca.invoice.voucher} ({número})", "es", [], [Voucher]));
        Assert.False(FormRulePolicy.ValidPlaceholders("Emitimos la {result:arca.invoice.voucher}", "es", [], []));
        Assert.False(FormRulePolicy.ValidPlaceholders("Vence {result:arca.invoice.total}", "es", [], [Voucher, ExpiresOn]));
    }

    [Fact]
    public void The_datum_is_filled_in_when_the_message_is_sent_in_the_language_of_whoever_receives_it()
    {
        var body = "Emitimos la " + FormActionResultTokenPolicy.Mark("result:arca.invoice.voucher") + ", vence el "
            + FormActionResultTokenPolicy.Mark("result:arca.invoice.expiresOn") + " por " + FormActionResultTokenPolicy.Mark("result:arca.invoice.total") + ".";
        var results = new[] { Row(Value("voucher", "text", "Factura C 0004-00000012"), Value("expiresOn", "date", "2026-11-19"), Value("total", "number", "15300.5")) };

        Assert.Equal("Emitimos la Factura C 0004-00000012, vence el 19/11/2026 por 15300,5.", FormActionResultTokenPolicy.Resolve(body, results, "es"));
        Assert.Equal("Emitimos la Factura C 0004-00000012, vence el 11/19/2026 por 15300.5.", FormActionResultTokenPolicy.Resolve(body, results, "en"));
    }

    [Theory]
    [InlineData("es", "vence el 03/11/2026.")]
    [InlineData("en", "vence el 11/03/2026.")]
    public void A_date_datum_always_has_two_digits_for_the_day_and_the_month(string culture, string expected)
    {
        var body = "vence el " + FormActionResultTokenPolicy.Mark("result:arca.invoice.expiresOn") + ".";
        var results = new[] { Row(Value("expiresOn", "date", "2026-11-03")) };

        Assert.Equal(expected, FormActionResultTokenPolicy.Resolve(body, results, culture));
    }

    [Fact]
    public void An_answer_cannot_bring_a_mark_of_its_own() =>
        Assert.Equal("result:arca.invoice.voucher", FormActionResultTokenPolicy.Unmarked(FormActionResultTokenPolicy.Mark("result:arca.invoice.voucher")));

    [Fact]
    public void A_datum_without_a_result_is_left_empty()
    {
        var body = "Comprobante: " + FormActionResultTokenPolicy.Mark("result:arca.invoice.voucher");
        Assert.Equal("Comprobante: ", FormActionResultTokenPolicy.Resolve(body, [], "es"));
        Assert.Equal("Sin datos", FormActionResultTokenPolicy.Resolve("Sin datos", [], "es"));
    }
}
