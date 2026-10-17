using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionAuthenticSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task The_owner_derives_suggestion_ids_from_the_authentic_proposal_without_the_channel_reading_its_private_payload(bool manual, bool sending)
    {
        var f = new FormSuggestionAssistantFixture();
        Guid? actorId = null;
        if (manual)
        {
            actorId = f.Runtime.Assistant.Users.AddUser("operator@example.invalid").Id;
            f.Runtime.Assistant.Permissions.Permissions[actorId.Value] = [BusinessFormsPermissions.RecordsRead];
        }
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with
        {
            SuggestionSetId = null, SuggestionId = null, ActorUserId = actorId, Proposal = f.Proposal(), ForSending = sending
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(f.Runtime.SourceId, result.Value.OriginSuggestion.OriginRecordId);
        Assert.Equal(f.Runtime.Set.Id, result.Value.OriginSuggestion.SuggestionSetId);
        Assert.Equal(f.Runtime.Suggestion.Id, result.Value.OriginSuggestion.OriginSuggestionId);
        Assert.Single(result.Value.ProposedFields);
        Assert.Single(result.Value.Prefill.Fields);
        Assert.Equal(f.Runtime.Assistant.Rows[0].DefinitionId, result.Value.Prefill.Form.DefinitionId);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("suggestion")]
    [InlineData("both")]
    public async Task Always_requires_explicit_configuration_ids_without_a_model_proposal(string missing)
    {
        var f = new FormSuggestionRuntimeFixture();
        var result = await f.Policy.PrepareAsync(f.Request with
        {
            SuggestionSetId = missing is "set" or "both" ? null : f.Set.Id,
            SuggestionId = missing is "suggestion" or "both" ? null : f.Suggestion.Id
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData("set")]
    [InlineData("suggestion")]
    public async Task Explicit_ids_that_disagree_with_the_authentic_proposal_are_not_replaced_by_its_payload(string mismatch)
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with
        {
            SuggestionSetId = mismatch == "set" ? Guid.NewGuid() : null,
            SuggestionId = mismatch == "suggestion" ? Guid.NewGuid() : null,
            Proposal = f.Proposal()
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Null_ids_do_not_make_a_proposal_authentic_without_its_observed_reads()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with
        {
            SuggestionSetId = null, SuggestionId = null, Proposal = f.Proposal() with { ObservedReads = [] }
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Derived_ids_preserve_the_configuration_revision_instead_of_adopting_a_new_set()
    {
        var f = new FormSuggestionAssistantFixture(); var proposal = f.Proposal();
        f.Runtime.Save([f.Runtime.Suggestion]);
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with
        {
            SuggestionSetId = null, SuggestionId = null, Proposal = proposal
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormSuggestionErrors.RevisionConflict, result.Error);
    }

    [Fact]
    public async Task A_derived_selection_preserves_the_operator_exclusion_and_full_original_proposal()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Runtime.Policy.PrepareAsync(f.Runtime.Request with
        {
            SuggestionSetId = null, SuggestionId = null, Proposal = f.Proposal(), IncludedFieldIds = []
        }, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Prefill.Fields);
        Assert.Single(result.Value.ProposedFields);
        Assert.Single(result.Value.OriginSuggestion.Mappings);
    }
}
