using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormIssueRulePolicyTests
{
    private const string Issuer = "test.issuer";

    [Fact]
    public void An_issuance_is_offered_only_with_its_issuer_and_keeps_it_in_the_rule()
    {
        var saved = Normalize(Context(), Rule("Received", Issue()));

        Assert.Equal(Issuer, saved.Value.Single().Actions.Single().IssuerId);
        Assert.Equal(FormRuleErrors.IssuerUnavailable.Code, Normalize(Context() with { Issuers = null }, Rule("Received", Issue())).Error.Code);
        Assert.Equal(FormRuleErrors.IssuerUnavailable.Code, Normalize(Context(), Rule("Received", Issue("other.issuer"))).Error.Code);
    }

    [Fact]
    public void An_issuance_goes_once_per_rule_on_arrival_or_approval_and_before_the_messages()
    {
        Assert.True(Normalize(Context(), Rule("Approved", Issue(), Notify())).IsSuccess);
        Assert.True(Normalize(Context(), Rule("Rejected", Issue())).IsFailure);
        Assert.True(Normalize(Context(), Rule("Received", Issue(), Issue())).IsFailure);
        Assert.True(Normalize(Context(), Rule("Received", Notify(), Issue())).IsFailure);
    }

    [Fact]
    public void Only_an_issuance_names_an_issuer()
    {
        Assert.True(Normalize(Context(), Rule("Received", Notify() with { IssuerId = Issuer })).IsFailure);
    }

    [Fact]
    public void A_message_after_the_issuance_may_use_the_data_of_its_result_and_one_before_it_may_not()
    {
        var after = Rule("Received", Issue(), Notify("Tu factura {result:test.issuer.number} salió."));
        var unknown = Rule("Received", Issue(), Notify("Tu factura {result:test.issuer.color} salió."));

        Assert.True(Normalize(Context(), after).IsSuccess);
        Assert.Equal(FormRuleErrors.PlaceholderInvalid.Code, Normalize(Context(), unknown).Error.Code);
    }

    private static Domain.Results.Result<IReadOnlyList<FormRule>> Normalize(FormRuleOptionsResponse options, FormRuleInput rule) =>
        FormRulePolicy.Normalize(new(0, [rule]), options, action => FormRulePolicy.Declared(action, options));

    private static FormRuleOptionsResponse Context() => new(Guid.NewGuid(), new Dictionary<string, string> { ["es"] = "Factura", ["en"] = "Invoice" }, "es", [], [])
    {
        Issuers = [new FormRuleIssuerResponse(Issuer, new Dictionary<string, string> { ["es"] = "Emitir", ["en"] = "Issue" },
            [new StructuredRecordResultField("number", new Dictionary<string, string> { ["es"] = "Número", ["en"] = "Number" }, StructuredRecordResultField.Text)])],
    };

    private static FormRuleActionInput Issue(string issuer = Issuer) => new(Guid.NewGuid(), "IssueDocument", IssuerId: issuer);

    private static FormRuleActionInput Notify(string text = "Recibimos {número}") =>
        new(Guid.NewGuid(), "NotifyAuthor", Text: new Dictionary<string, string?> { ["es"] = text });

    private static FormRuleInput Rule(string trigger, params FormRuleActionInput[] actions) => new(Guid.NewGuid(), trigger, [], actions);
}
