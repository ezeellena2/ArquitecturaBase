using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

// DOC1 with DOC7-A: the currency of a number comes from the reading of the form's standard (amount → currency), mapped to the copy's
// questions through its standard key and version and its sealed bound map; anything missing on the way gives no currency.
public sealed class FormDashboardCurrencyPolicyTests
{
    private static readonly IReadOnlyList<StructuredFormStandard> Standards = new BusinessFormStandardSource().Standards;
    private static readonly Guid Amount = Guid.NewGuid(), Currency = Guid.NewGuid();
    private static readonly Dictionary<string, Guid> Both = new() { ["amount"] = Amount, ["currency"] = Currency, ["date"] = Guid.NewGuid() };
    private static readonly IReadOnlyDictionary<string, string> Text = new Dictionary<string, string> { ["es"] = "Simple", ["en"] = "Plain" };

    [Fact]
    public void A_copy_of_the_transfer_receipt_gives_its_amount_the_question_of_its_currency()
    {
        Assert.Equal(new Dictionary<Guid, Guid> { [Amount] = Currency }, FormDashboardCurrencyPolicy.Find(Copy("forms.transfer-receipt", 1, Both), Standards));
    }

    [Fact]
    public void An_own_form_another_standard_or_version_a_standard_without_reading_or_an_unbound_datum_give_no_currency()
    {
        var plain = new StructuredFormStandard("forms.plain", 1, Text, Text, "PLN", "{}", new Dictionary<string, string>());

        Assert.Empty(FormDashboardCurrencyPolicy.Find(new FormResponseDefinitionRow(Guid.NewGuid(), "PAG", Guid.NewGuid(), []), Standards));
        Assert.Empty(FormDashboardCurrencyPolicy.Find(Copy("forms.transfer-receipt", 2, Both), Standards));
        Assert.Empty(FormDashboardCurrencyPolicy.Find(Copy("forms.other", 1, Both), Standards));
        Assert.Empty(FormDashboardCurrencyPolicy.Find(Copy("forms.plain", 1, Both), [.. Standards, plain]));
        Assert.Empty(FormDashboardCurrencyPolicy.Find(Copy("forms.transfer-receipt", 1, new() { ["amount"] = Amount }), Standards));
        Assert.Empty(FormDashboardCurrencyPolicy.Find(Copy("forms.transfer-receipt", 1, new() { ["currency"] = Currency }), Standards));
    }

    private static FormResponseDefinitionRow Copy(string key, int version, Dictionary<string, Guid> bound) =>
        new(Guid.NewGuid(), "REC", Guid.NewGuid(), [])
        {
            StandardKey = key, StandardVersion = version, StandardBindingsJson = JsonSerializer.Serialize(bound),
        };
}
