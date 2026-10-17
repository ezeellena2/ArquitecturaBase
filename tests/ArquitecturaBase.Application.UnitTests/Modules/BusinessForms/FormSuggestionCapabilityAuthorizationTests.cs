using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormSuggestionCapabilityAuthorizationTests
{
    [Theory]
    [InlineData("whatsapp-manual", true)]
    [InlineData("whatsapp-manual", false)]
    [InlineData("whatsapp-assistant", false)]
    [InlineData("another-channel", true)]
    public async Task Historical_read_requires_the_manual_or_distinct_actors_own_permission_before_source_lookup(string channel, bool sameActor)
    {
        var f = new FormSuggestionAssistantFixture();
        var actor = sameActor ? f.Runtime.Assistant.UserId : f.Runtime.Assistant.Users.AddUser("operator@example.invalid").Id;
        var context = f.Context with { ChannelId = channel, ActorUserId = actor };
        var result = await f.Source.ExecuteAsync(Read(f, context), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.Runtime.Responses.Reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Even_a_record_reader_must_have_an_active_account(bool missing)
    {
        var f = new FormSuggestionAssistantFixture();
        var actor = missing ? Guid.NewGuid() : f.Runtime.Assistant.Users.AddUser("inactive@example.invalid", isActive: false).Id;
        f.Runtime.Assistant.Permissions.Permissions[actor] = [BusinessFormsPermissions.RecordsRead];
        var result = await f.Source.ExecuteAsync(Read(f, f.Context with { ChannelId = "whatsapp-manual", ActorUserId = actor }), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, f.Runtime.Responses.Reads);
    }

    [Fact]
    public async Task Permission_revoked_during_the_read_withdraws_the_entire_payload_before_it_can_reach_the_model()
    {
        var f = new FormSuggestionAssistantFixture();
        var actor = f.Runtime.Assistant.Users.AddUser("operator@example.invalid");
        f.Runtime.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsRead];
        f.Runtime.Assistant.OnRead = () => f.Runtime.Assistant.Permissions.Permissions.Remove(actor.Id);
        var result = await f.Source.ExecuteAsync(Read(f, f.Context with { ChannelId = "whatsapp-manual", ActorUserId = actor.Id }), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.True(f.Runtime.Responses.Reads > 0);
    }

    [Fact]
    public async Task The_manual_actor_can_read_the_subjects_suggestion_with_its_own_historical_permission()
    {
        var f = new FormSuggestionAssistantFixture();
        var actor = f.Runtime.Assistant.Users.AddUser("operator@example.invalid", culture: "en");
        f.Runtime.Assistant.Permissions.Permissions[actor.Id] = [BusinessFormsPermissions.RecordsRead];
        var result = await f.Source.ExecuteAsync(Read(f, f.Context with { ChannelId = "whatsapp-manual", ActorUserId = actor.Id }), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload);
        Assert.Single(payload.SourceValues);
        Assert.Single(payload.Suggestions);
    }

    [Fact]
    public async Task Automatic_same_subject_can_read_its_own_capture_without_global_historical_permission()
    {
        var f = new FormSuggestionAssistantFixture();
        var result = await f.Source.ExecuteAsync(Read(f, f.Context), TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Single(Assert.IsType<FormSuggestionAssistantCandidatesPayload>(result.Value.Payload).SourceValues);
    }

    [Fact]
    public async Task A_manual_proposal_cannot_reemit_observed_historical_values_after_its_actor_loses_read()
    {
        var f = new FormSuggestionAssistantFixture();
        var actor = f.Runtime.Assistant.Users.AddUser("operator@example.invalid");
        var context = f.Context with { ChannelId = "whatsapp-manual", ActorUserId = actor.Id };
        var result = await f.Source.ExecuteAsync(new(f.TurnId, context, new(BusinessFormSuggestionAssistantCapabilitySource.RegistrationId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = f.Runtime.SourceId, suggestionId = f.Runtime.Suggestion.Id,
                values = Array.Empty<FormAssistantValue>(), reason = "Pidió seguimiento." }), [f.Offers(), f.Read()]), TestContext.Current.CancellationToken);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    private static AssistantCapabilityRequest Read(FormSuggestionAssistantFixture f, AssistantChannelContext context) =>
        new(f.TurnId, context, new(BusinessFormSuggestionAssistantCapabilitySource.CandidatesId, 1),
            JsonSerializer.Serialize(new { sourceRecordId = f.Runtime.SourceId }), [f.Offers()]);
}
