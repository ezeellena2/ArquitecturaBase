using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRulePolicyTests
{
    [Fact]
    public void Messages_require_only_the_original_language_and_preserve_a_pending_translation()
    {
        var action = Action("NotifyAuthor", text: new() { ["es"] = "Recibimos {número}: {Visita}", ["en"] = "" });
        var result = FormRulePolicy.Normalize(new(0, [Rule("Received", action)]), Context());
        Assert.True(result.IsSuccess);
        Assert.Equal("Recibimos {número}: {Visita}", result.Value[0].Actions[0].Text!["es"]);
        Assert.False(result.Value[0].Actions[0].Text!.ContainsKey("en"));
    }
    [Fact]
    public void The_original_language_cannot_be_replaced_with_a_complete_translation()
    {
        var result = FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyAuthor", text: new() { ["en"] = "Received {number}" }))]), Context());
        Assert.True(result.IsFailure);
        Assert.Equal("Forms.Rule.ContentInvalid", result.Error.Code);
    }
    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void Approval_can_only_be_requested_on_receipt(string trigger) =>
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule(trigger, Action("RequestApproval", role: RoleId))]), Context()).IsFailure);
    [Fact]
    public void Approval_must_be_the_last_action_of_its_rule() =>
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule("Received", Action("RequestApproval", role: RoleId), Action("NotifyAuthor", text: Text()))]), Context()).IsFailure);
    [Fact]
    public void A_pdf_can_only_be_generated_after_approval_and_attached_after_its_generation()
    {
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule("Received", Action("GeneratePdf"))]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule("Approved", Action("NotifyAuthor", text: Text(), includePdf: true))]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule("Approved", Action("GeneratePdf"), Action("NotifyAuthor", text: Text(), includePdf: true))]), Context()).IsSuccess);
    }
    [Theory]
    [InlineData(false, 4096, true)]
    [InlineData(false, 4097, false)]
    [InlineData(true, 1024, true)]
    [InlineData(true, 1025, false)]
    public void Message_and_document_caption_limits_are_checked_without_truncating(bool includePdf, int length, bool valid)
    {
        var action = Action("NotifyAuthor", text: new() { ["es"] = new string('a', length) }, includePdf: includePdf);
        var actions = includePdf ? new[] { Action("GeneratePdf"), action } : new[] { action };
        var result = FormRulePolicy.Normalize(new(0, [Rule(includePdf ? "Approved" : "Received", actions)]), Context());
        Assert.Equal(valid, result.IsSuccess);
        if (valid) Assert.Equal(length, result.Value[0].Actions[^1].Text!["es"].Length);
    }
    [Fact]
    public void Unknown_roles_and_unknown_or_ambiguous_question_tokens_are_rejected()
    {
        Assert.Equal("Forms.Rule.RoleUnavailable", FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyRole", role: Guid.NewGuid(), text: Text()))]), Context()).Error.Code);
        Assert.Equal("Forms.Rule.PlaceholderInvalid", FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyAuthor", text: new() { ["es"] = "{No existe}" }))]), Context()).Error.Code);
        var context = Context();
        Assert.Equal("Forms.Rule.PlaceholderInvalid", FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyAuthor", text: new() { ["es"] = "{Visita}" }))]), context with { Questions = [context.Questions[0], context.Questions[0] with { FieldId = Guid.NewGuid() }] }).Error.Code);
    }
    [Fact]
    public void A_question_token_can_reference_the_question_by_its_identity_in_either_language()
    {
        var action = Action("NotifyAuthor", text: new() { ["es"] = $"Recibimos {{número}}: {{{FieldId:D}}}", ["en"] = $"Received {{number}}: {{{FieldId:D}}}" });
        var result = FormRulePolicy.Normalize(new(0, [Rule("Received", action)]), Context());
        Assert.True(result.IsSuccess);
        Assert.Equal($"Received {{number}}: {{{FieldId:D}}}", result.Value[0].Actions[0].Text!["en"]);
    }
    [Fact]
    public void A_reference_stays_unambiguous_when_two_questions_share_a_label()
    {
        var context = Context();
        var action = Action("NotifyAuthor", text: new() { ["es"] = $"Recibimos {{{FieldId:D}}}" });
        Assert.True(FormRulePolicy.Normalize(new(0, [Rule("Received", action)]), context with { Questions = [context.Questions[0], context.Questions[0] with { FieldId = Guid.NewGuid() }] }).IsSuccess);
    }
    [Fact]
    public void A_reference_to_a_question_outside_the_options_is_rejected() =>
        Assert.Equal("Forms.Rule.PlaceholderInvalid", FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyAuthor", text: new() { ["es"] = $"Recibimos {{{Guid.NewGuid():D}}}" }))]), Context()).Error.Code);
    [Fact]
    public void Conditions_use_stable_fields_and_the_existing_typed_grammar()
    {
        var rule = Rule("Received", Action("NotifyAuthor", text: Text())) with { Conditions = [new(FieldId, "contains", ["cliente"])] };
        Assert.True(FormRulePolicy.Normalize(new(0, [rule]), Context()).IsSuccess);
        Assert.True(FormRulePolicy.Normalize(new(0, [rule with { Conditions = [new(FieldId, "gte", ["3"])] }]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(0, [rule with { Conditions = [new(Guid.NewGuid(), "answered", [])] }]), Context()).IsFailure);
    }
    [Fact]
    public void A_condition_can_ask_for_a_question_of_the_block_but_not_for_the_block_itself()
    {
        var rule = Rule("Received", Action("NotifyAuthor", text: Text())) with { Conditions = [new(QuantityId, "gte", ["200"])] };
        Assert.True(FormRulePolicy.Normalize(new(0, [rule]), BlockContext()).IsSuccess);
        var result = FormRulePolicy.Normalize(new(0, [rule with { Conditions = [new(BlockId, "answered", [])] }]), BlockContext());
        Assert.Equal("Forms.Rule.ContentInvalid", result.Error.Code);
        Assert.Equal(["rules[0].conditions"], Assert.IsType<ValidationError>(result.Error).Errors.Keys);
    }
    [Fact]
    public void The_block_is_a_datum_of_the_message_but_its_questions_are_not()
    {
        static Result<IReadOnlyList<FormRule>> Normalize(string datum) =>
            FormRulePolicy.Normalize(new(0, [Rule("Received", Action("NotifyAuthor", text: new() { ["es"] = "Llegó: " + datum }))]), BlockContext());
        Assert.True(Normalize("{Artículos}").IsSuccess);
        Assert.True(Normalize($"{{{BlockId:D}}}").IsSuccess);
        Assert.Equal("Forms.Rule.PlaceholderInvalid", Normalize("{Cantidad}").Error.Code);
        var byIdentity = Normalize($"{{{QuantityId:D}}}");
        Assert.Equal("Forms.Rule.PlaceholderInvalid", byIdentity.Error.Code);
        Assert.Equal(["rules[0].actions[0].text.es"], Assert.IsType<ValidationError>(byIdentity.Error).Errors.Keys);
    }
    [Fact]
    public void Conditions_cannot_store_invalid_database_text()
    {
        var rule = Rule("Received", Action("NotifyAuthor", text: Text())) with { Conditions = [new(FieldId,"contains",[new string((char)0,1)])] };
        Assert.True(FormRulePolicy.Normalize(new(0,[rule]),Context()).IsFailure);
    }
    [Fact]
    public void Rules_can_be_disabled_with_an_empty_revision_but_invalid_or_duplicate_ids_are_rejected()
    {
        Assert.Empty(FormRulePolicy.Normalize(new(0, []), Context()).Value);
        var rule = Rule("Received", Action("NotifyAuthor", text: Text()));
        Assert.True(FormRulePolicy.Normalize(new(0, [rule, rule]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(null, [rule]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(0, [rule with { Id = Guid.Empty }]), Context()).IsFailure);
    }
    private static readonly Guid FieldId = Guid.NewGuid();
    private static readonly Guid RoleId = Guid.NewGuid();
    private static FormRuleOptionsResponse Context() => new(Guid.NewGuid(), new Dictionary<string,string> { ["es"] = "Visitas", ["en"] = "Visits" }, "es",
        [new(FieldId,"visit","shortText",new Dictionary<string,string> { ["es"] = "Visita", ["en"] = "Visit" },null,false,80,null)], [new(RoleId,"Supervisor")]);
    private static readonly Guid BlockId = Guid.NewGuid();
    private static readonly Guid QuantityId = Guid.NewGuid();
    /// <summary>The options with a block of rows, «Artículos», and its question «Cantidad» behind it.</summary>
    private static FormRuleOptionsResponse BlockContext()
    {
        var context = Context();
        return context with { Questions = [.. context.Questions,
            new(BlockId, "items", "group", new Dictionary<string,string> { ["es"] = "Artículos", ["en"] = "Items" }, null, false, null, null, Rows: new(1, 10)),
            new(QuantityId, "quantity", "decimal", new Dictionary<string,string> { ["es"] = "Cantidad", ["en"] = "Quantity" }, null, true, null, null, Repeated: true)] };
    }
    private static Dictionary<string,string?> Text() => new() { ["es"] = "Recibimos {número}" };
    private static FormRuleActionInput Action(string kind, Guid? role = null, Dictionary<string,string?>? text = null, bool includePdf = false) => new(Guid.NewGuid(),kind,role,text,includePdf);
    private static FormRuleInput Rule(string trigger, params FormRuleActionInput[] actions) => new(Guid.NewGuid(),trigger,[],actions);
}
