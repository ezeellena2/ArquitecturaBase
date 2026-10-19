using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormAssistantPrefillPolicyTests
{
    [Fact]
    public async Task An_authentic_observed_proposal_is_validated_against_the_current_recipient_offer()
    {
        var fixture = new FormAssistantFixture();
        var result = await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", fixture.Proposal(), ForSending: true), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.Rows[0].DefinitionRevision, result.Value.DefinitionRevision);
        Assert.Equal(fixture.Rows[0].VersionId, result.Value.Form.VersionId);
        Assert.Equal(fixture.Catalog.Items[0].Id.ToString("D"), Assert.Single(result.Value.Fields).Value);
        Assert.Equal("Contó una visita.", result.Value.Reason);
        Assert.Equal([fixture.Catalog.Catalog.Id], fixture.Catalog.Locks);
    }
    [Fact]
    public async Task An_operator_can_remove_proposed_fields_without_substituting_values_or_confirming_a_response()
    {
        var fixture = new FormAssistantFixture();
        var result = await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", fixture.Proposal(), []), Ct);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Fields);
        Assert.Single(result.Value.Catalogs);
        Assert.Empty(fixture.Catalog.Locks);
    }
    [Fact]
    public async Task Preview_returns_owner_labels_without_taking_send_locks()
    {
        var fixture = new FormAssistantFixture();
        var result = await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", fixture.Proposal()), Ct);
        Assert.True(result.IsSuccess);
        var field = Assert.Single(result.Value.Fields);
        Assert.Equal(fixture.Catalog.FieldId, field.FieldId);
        Assert.Equal("Personas", field.Label);
        Assert.Equal("Ana", field.DisplayValue);
        Assert.Empty(fixture.Catalog.Locks);
    }
    [Theory]
    [InlineData("recipient")]
    [InlineData("turn")]
    [InlineData("capability")]
    [InlineData("ordinal")]
    [InlineData("field")]
    [InlineData("choice")]
    [InlineData("type")]
    [InlineData("included")]
    public async Task A_proposal_cannot_borrow_another_identity_field_or_snapshot(string attack)
    {
        var fixture = new FormAssistantFixture(); var proposal = fixture.Proposal();
        var payload = Assert.IsType<FormAssistantRegistrationPayload>(proposal.Payload);
        proposal = attack switch
        {
            "recipient" => proposal with { ObservedReads = [proposal.ObservedReads[0] with { Payload = fixture.ReadPayload(Guid.CreateVersion7()) }] },
            "turn" => proposal with { ObservedReads = [proposal.ObservedReads[0] with { TurnId = Guid.CreateVersion7() }] },
            "capability" => proposal with { Capability = proposal.Capability with { Version = 2 } },
            "ordinal" => proposal with { Payload = payload with { ReadOrdinal = 99 } },
            "field" => proposal with { Payload = payload with { Values = [payload.Values[0] with { FieldId = Guid.CreateVersion7() }] } },
            "choice" => proposal with { Payload = payload with { Values = [payload.Values[0] with { Value = Guid.CreateVersion7().ToString("D") }] } },
            "type" => proposal with { Payload = payload with { Values = [payload.Values[0] with { Type = "shortText" }] } },
            _ => proposal,
        };
        var selected = attack == "included" ? new[] { Guid.CreateVersion7() } : null;
        Assert.True((await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", proposal, selected), Ct)).IsFailure);
    }
    [Fact]
    public async Task A_new_definition_revision_conflicts_without_using_the_current_configuration_as_observation()
    {
        var fixture = new FormAssistantFixture(); var proposal = fixture.Proposal();
        fixture.Rows[0] = fixture.Rows[0] with { DefinitionRevision = fixture.Rows[0].DefinitionRevision + 1 };
        var result = await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", proposal), Ct);
        Assert.Equal(FormDraftErrors.RevisionConflict, result.Error);
    }
    [Fact]
    public async Task A_removed_catalog_choice_cannot_be_offered_from_an_old_proposal()
    {
        var fixture = new FormAssistantFixture(); var proposal = fixture.Proposal();
        fixture.Catalog.Items.Clear();
        Assert.True((await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", proposal), Ct)).IsFailure);
    }
    [Fact]
    public async Task An_inactive_recipient_or_permission_revocation_is_rechecked_before_returning_values()
    {
        var fixture = new FormAssistantFixture(); var proposal = fixture.Proposal();
        await fixture.Users.SetActiveAsync(fixture.UserId, false, Ct);
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", proposal), Ct)).Error);
        await fixture.Users.SetActiveAsync(fixture.UserId, true, Ct);
        fixture.OnRead = () => fixture.Permissions.Permissions.Clear();
        Assert.Equal(FormRecordErrors.AuthorizationChanged, (await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", proposal), Ct)).Error);
    }
    [Fact]
    public async Task Repeated_included_field_ids_are_rejected_instead_of_broadening_the_selection()
    {
        var fixture = new FormAssistantFixture();
        Assert.True((await fixture.Prefill.ValidateAsync(new(fixture.UserId, "es", fixture.Proposal(), [fixture.Catalog.FieldId, fixture.Catalog.FieldId]), Ct)).IsFailure);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_owner_keeps_an_original_catalog_item_inside_an_English_preview_or_send(bool forSending)
    {
        var fixture=new FormAssistantFixture();
        await fixture.Users.UpdateProfileAsync(fixture.UserId,null,"en","America/Argentina/Buenos_Aires",Ct);
        fixture.Catalog.Items.Clear();
        fixture.Catalog.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Catalog.Id,"A","Ana",null,null,null,0));
        var proposal=fixture.Proposal();
        var read=proposal.ObservedReads[0];
        proposal=proposal with{ObservedReads=[read with{Payload=Assert.IsType<FormAssistantCandidatesPayload>(read.Payload) with{Culture="en"}}]};
        var result=await fixture.Prefill.ValidateAsync(new(fixture.UserId,"en",proposal,ForSending:forSending),Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("en",result.Value.Culture);
        Assert.Equal("People",Assert.Single(result.Value.Fields).Label);
        Assert.Equal("Ana",Assert.Single(result.Value.Fields).DisplayValue);
    }
    [Theory]
    [InlineData(false, "rule")]
    [InlineData(true, "rule")]
    [InlineData(false, "pdfName")]
    [InlineData(true, "pdfName")]
    [InlineData(false, "pdfFooter")]
    [InlineData(true, "pdfFooter")]
    public async Task A_missing_rule_or_pdf_translation_falls_back_the_whole_offer_before_preview_or_send(bool forSending, string missing)
    {
        var fixture = new FormAssistantFixture();
        await fixture.Users.UpdateProfileAsync(fixture.UserId, null, "en", "America/Argentina/Buenos_Aires", Ct);
        fixture.Catalog.Items.Clear();
        fixture.Catalog.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Catalog.Id, "A", "Ana", "Anne", "Detalle", "Detail", 0));
        var notify = new FormRuleAction(Guid.NewGuid(), "NotifyAuthor", null,
            missing == "rule" ? new Dictionary<string, string> { ["es"] = "Aprobamos la visita" }
                : new Dictionary<string, string> { ["es"] = "Aprobamos la visita", ["en"] = "Visit approved" }, true);
        fixture.Catalog.SetRules([new(Guid.NewGuid(), "Approved", [], [new(Guid.NewGuid(), "GeneratePdf", null, null, false), notify])]);
        fixture.Catalog.CurrentDesign = new(Guid.NewGuid(), fixture.Catalog.DefinitionId, 1, "es",
            System.Text.Json.JsonSerializer.Serialize(new FormPdfDesign(
                missing == "pdfName" ? new Dictionary<string, string> { ["es"] = "Informe" }
                    : new Dictionary<string, string> { ["es"] = "Informe", ["en"] = "Report" },
                [fixture.Catalog.FieldId], true,
                missing == "pdfFooter" ? new Dictionary<string, string> { ["es"] = "Gracias" } : null, true), System.Text.Json.JsonSerializerOptions.Web),
            new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc), null);
        var proposal = fixture.Proposal();
        var read = proposal.ObservedReads[0];
        proposal = proposal with { ObservedReads = [read with { Payload = Assert.IsType<FormAssistantCandidatesPayload>(read.Payload) with { Culture = "en" } }] };
        var result = await fixture.Prefill.ValidateAsync(new(fixture.UserId, "en", proposal, ForSending: forSending), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("es", result.Value.Culture);
        var catalog = Assert.Single(result.Value.Catalogs);
        Assert.Equal("Personas", catalog.Label);
        Assert.Equal("Personas", catalog.CatalogName);
        Assert.Equal("Ana", Assert.Single(catalog.Options).Text);
        Assert.Equal("Detalle", Assert.Single(catalog.Options).Detail);
    }
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
