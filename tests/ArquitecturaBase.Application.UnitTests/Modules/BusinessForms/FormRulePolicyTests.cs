using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
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
    public void Conditions_use_stable_fields_and_the_existing_typed_grammar()
    {
        var rule = Rule("Received", Action("NotifyAuthor", text: Text())) with { Conditions = [new(FieldId, "contains", ["cliente"])] };
        Assert.True(FormRulePolicy.Normalize(new(0, [rule]), Context()).IsSuccess);
        Assert.True(FormRulePolicy.Normalize(new(0, [rule with { Conditions = [new(FieldId, "gte", ["3"])] }]), Context()).IsFailure);
        Assert.True(FormRulePolicy.Normalize(new(0, [rule with { Conditions = [new(Guid.NewGuid(), "answered", [])] }]), Context()).IsFailure);
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
    private static Dictionary<string,string?> Text() => new() { ["es"] = "Recibimos {número}" };
    private static FormRuleActionInput Action(string kind, Guid? role = null, Dictionary<string,string?>? text = null, bool includePdf = false) => new(Guid.NewGuid(),kind,role,text,includePdf);
    private static FormRuleInput Rule(string trigger, params FormRuleActionInput[] actions) => new(Guid.NewGuid(),trigger,[],actions);
}
