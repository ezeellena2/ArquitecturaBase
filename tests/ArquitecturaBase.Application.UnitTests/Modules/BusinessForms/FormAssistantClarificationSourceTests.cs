using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantClarificationSourceTests
{
    [Fact]
    public void Clarification_is_an_exact_proposal_with_no_model_supplied_question_or_labels()
    {
        var definition = Assert.Single(new FormAssistantFixture().Source.Definitions, item => item.Id == BusinessFormAssistantCapabilitySource.ClarificationId);
        Assert.Equal(1, definition.Version); Assert.Equal(AssistantCapabilityKind.Proposal, definition.Kind);
        Assert.Equal([BusinessFormsPermissions.RecordsCreate], definition.RequiredPermissions.Subject);
        using var schema = JsonDocument.Parse(definition.InputSchemaJson);
        var properties = schema.RootElement.GetProperty("properties");
        Assert.True(properties.TryGetProperty("forms", out _));
        Assert.False(properties.TryGetProperty("question", out _));
        Assert.False(properties.TryGetProperty("labels", out _));
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Two_or_three_choices_derive_revisions_and_ordinal_from_one_observed_read(int count)
    {
        var f = new FormAssistantClarificationFixture(count);
        var result = await f.Inner.Source.ExecuteAsync(f.Request(), Ct);
        Assert.True(result.IsSuccess);
        var payload = Assert.IsType<FormAssistantClarificationPayload>(result.Value.Payload);
        Assert.Equal("clarification", result.Value.State); Assert.Equal(1, payload.ReadOrdinal);
        Assert.Equal(f.Inner.Rows.Select(row => row.DefinitionId), payload.Forms.Select(form => form.DefinitionId));
        Assert.Equal(f.Inner.Rows.Select(row => row.DefinitionRevision), payload.Forms.Select(form => form.ExpectedDefinitionRevision));
        Assert.Empty(f.Inner.Catalog.Locks);
    }
    [Theory]
    [InlineData("absent")]
    [InlineData("turn")]
    [InlineData("recipient")]
    [InlineData("culture")]
    [InlineData("version")]
    [InlineData("incomplete")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("one")]
    [InlineData("four")]
    [InlineData("question")]
    public async Task Clarification_cannot_replace_observed_identity_or_closed_choice_membership(string attack)
    {
        var f = new FormAssistantClarificationFixture(attack == "four" ? 4 : 2);
        var request = f.Request();
        var read = request.ObservedReads[0];
        var snapshot = Assert.IsType<FormAssistantCandidatesPayload>(read.Payload);
        if (attack == "absent") request = request with { ObservedReads = [] };
        if (attack == "turn") request = request with { TurnId = Guid.CreateVersion7() };
        if (attack == "recipient") read = read with { Payload = snapshot with { RecipientUserId = Guid.CreateVersion7() } };
        if (attack == "culture") read = read with { Payload = snapshot with { Culture = "en" } };
        if (attack == "version") read = read with { Capability = read.Capability with { Version = 2 } };
        if (attack == "incomplete") read = read with { Payload = snapshot with { IsComplete = false } };
        if (attack is "recipient" or "culture" or "version" or "incomplete") request = request with { ObservedReads = [read] };
        if (attack == "unknown") request = request with { ArgumentsJson = FormDraftPolicy.Serialize(new { forms = new[] {
            new { definitionId = f.Inner.Rows[0].DefinitionId, versionId = f.Inner.Rows[0].VersionId },
            new { definitionId = Guid.CreateVersion7(), versionId = Guid.CreateVersion7() } } }) };
        if (attack == "duplicate") request = request with { ArgumentsJson = FormDraftPolicy.Serialize(new { forms = new[] {
            new { definitionId = f.Inner.Rows[0].DefinitionId, versionId = f.Inner.Rows[0].VersionId },
            new { definitionId = f.Inner.Rows[0].DefinitionId, versionId = f.Inner.Rows[0].VersionId } } }) };
        if (attack == "one") request = request with { ArgumentsJson = FormDraftPolicy.Serialize(new { forms = new[] { new {
            definitionId = f.Inner.Rows[0].DefinitionId, versionId = f.Inner.Rows[0].VersionId } } }) };
        if (attack == "question") request = request with { ArgumentsJson = request.ArgumentsJson.Insert(1, "\"question\":\"Inventada\",") };
        Assert.True((await f.Inner.Source.ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public async Task Older_reads_cannot_supply_a_choice_missing_from_the_latest_snapshot()
    {
        var f = new FormAssistantClarificationFixture(); var request = f.Request();
        var newer = request.ObservedReads[0] with { Ordinal = 2, Payload = f.Snapshot() with { Forms = [f.Snapshot().Forms[0]] } };
        request = request with { ObservedReads = [request.ObservedReads[0], newer] };
        Assert.True((await f.Inner.Source.ExecuteAsync(request, Ct)).IsFailure);
    }
    [Fact]
    public void Clarification_payload_has_a_closed_codec_with_exact_version_and_no_free_text()
    {
        var f = new FormAssistantClarificationFixture();
        var reference = new AssistantCapabilityReference(BusinessFormAssistantCapabilitySource.ClarificationId, 1);
        var json = FormDraftPolicy.Serialize(f.Payload());
        var result = f.Inner.Source.ParseOutput(reference, json);
        Assert.True(result.IsSuccess); Assert.IsType<FormAssistantClarificationPayload>(result.Value);
        Assert.True(f.Inner.Source.ParseOutput(reference with { Version = 2 }, json).IsFailure);
        Assert.True(f.Inner.Source.ParseOutput(reference, json.Insert(1, "\"question\":\"Inventada\",")).IsFailure);
        Assert.True(f.Inner.Source.ParseOutput(reference, json.Insert(1, "\"state\":\"clarification\",")).IsFailure);
    }
    [Theory]
    [InlineData("{\"forms\":null}")]
    [InlineData("{\"forms\":[null,null]}")]
    public async Task Invalid_json_null_collections_and_members_are_rejected(string arguments)
    {
        var f = new FormAssistantClarificationFixture(); var request = f.Request() with { ArgumentsJson = arguments };
        Assert.True((await f.Inner.Source.ExecuteAsync(request, Ct)).IsFailure);
        var json = "{\"state\":\"clarification\",\"readOrdinal\":1," + arguments[1..];
        Assert.True(f.Inner.Source.ParseOutput(new(BusinessFormAssistantCapabilitySource.ClarificationId, 1), json).IsFailure);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
