using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormRuleHistoryPolicyTests
{
    [Fact]
    public void The_first_revision_counts_all_rules_as_added()
    {
        Assert.Equal(new FormRuleChangesResponse(2, 0, 0, false), FormRuleHistoryPolicy.Compare([Rule(), Rule()], []));
    }
    [Fact]
    public void Empty_revisions_have_no_changes()
    {
        Assert.Equal(new FormRuleChangesResponse(0, 0, 0, false), FormRuleHistoryPolicy.Compare([], []));
    }
    [Fact]
    public void Inserting_a_rule_before_existing_rules_does_not_count_as_reordering()
    {
        var first = Rule(); var second = Rule();
        Assert.Equal(new FormRuleChangesResponse(1, 0, 0, false), FormRuleHistoryPolicy.Compare([Rule(), first, second], [first, second]));
    }
    [Fact]
    public void Removing_a_rule_before_existing_rules_does_not_count_as_reordering()
    {
        var first = Rule(); var second = Rule(); var removed = Rule();
        Assert.Equal(new FormRuleChangesResponse(0, 0, 1, false), FormRuleHistoryPolicy.Compare([first, second], [removed, first, second]));
    }
    [Fact]
    public void Replacing_every_identity_counts_additions_and_removals_without_a_move()
    {
        Assert.Equal(new FormRuleChangesResponse(1, 0, 2, false), FormRuleHistoryPolicy.Compare([Rule()], [Rule(), Rule()]));
    }
    [Fact]
    public void Moving_common_rules_is_reported_even_when_another_rule_was_added_or_removed()
    {
        var first = Rule(); var second = Rule();
        Assert.Equal(new FormRuleChangesResponse(1, 0, 1, true), FormRuleHistoryPolicy.Compare([second, Rule(), first], [first, Rule(), second]));
    }
    [Fact]
    public void Equivalent_deserialized_rules_do_not_count_as_updates()
    {
        var previous = new[] { Rule() };
        var current = FormDraftPolicy.Read<FormRule[]>(FormDraftPolicy.Serialize(previous));
        Assert.Equal(new FormRuleChangesResponse(0, 0, 0, false), FormRuleHistoryPolicy.Compare(current, previous));
    }
    [Fact]
    public void Translation_key_order_does_not_count_as_a_rule_edit()
    {
        var previous = Rule();
        var action = Assert.Single(previous.Actions);
        var current = previous with { Actions = [action with { Text = new Dictionary<string,string> { ["en"] = "Received", ["es"] = "Recibido" } }] };
        Assert.Equal(new FormRuleChangesResponse(0, 0, 0, false), FormRuleHistoryPolicy.Compare([current], [previous]));
    }
    [Theory]
    [InlineData("event")]
    [InlineData("condition")]
    [InlineData("text")]
    [InlineData("action")]
    [InlineData("role")]
    public void Changing_rule_content_counts_one_update(string change)
    {
        var previous = Rule();
        var action = Assert.Single(previous.Actions);
        var current = change switch
        {
            "event" => previous with { Event = "Rejected" },
            "condition" => previous with { Conditions = [new(Guid.NewGuid(), "answered", null)] },
            "text" => previous with { Actions = [action with { Text = new Dictionary<string,string> { ["es"] = "Confirmado", ["en"] = "Received" } }] },
            "action" => previous with { Actions = [action, action with { Id = Guid.NewGuid() }] },
            "role" => previous with { Actions = [action with { Kind = "NotifyRole", RoleId = Guid.NewGuid() }] },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        Assert.Equal(new FormRuleChangesResponse(0, 1, 0, false), FormRuleHistoryPolicy.Compare([current], [previous]));
    }
    [Fact]
    public void A_condition_value_edit_counts_as_an_update()
    {
        var fieldId = Guid.NewGuid();
        var previous = Rule() with { Conditions = [new(fieldId, "eq", ["first"])] };
        var current = previous with { Conditions = [new(fieldId, "eq", ["second"])] };
        Assert.Equal(new FormRuleChangesResponse(0, 1, 0, false), FormRuleHistoryPolicy.Compare([current], [previous]));
    }
    [Fact]
    public void Reordering_actions_edits_the_rule_without_reordering_rules()
    {
        var previous = Rule();
        var first = Assert.Single(previous.Actions);
        var second = first with { Id = Guid.NewGuid() };
        previous = previous with { Actions = [first, second] };
        var current = previous with { Actions = [second, first] };
        Assert.Equal(new FormRuleChangesResponse(0, 1, 0, false), FormRuleHistoryPolicy.Compare([current], [previous]));
    }
    [Fact]
    public void Including_the_generated_pdf_counts_as_a_rule_edit()
    {
        var action = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string,string> { ["es"] = "Recibido" }, false);
        var generate = new FormRuleAction(Guid.NewGuid(), "GeneratePdf", null, null, false);
        var previous = Rule() with { Event = "Approved", Actions = [generate, action] };
        var current = previous with { Actions = [generate, action with { IncludePdf = true }] };
        Assert.Equal(new FormRuleChangesResponse(0, 1, 0, false), FormRuleHistoryPolicy.Compare([current], [previous]));
    }
    private static FormRule Rule() => new(Guid.NewGuid(), "Received", [],
        [new(Guid.NewGuid(), "NotifyAuthor", null, new Dictionary<string,string> { ["es"] = "Recibido", ["en"] = "Received" }, false)]);
}
