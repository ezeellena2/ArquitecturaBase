using System.Text.Json.Nodes;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormMissingRequestCapabilityTests
{
    [Fact]
    public async Task The_same_complete_read_contains_only_generalized_topics_alongside_forms()
    {
        var f = Fixture();
        var result = await f.Source.ExecuteAsync(Read(f), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantCandidatesPayload>(result.Value.Payload);
        Assert.Equal(f.Topics, payload.Topics); Assert.Single(payload.Forms);
        Assert.Equal(2, result.Value.ItemCount);
    }
    [Fact]
    public async Task An_active_subject_without_create_permission_can_observe_topics_and_no_forms()
    {
        var f = Fixture(); f.Permissions.Permissions[f.UserId] = [];
        var result = await f.Source.ExecuteAsync(Read(f), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantCandidatesPayload>(result.Value.Payload);
        Assert.Empty(payload.Forms); Assert.Single(payload.Topics);
    }
    [Fact]
    public async Task An_unknown_subject_cannot_receive_a_complete_absence_snapshot()
    {
        var f = Fixture(); var request = Read(f);
        request = request with { Context = request.Context with { SubjectUserId = Guid.CreateVersion7() } };
        Assert.True((await f.Source.ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public async Task Topic_overflow_returns_a_partial_snapshot_that_cannot_authorize_absence()
    {
        var f = Fixture(); f.Topics.AddRange(Enumerable.Range(0, 100).Select(i => Topic() with { Name = "Tema " + i }));
        var result = await f.Source.ExecuteAsync(Read(f), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantCandidatesPayload>(result.Value.Payload);
        Assert.False(payload.IsComplete);
        Assert.Equal("partial", payload.State);
        Assert.InRange(payload.Topics.Count, 1, 100);
        var request = Proposal(f, false) with { ObservedReads = [new(Read(f).TurnId, 3,
            new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), payload)] };
        request = request with { TurnId = request.ObservedReads[0].TurnId };
        Assert.True((await new BusinessFormMissingRequestAssistantCapabilitySource(f.Candidates).ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public void The_read_codec_requires_topics_and_rejects_unknown_topic_content()
    {
        var f = Fixture(); var payload = f.ReadPayload() with { Topics = f.Topics };
        var json = FormDraftPolicy.Serialize(payload); var reference = new AssistantCapabilityReference(BusinessFormAssistantCapabilitySource.CandidatesId, 1);
        Assert.True(f.Source.ParseOutput(reference, json).IsSuccess);
        var node = JsonNode.Parse(json)!.AsObject(); node.Remove("topics");
        Assert.True(f.Source.ParseOutput(reference, node.ToJsonString()).IsFailure);
        node = JsonNode.Parse(json)!.AsObject(); node["topics"]![0]!["requesterEmail"] = "private@example.invalid";
        Assert.True(f.Source.ParseOutput(reference, node.ToJsonString()).IsFailure);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_missing_proposal_uses_the_observed_topic_or_generalized_new_metadata(bool existing)
    {
        var f = Fixture(); var request = Proposal(f, existing);
        var result = await new BusinessFormMissingRequestAssistantCapabilitySource(f.Candidates).ExecuteAsync(request, Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<MissingFormRequestProposalPayload>(result.Value.Payload);
        Assert.Equal(3, payload.ReadOrdinal);
        Assert.Equal(existing ? f.Topics[0].Id : (Guid?)null, payload.ExistingTopicId);
        Assert.Equal(existing ? null : "Registrar mantenimiento", payload.Name);
    }
    [Theory]
    [InlineData("absent")]
    [InlineData("turn")]
    [InlineData("subject")]
    [InlineData("incomplete")]
    [InlineData("newer")]
    [InlineData("topic")]
    public async Task A_model_cannot_invent_or_combine_authority_for_missing_topics(string attack)
    {
        var f = Fixture(); var request = Proposal(f, true); var read = request.ObservedReads[0];
        var payload = Assert.IsType<FormAssistantCandidatesPayload>(read.Payload);
        if (attack == "absent") request = request with { ObservedReads = [] };
        if (attack == "turn") request = request with { ObservedReads = [read with { TurnId = Guid.CreateVersion7() }] };
        if (attack == "subject") request = request with { ObservedReads = [read with { Payload = payload with { RecipientUserId = Guid.CreateVersion7() } }] };
        if (attack == "incomplete") request = request with { ObservedReads = [read with { Payload = payload with { IsComplete = false } }] };
        if (attack == "newer") request = request with { ObservedReads = [read, read with { Ordinal = 4, Payload = payload with { Topics = [] } }] };
        if (attack == "topic") request = request with { ArgumentsJson = Arguments(Guid.CreateVersion7()) };
        Assert.True((await new BusinessFormMissingRequestAssistantCapabilitySource(f.Candidates).ExecuteAsync(request, Ct)).IsFailure);
    }
    [Theory]
    [InlineData("{\"existingTopicId\":null,\"name\":\"Visita\",\"description\":\"Registrar una visita\",\"sourceMessageId\":\"chosen\"}")]
    [InlineData("{\"existingTopicId\":null,\"name\":\"Visita\"}")]
    public async Task Missing_arguments_are_closed_and_do_not_accept_client_identity(string json)
    {
        var f = Fixture(); var request = Proposal(f, false) with { ArgumentsJson = json };
        Assert.True((await new BusinessFormMissingRequestAssistantCapabilitySource(f.Candidates).ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public async Task Missing_topics_do_not_require_the_permission_to_create_a_response()
    {
        var f = Fixture(); f.Permissions.Permissions[f.UserId] = [];
        var source = new BusinessFormMissingRequestAssistantCapabilitySource(f.Candidates);
        Assert.Empty(Assert.Single(source.Definitions).RequiredPermissions.Subject);
        Assert.True((await source.ExecuteAsync(Proposal(f, false), Ct)).IsSuccess);
    }
    private static FormAssistantFixture Fixture() { var f = new FormAssistantFixture(); f.Topics.Add(Topic()); return f; }
    private static FormMissingTopicMetadata Topic() => new(Guid.CreateVersion7(), "Registrar mantenimiento", "Piden registrar tareas de mantenimiento.", "es");
    private static AssistantCapabilityRequest Read(FormAssistantFixture f) => new(Guid.CreateVersion7(), f.Context, new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), "{}", []);
    private static AssistantCapabilityRequest Proposal(FormAssistantFixture f, bool existing)
    {
        var turn = Guid.CreateVersion7(); var payload = f.ReadPayload() with { Topics = f.Topics };
        return new(turn, f.Context, new(BusinessFormMissingRequestAssistantCapabilitySource.ProposeId, 1),
            existing ? Arguments(f.Topics[0].Id) : Arguments(null), [new(turn, 3, new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), payload)]);
    }
    private static string Arguments(Guid? id) => FormDraftPolicy.Serialize(new { existingTopicId = id,
        name = id is null ? "Registrar mantenimiento" : null, description = id is null ? "Piden registrar tareas de mantenimiento." : null });
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
