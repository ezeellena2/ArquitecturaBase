using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantCapabilitySourceTests
{
    [Fact]
    public void Discovery_and_registration_have_exact_versions_and_separate_subject_requirements()
    {
        var source = new FormAssistantFixture().Source;
        Assert.Equal(3, source.Definitions.Count);
        var read = Assert.Single(source.Definitions, definition => definition.Id == BusinessFormAssistantCapabilitySource.CandidatesId);
        Assert.Equal(1, read.Version); Assert.Equal(AssistantCapabilityKind.Read, read.Kind);
        Assert.Empty(read.RequiredPermissions.Subject);
        var proposal = Assert.Single(source.Definitions, definition => definition.Id == BusinessFormAssistantCapabilitySource.RegistrationId);
        Assert.Equal(AssistantCapabilityKind.Proposal, proposal.Kind);
        Assert.Equal([BusinessFormsPermissions.RecordsCreate], proposal.RequiredPermissions.Subject);
    }
    [Fact]
    public async Task A_complete_read_is_bound_to_the_actual_recipient_and_contains_only_authorized_metadata()
    {
        var fixture = new FormAssistantFixture();
        var result = await fixture.Source.ExecuteAsync(new(Guid.CreateVersion7(), fixture.Context,
            new(BusinessFormAssistantCapabilitySource.CandidatesId, 1), "{}", []), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantCandidatesPayload>(result.Value.Payload);
        Assert.True(payload.IsComplete); Assert.Equal(fixture.UserId, payload.RecipientUserId);
        Assert.Single(payload.Forms); Assert.Empty(result.Value.Evidences);
        Assert.Empty(fixture.Catalog.Locks);
    }
    [Fact]
    public async Task A_registration_uses_one_previously_observed_snapshot_and_derives_its_revision()
    {
        var fixture = new FormAssistantFixture(); var turnId = Guid.CreateVersion7();
        var result = await fixture.Source.ExecuteAsync(Request(fixture, turnId, [fixture.Read(turnId)]), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantRegistrationPayload>(result.Value.Payload);
        Assert.Equal(fixture.Rows[0].DefinitionRevision, payload.ExpectedDefinitionRevision);
        Assert.Equal(1, payload.ReadOrdinal);
        Assert.Equal(fixture.Catalog.FieldId, Assert.Single(payload.Values).FieldId);
        Assert.Equal(fixture.Catalog.Items[0].Id.ToString("D"), payload.Values[0].Value);
    }
    [Theory]
    [InlineData("absent")]
    [InlineData("turn")]
    [InlineData("recipient")]
    [InlineData("version")]
    [InlineData("field")]
    [InlineData("choice")]
    public async Task Current_valid_ids_do_not_replace_observation_of_the_same_recipient_snapshot(string attack)
    {
        var fixture = new FormAssistantFixture(); var turnId = Guid.CreateVersion7();
        var read = fixture.Read(attack == "turn" ? Guid.CreateVersion7() : turnId, attack == "recipient" ? Guid.CreateVersion7() : null);
        if (attack == "version") read = read with { Capability = read.Capability with { Version = 2 } };
        var request = Request(fixture, turnId, attack == "absent" ? [] : [read]);
        if (attack == "field") request = request with { ArgumentsJson = Arguments(fixture, Guid.CreateVersion7(), fixture.Catalog.Items[0].Id) };
        if (attack == "choice") request = request with { ArgumentsJson = Arguments(fixture, fixture.Catalog.FieldId, Guid.CreateVersion7()) };
        Assert.True((await fixture.Source.ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public async Task The_latest_snapshot_is_used_without_combining_choices_from_an_older_read()
    {
        var fixture = new FormAssistantFixture(); var turnId = Guid.CreateVersion7();
        var first = fixture.Read(turnId);
        var newerPayload = fixture.ReadPayload();
        newerPayload = newerPayload with { Forms = [newerPayload.Forms[0] with { Questions = [newerPayload.Forms[0].Questions[0] with { Choices = [] }] }] };
        var second = new AssistantObservedRead(turnId, 2, first.Capability, newerPayload);
        Assert.True((await fixture.Source.ExecuteAsync(Request(fixture, turnId, [first, second]), Ct)).IsFailure);
    }
    [Fact]
    public void Exact_typed_payloads_round_trip_and_unknown_or_duplicate_properties_are_rejected()
    {
        var fixture = new FormAssistantFixture(); var payload = fixture.ReadPayload();
        var json = FormDraftPolicy.Serialize(payload);
        var reference = new AssistantCapabilityReference(BusinessFormAssistantCapabilitySource.CandidatesId, 1);
        var result = fixture.Source.ParseOutput(reference, json);
        Assert.True(result.IsSuccess);
        Assert.IsType<FormAssistantCandidatesPayload>(result.Value);
        Assert.True(fixture.Source.ParseOutput(reference with { Version = 2 }, json).IsFailure);
        Assert.True(fixture.Source.ParseOutput(reference, json.Insert(1, "\"sql\":\"SELECT secret\",")).IsFailure);
        Assert.True(fixture.Source.ParseOutput(reference, json.Insert(1, "\"state\":\"complete\",")).IsFailure);
    }
    [Fact]
    public async Task An_incomplete_catalog_cannot_be_a_source_for_a_missing_or_registration_decision()
    {
        var fixture = new FormAssistantFixture(); var turnId = Guid.CreateVersion7();
        var read = fixture.Read(turnId) with { Payload = fixture.ReadPayload() with { IsComplete = false } };
        Assert.True((await fixture.Source.ExecuteAsync(Request(fixture, turnId, [read]), Ct)).IsFailure);
    }
    private static AssistantCapabilityRequest Request(FormAssistantFixture fixture, Guid turnId, IReadOnlyList<AssistantObservedRead> reads) =>
        new(turnId, fixture.Context, new(BusinessFormAssistantCapabilitySource.RegistrationId, 1), Arguments(fixture, fixture.Catalog.FieldId, fixture.Catalog.Items[0].Id), reads);
    private static string Arguments(FormAssistantFixture fixture, Guid fieldId, Guid choiceId) => FormDraftPolicy.Serialize(new
    { definitionId = fixture.Rows[0].DefinitionId, versionId = fixture.Rows[0].VersionId,
      values = new[] { new { fieldId, type = "choice", value = choiceId.ToString("D") } }, reason = "Contó una visita." });
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
