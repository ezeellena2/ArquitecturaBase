using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionPreparationAuthorizationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_manual_actor_without_record_read_is_forbidden_before_reading_either_suggestion_mode(bool assistant, bool sending)
    {
        var (f, proposal) = Arrange(assistant);
        var actor = f.Assistant.Users.AddUser("operator@example.invalid");
        f.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsCreate];
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = actor.Id, Proposal = proposal, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.Responses.Reads);
        Assert.Empty(f.Assistant.Catalog.Locks);
    }

    [Theory]
    [InlineData("empty", false)]
    [InlineData("empty", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    [InlineData("inactive", false)]
    [InlineData("inactive", true)]
    public async Task A_manual_actor_must_be_a_current_active_account_even_with_record_read(string kind, bool sending)
    {
        var f = new FormSuggestionRuntimeFixture();
        var actor = kind switch
        {
            "empty" => Guid.Empty,
            "missing" => Guid.NewGuid(),
            _ => f.Assistant.Users.AddUser("inactive@example.invalid", isActive: false).Id
        };
        f.Assistant.Permissions.Permissions[actor] = [BusinessFormsPermissions.RecordsRead];
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = actor, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.Responses.Reads);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Losing_record_read_during_preparation_withdraws_the_entire_manual_proposal(bool assistant, bool sending)
    {
        var (f, proposal) = Arrange(assistant);
        var actor = f.Assistant.Users.AddUser("operator@example.invalid");
        f.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsRead];
        f.Assistant.OnRead = () => f.Assistant.Permissions.Permissions.Remove(actor.Id);
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = actor.Id, Proposal = proposal, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.True(f.Responses.Reads > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_actor_deactivated_while_reading_the_source_receives_no_prepared_values(bool sending)
    {
        var f = new FormSuggestionRuntimeFixture();
        var actor = f.Assistant.Users.AddUser("operator@example.invalid");
        f.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsRead];
        f.Responses.BeforeReturn = () => f.Assistant.Users.SetActiveAsync(actor.Id, false, TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = actor.Id, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Manual_record_read_belongs_to_the_explicit_actor_and_does_not_require_its_create_permission(bool assistant, bool sending)
    {
        var (f, proposal) = Arrange(assistant);
        var actor = f.Assistant.Users.AddUser("operator@example.invalid", culture: "en");
        f.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsRead];
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = actor.Id, Proposal = proposal, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ProposedFields);
        Assert.Equal(f.SourceId, result.Value.OriginSuggestion.OriginRecordId);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Automatic_preparation_for_the_linked_subject_does_not_require_historical_read_permission(bool assistant, bool sending)
    {
        var (f, proposal) = Arrange(assistant);
        var result = await f.Policy.PrepareAsync(f.Request with { ActorUserId = null, Proposal = proposal, ForSending = sending }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.ProposedFields);
    }

    private static (FormSuggestionRuntimeFixture Fixture, AssistantObservedProposal? Proposal) Arrange(bool assistant)
    {
        if (!assistant) return (new(), null);
        var f = new FormSuggestionAssistantFixture();
        return (f.Runtime, f.Proposal());
    }
}
