using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormRecipientPreviewServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Recipient_lookup_search_is_redacted_from_diagnostic_text()
    {
        var request = new ListFormRecipientSamplesRequest { Search = "Private person lookup" };
        Assert.DoesNotContain("Private person lookup", request.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Preview_exposes_only_the_matched_person_attribute_and_the_chosen_option_presentation()
    {
        var f = new FormRecipientPreviewFixture();
        await f.Users.SetDisplayNameAsync(f.Subject, "Juan Gómez", Ct);
        var source = Guid.CreateVersion7();
        var zone = new BusinessFormCatalogField(Guid.CreateVersion7(), "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", source,
            false, false, null, null, null, "filter", 0, true);
        Assert.True(f.Catalogs.Catalog.SetFields([zone], null).IsSuccess);
        var attribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "Catalog", source, 0, true, 1);
        var privateAttribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "private", "{\"es\":\"Privado\"}", "Text", null, 1, true, 1);
        f.People.Attributes.AddRange([attribute, privateAttribute]);
        var central = Guid.CreateVersion7();
        f.People.Values[attribute.Id.ToString("D")] = central.ToString("D");
        f.People.OptionTexts[central] = new(source, "Centro");
        f.People.Values[privateAttribute.Id.ToString("D")] = "Private person value";
        var item = BusinessFormCatalogItem.Create(f.Catalogs.Catalog.Id, "ONE", "Original", null, "Private detail", null, 0);
        Assert.True(f.Catalogs.Catalog.SetManualItemFields(item, FormDraftPolicy.Serialize(new Dictionary<string, string> { [zone.Id.ToString("D")] = central.ToString("D") })).IsSuccess);
        f.Catalogs.Items.Add(item);
        var request = f.Request(new(f.Catalogs.Catalog.Id, [new(zone.Id, PersonAttributeId: attribute.Id)], new(["code"], [])));
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, request, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("Ready", result.Value.State);
        Assert.Equal(1, result.Value.OptionCount);
        Assert.Equal("Juan Gómez", result.Value.Recipient.Name);
        var personValue = Assert.Single(result.Value.Recipient.Values);
        Assert.Equal(attribute.Id, personValue.AttributeId);
        Assert.Equal("Centro", personValue.Text);
        var option = Assert.Single(result.Value.Options);
        Assert.Equal("ONE", option.Title);
        Assert.Null(option.Description);
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(result.Value, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal(1, f.People.RecipientReads);
        Assert.Empty(f.Catalogs.Locks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_unavailable_match_cannot_reveal_a_requested_private_attribute(bool sourceMismatch)
    {
        var f = new FormRecipientPreviewFixture();
        var source = Guid.CreateVersion7();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", source,
            false, false, null, null, null, "filter", 0, true);
        Assert.True(f.Catalogs.Catalog.SetFields([field], null).IsSuccess);
        var attribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "private", "{\"es\":\"Privado\"}", sourceMismatch ? "Catalog" : "Text",
            sourceMismatch ? Guid.CreateVersion7() : null, 0, true, 1);
        f.People.Attributes.Add(attribute);
        var option = Guid.CreateVersion7();
        f.People.Values[attribute.Id.ToString("D")] = sourceMismatch ? option.ToString("D") : "Private attribute text";
        f.People.OptionTexts[option] = new(attribute.SourceCatalogId ?? Guid.CreateVersion7(), "Private attribute text");
        var request = f.Request(new(f.Catalogs.Catalog.Id,
            [new(sourceMismatch ? field.Id : Guid.CreateVersion7(), PersonAttributeId: attribute.Id)]));
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, request, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("FilterUnavailable", result.Value.State);
        Assert.Empty(result.Value.Recipient.Values);
        Assert.DoesNotContain("Private attribute text", JsonSerializer.Serialize(result.Value, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.Equal(0, f.Catalogs.OfferReads);
    }

    [Fact]
    public async Task A_preview_reports_a_missing_datum_and_an_unavailable_field_as_distinct_states()
    {
        var f = new FormRecipientPreviewFixture();
        var source = Guid.CreateVersion7();
        var field = new BusinessFormCatalogField(Guid.CreateVersion7(), "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", source,
            false, false, null, null, null, "filter", 0, true);
        Assert.True(f.Catalogs.Catalog.SetFields([field], null).IsSuccess);
        var attribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "Catalog", source, 0, true, 1);
        f.People.Attributes.Add(attribute);
        var missing = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(new(f.Catalogs.Catalog.Id,
            [new(field.Id, PersonAttributeId: attribute.Id)])), Ct);
        Assert.True(missing.IsSuccess);
        Assert.Equal("RecipientDataMissing", missing.Value.State);
        Assert.Empty(missing.Value.Options);
        var unavailable = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(new(f.Catalogs.Catalog.Id, [new(Guid.CreateVersion7(), "fixed")])), Ct);
        Assert.True(unavailable.IsSuccess);
        Assert.Equal("FilterUnavailable", unavailable.Value.State);
        Assert.Empty(unavailable.Value.Options);
        Assert.Equal(0, f.Catalogs.OfferReads);
    }

    [Theory]
    [InlineData("other-catalog")]
    [InlineData("text")]
    public async Task A_person_value_that_is_not_an_option_of_the_attribute_source_is_not_shown_next_to_the_missing_datum(string stored)
    {
        var f = new FormRecipientPreviewFixture();
        var source = Guid.CreateVersion7();
        var zone = new BusinessFormCatalogField(Guid.CreateVersion7(), "zone", new Dictionary<string, string> { ["es"] = "Zona" }, "catalog", source,
            false, false, null, null, null, "filter", 0, true);
        Assert.True(f.Catalogs.Catalog.SetFields([zone], null).IsSuccess);
        var attribute = new FormPersonAttributeStoredRow(Guid.CreateVersion7(), "zone", "{\"es\":\"Zona\"}", "Catalog", source, 0, true, 1);
        f.People.Attributes.Add(attribute);
        f.People.Values[attribute.Id.ToString("D")] = stored == "text" ? "Norte" : Guid.CreateVersion7().ToString("D");
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(new(f.Catalogs.Catalog.Id,
            [new(zone.Id, PersonAttributeId: attribute.Id)])), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("RecipientDataMissing", result.Value.State);
        Assert.Empty(result.Value.Recipient.Values);
        Assert.Empty(result.Value.Options);
        Assert.Equal(0, f.Catalogs.OfferReads);
    }

    [Theory]
    [InlineData("choice", 200)]
    [InlineData("multiChoice", 20)]
    public async Task A_preview_keeps_the_excess_information_but_only_returns_the_channel_limit(string type, int limit)
    {
        var f = new FormRecipientPreviewFixture();
        f.Catalogs.Items.AddRange(Enumerable.Range(0, limit + 1).Select(index => BusinessFormCatalogItem.Create(f.Catalogs.Catalog.Id,
            "C" + index, "Opción " + index, null, null, null, index)));
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(type: type), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(limit, result.Value.Limit);
        Assert.Equal(limit + 1, result.Value.OptionCount);
        Assert.True(result.Value.ExceedsLimit);
        Assert.Equal(limit, result.Value.Options.Count);
    }

    [Theory]
    [InlineData(BusinessFormsPermissions.Read)]
    [InlineData(BusinessFormsPermissions.Manage)]
    public async Task Either_missing_form_permission_rejects_before_reading_people_or_definitions(string granted)
    {
        var f = new FormRecipientPreviewFixture();
        f.Permissions.Permissions[f.Actor] = [granted];
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
        Assert.Equal(0, f.DefinitionReads);
        Assert.Equal(0, f.People.RecipientReads);
        var samples = await f.Service.ListSamplesAsync(new(), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, samples.Error.Code);
        Assert.Equal(0, f.SampleReads);
    }

    [Fact]
    public async Task An_unknown_definition_or_non_sample_user_is_absent_before_reading_private_values()
    {
        var f = new FormRecipientPreviewFixture();
        var unknownDefinition = await f.Service.PreviewAsync(Guid.CreateVersion7(), f.Request(), Ct);
        Assert.Equal(FormDefinitionErrors.NotFound.Code, unknownDefinition.Error.Code);
        var unknownUser = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request() with { SampleUserId = Guid.CreateVersion7() }, Ct);
        Assert.Equal(FormPersonDataErrors.UserNotFound.Code, unknownUser.Error.Code);
        Assert.Equal(0, f.People.RecipientReads);
    }

    [Fact]
    public async Task Revocation_during_preview_discards_the_complete_private_response()
    {
        var f = new FormRecipientPreviewFixture();
        f.OnRead = () => f.Permissions.Permissions[f.Actor] = [];
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
    }

    [Fact]
    public async Task Revocation_during_an_absence_read_also_hides_that_absence()
    {
        var f = new FormRecipientPreviewFixture { DefinitionMissing = true };
        f.OnRead = () => f.Permissions.Permissions[f.Actor] = [];
        var result = await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, result.Error.Code);
        Assert.Equal(0, f.People.RecipientReads);
    }

    [Fact]
    public async Task Samples_share_the_form_management_permission_and_recheck_it_after_the_read()
    {
        var f = new FormRecipientPreviewFixture();
        var page = await f.Service.ListSamplesAsync(new() { Search = "Juan" }, Ct);
        Assert.True(page.IsSuccess);
        Assert.Equal(f.Subject, Assert.Single(page.Value.Items).Id);
        f.OnRead = () => f.Permissions.Permissions[f.Actor] = [];
        var denied = await f.Service.ListSamplesAsync(new(), Ct);
        Assert.Equal(FormDefinitionErrors.Forbidden.Code, denied.Error.Code);
        Assert.Empty(f.Catalogs.Locks);
    }

    [Fact]
    public async Task Validation_and_absence_are_resolved_before_private_person_values_are_read()
    {
        var f = new FormRecipientPreviewFixture();
        Assert.True((await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request() with { Culture = "xx" }, Ct)).IsFailure);
        Assert.Equal(0, f.DefinitionReads);
        f.DefinitionMissing = true;
        Assert.Equal(FormDefinitionErrors.NotFound.Code, (await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(), Ct)).Error.Code);
        Assert.Equal(0, f.People.RecipientReads);
        f.DefinitionMissing = false;
        f.SubjectMissing = true;
        Assert.Equal(FormPersonDataErrors.UserNotFound.Code, (await f.Service.PreviewAsync(f.Catalogs.DefinitionId, f.Request(), Ct)).Error.Code);
        Assert.Equal(0, f.People.RecipientReads);
    }
}
