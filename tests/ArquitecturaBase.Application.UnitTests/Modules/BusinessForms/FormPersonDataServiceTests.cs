using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormPersonDataServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(BusinessFormsPermissions.PersonDataManage)]
    [InlineData(Permissions.Users.Manage)]
    [InlineData(BusinessFormsPermissions.Manage)]
    public async Task Each_authorized_attribute_reader_can_read_definitions_without_receiving_person_values(string permission)
    {
        var fixture = new FormPersonDataFixture();
        fixture.Grant(permission);
        fixture.AddAttribute();

        var result = await fixture.Service.ListAttributesAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(0, fixture.RecipientReads);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Theory]
    [InlineData(BusinessFormsPermissions.PersonDataManage)]
    [InlineData(Permissions.Users.Read)]
    [InlineData(Permissions.Users.Manage)]
    public async Task The_people_table_requires_both_definition_management_and_user_read_access(string permission)
    {
        var fixture = new FormPersonDataFixture();
        fixture.Grant(permission);

        var result = await fixture.Service.ListPeopleAsync(new(), Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(0, fixture.PeopleReads);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task The_people_table_reports_how_many_matching_people_lack_each_active_attribute()
    {
        var fixture = new FormPersonDataFixture();
        var zone = fixture.AddAttribute("zone");
        var retired = fixture.AddAttribute("retired_zone", active: false);
        var branch = fixture.AddAttribute("branch", catalog: false);
        fixture.MissingCounts[zone.Id] = 2;
        fixture.MissingCounts[retired.Id] = 9;

        var result = await fixture.Service.ListPeopleAsync(new() { Search = "Sosa" }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new FormPersonMissingCountResponse[] { new(zone.Id, 2), new(branch.Id, 0) }, result.Value.Missing);
        var read = Assert.Single(fixture.MissingReads);
        Assert.Equal([zone.Id, branch.Id], read.AttributeIds);
        Assert.Equal("Sosa", read.Search);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task Revoking_user_read_access_while_counting_missing_values_discards_the_people_table()
    {
        var fixture = new FormPersonDataFixture();
        fixture.AddAttribute();
        fixture.OnMissingRead = () => fixture.Grant(BusinessFormsPermissions.PersonDataManage);

        var result = await fixture.Service.ListPeopleAsync(new(), Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Creating_an_attribute_checks_global_capacity_and_key_uniqueness_before_adding_it()
    {
        var fixture = new FormPersonDataFixture();
        fixture.AddAttribute("zone");
        var duplicate = await fixture.Service.CreateAttributeAsync(new("zone", Names(), "Text", null), Ct);
        Assert.Equal(FormPersonDataErrors.AttributeKeyDuplicate.Code, duplicate.Error.Code);
        Assert.Contains("key", Assert.IsType<ValidationError>(duplicate.Error).Errors.Keys);
        Assert.Single(fixture.Attributes);
        for (var index = 1; index < 20; index++) fixture.AddAttribute("field_" + index, active: false);

        var full = await fixture.Service.CreateAttributeAsync(new("other", Names(), "Text", null), Ct);

        Assert.Equal(FormPersonDataErrors.AttributeTooMany, full.Error);
        Assert.Equal(20, fixture.Attributes.Count);
        Assert.Equal(0, fixture.Work.Commits);
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("en-US", "en")]
    public async Task A_created_attribute_takes_its_original_language_from_the_menu(string uiCulture, string original)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
        try
        {
            var fixture = new FormPersonDataFixture();

            var created = await fixture.Service.CreateAttributeAsync(new("zone", Names(), "Text", null), Ct);

            Assert.True(created.IsSuccess);
            Assert.Equal(original, Assert.Single(fixture.Attributes).OriginalCulture);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("es-AR", "es")]
    [InlineData("en-US", "en")]
    public async Task Attribute_responses_report_their_original_culture(string uiCulture, string original)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var fixture = new FormPersonDataFixture();
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            var created = await fixture.Service.CreateAttributeAsync(new("zone", Names(), "Text", null), Ct);

            // Editing and reading with the menu in the other language keep the original the attribute was created in.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(original == "en" ? "es-AR" : "en-US");
            var updated = await fixture.Service.UpdateAttributeAsync(created.Value.Id, new(1, Names(), "Text", null, 0, true), Ct);
            var listed = await fixture.Service.ListAttributesAsync(Ct);

            Assert.Equal(original, created.Value.OriginalCulture);
            Assert.Equal(original, updated.Value.OriginalCulture);
            Assert.Equal(original, Assert.Single(listed.Value.Items).OriginalCulture);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task An_attribute_created_with_the_menu_in_English_requires_only_its_English_name()
    {
        using var menu = new CultureScope("en-US");
        var fixture = new FormPersonDataFixture();

        var missing = await fixture.Service.CreateAttributeAsync(new("zone", new Dictionary<string, string?> { ["es"] = "Zona", ["en"] = " " }, "Text", null), Ct);
        var created = await fixture.Service.CreateAttributeAsync(new("zone", new Dictionary<string, string?> { ["en"] = "Zone" }, "Text", null), Ct);

        var error = Assert.IsType<ValidationError>(missing.Error);
        Assert.Equal(FormPersonDataErrors.AttributeInvalid.Code, error.Code);
        Assert.Equal("name.en", Assert.Single(error.Errors).Key);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Code : null);
        Assert.Equal("en", created.Value.OriginalCulture);
        Assert.Equal(new Dictionary<string, string> { ["en"] = "Zone" }, created.Value.Name);
        Assert.Equal(1, fixture.Work.Commits);
    }

    [Fact]
    public async Task Updating_an_attribute_requires_its_stored_original_name()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute(catalog: false);
        using var menu = new CultureScope("en-US");

        var missing = await fixture.Service.UpdateAttributeAsync(attribute.Id,
            new(1, new Dictionary<string, string?> { ["en"] = "Zone" }, "Text", null, 0, true), Ct);
        var renamed = await fixture.Service.UpdateAttributeAsync(attribute.Id,
            new(1, new Dictionary<string, string?> { ["es"] = "Zona de atención", ["en"] = null }, "Text", null, 0, true), Ct);

        var error = Assert.IsType<ValidationError>(missing.Error);
        Assert.Equal(FormPersonDataErrors.AttributeInvalid.Code, error.Code);
        Assert.Equal("name.es", Assert.Single(error.Errors).Key);
        Assert.True(renamed.IsSuccess, renamed.IsFailure ? renamed.Error.Code : null);
        Assert.Equal("es", renamed.Value.OriginalCulture);
        Assert.Equal(new Dictionary<string, string> { ["es"] = "Zona de atención" }, renamed.Value.Name);
        Assert.Equal(1, fixture.Work.Commits);
    }

    [Fact]
    public async Task Source_and_form_names_fall_back_to_their_original_language()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-AR");
        try
        {
            // The source catalog and the form that uses the attribute were written in English and have no Spanish translation.
            var fixture = new FormPersonDataFixture { SourceNameJson = "{\"en\":\"Branches\"}", UsageNameJson = "{\"en\":\"Visit\"}" };
            var attribute = fixture.AddAttribute();
            fixture.UsedAttributes.Add(attribute.Id);

            var listed = await fixture.Service.ListAttributesAsync(Ct);

            var item = Assert.Single(listed.Value.Items);
            Assert.Equal("Branches", item.SourceCatalogName);
            Assert.Equal("Visit", Assert.Single(item.UsedBy).Name);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task A_used_attribute_can_be_renamed_but_cannot_change_its_type_or_be_deactivated()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute();
        fixture.UsedAttributes.Add(attribute.Id);
        var request = new UpdateFormPersonAttributeRequest(1, Names(), "Text", null, 0, true);

        Assert.Equal(FormPersonDataErrors.AttributeInUse, (await fixture.Service.UpdateAttributeAsync(attribute.Id, request, Ct)).Error);
        Assert.Equal(FormPersonDataErrors.AttributeInUse, (await fixture.Service.UpdateAttributeAsync(attribute.Id,
            request with { Type = "Catalog", SourceCatalogId = fixture.Catalog.Id, IsActive = false }, Ct)).Error);
        var renamed = await fixture.Service.UpdateAttributeAsync(attribute.Id,
            request with { Name = new Dictionary<string, string?> { ["es"] = "Zona de atención" }, Type = "Catalog", SourceCatalogId = fixture.Catalog.Id }, Ct);

        Assert.True(renamed.IsSuccess);
        Assert.Equal(attribute.Id, renamed.Value.Id);
        Assert.Equal(2, renamed.Value.Revision);
        Assert.Equal(1, fixture.Work.Commits);
    }

    [Theory]
    [InlineData("type")]
    [InlineData("source")]
    public async Task Changing_the_type_or_source_of_an_attribute_with_stored_values_requires_confirmation_and_clears_only_that_value(string change)
    {
        var fixture = new FormPersonDataFixture();
        var zone = fixture.AddAttribute("zone");
        var note = fixture.AddAttribute("note", catalog: false);
        var storedJson = JsonSerializer.Serialize(new Dictionary<string, string>
        { [zone.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D"), [note.Id.ToString("D")] = "Nota" });
        fixture.Data.Add(BusinessFormPersonData.Create(fixture.Subject, storedJson));
        var request = change == "type"
            ? new UpdateFormPersonAttributeRequest(1, Names(), "Text", null, 0, true)
            : new UpdateFormPersonAttributeRequest(1, Names(), "Catalog", fixture.OtherCatalog.Id, 0, true);

        var unconfirmed = await fixture.Service.UpdateAttributeAsync(zone.Id, request, Ct);

        Assert.Equal("Forms.PersonAttribute.ValueRemovalNotConfirmed", unconfirmed.Error.Code);
        Assert.Contains(change == "type" ? "type" : "sourceCatalogId", Assert.IsType<ValidationError>(unconfirmed.Error).Errors.Keys);
        Assert.Equal(1, unconfirmed.Error.Metadata!["count"]);
        Assert.Equal(0, fixture.Work.Commits);
        Assert.Equal(1, zone.Revision);
        Assert.Equal(BusinessFormPersonAttributeType.Catalog, zone.Type);
        Assert.Equal(fixture.Catalog.Id, zone.SourceCatalogId);
        Assert.Equal(storedJson, Assert.Single(fixture.Data).ValuesJson);

        var confirmed = await fixture.Service.UpdateAttributeAsync(zone.Id, request with { ConfirmValueRemoval = true }, Ct);

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(change == "type" ? BusinessFormPersonAttributeType.Text : BusinessFormPersonAttributeType.Catalog, zone.Type);
        Assert.Equal(change == "type" ? null : fixture.OtherCatalog.Id, zone.SourceCatalogId);
        var row = Assert.Single(fixture.Data);
        var stored = JsonDocument.Parse(row.ValuesJson).RootElement;
        Assert.False(stored.TryGetProperty(zone.Id.ToString("D"), out _));
        Assert.Equal("Nota", stored.GetProperty(note.Id.ToString("D")).GetString());
        Assert.Equal(2, row.Revision);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.Work.LastPolicy);
    }

    [Fact]
    public async Task Changing_the_type_counts_only_people_who_are_not_deleted_and_still_clears_the_values_of_deleted_accounts()
    {
        var fixture = new FormPersonDataFixture();
        var zone = fixture.AddAttribute("zone");
        var deleted = Guid.CreateVersion7();
        fixture.DeletedUsers.Add(deleted);
        var storedJson = JsonSerializer.Serialize(new Dictionary<string, string> { [zone.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D") });
        var visible = BusinessFormPersonData.Create(fixture.Subject, storedJson);
        fixture.Data.AddRange([visible, BusinessFormPersonData.Create(deleted, storedJson)]);
        var request = new UpdateFormPersonAttributeRequest(1, Names(), "Text", null, 0, true);

        var withVisiblePerson = await fixture.Service.UpdateAttributeAsync(zone.Id, request, Ct);

        Assert.Equal("Forms.PersonAttribute.ValueRemovalNotConfirmed", withVisiblePerson.Error.Code);
        Assert.Equal(1, withVisiblePerson.Error.Metadata!["count"]);

        fixture.Data.Remove(visible);
        var onlyDeleted = await fixture.Service.UpdateAttributeAsync(zone.Id, request, Ct);

        Assert.True(onlyDeleted.IsSuccess);
        Assert.Equal(BusinessFormPersonAttributeType.Text, zone.Type);
        var row = Assert.Single(fixture.Data);
        Assert.Equal(deleted, row.UserId);
        Assert.False(JsonDocument.Parse(row.ValuesJson).RootElement.TryGetProperty(zone.Id.ToString("D"), out _));
        Assert.Equal(2, row.Revision);
        Assert.Equal(1, fixture.Work.Commits);
    }

    [Fact]
    public async Task Saving_person_values_uses_one_boundary_and_rejects_a_stale_initial_revision()
    {
        var fixture = new FormPersonDataFixture();
        fixture.Grant(Permissions.Users.Manage);
        var attribute = fixture.AddAttribute();
        var request = new UpdateFormPersonDataRequest(null,
            new Dictionary<string, string?> { [attribute.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D") });

        var saved = await fixture.Service.SaveDataAsync(fixture.Subject, request, Ct);
        var stale = await fixture.Service.SaveDataAsync(fixture.Subject, request, Ct);

        Assert.True(saved.IsSuccess);
        Assert.Equal(1, saved.Value.Revision);
        Assert.Equal(FormPersonDataErrors.RevisionConflict, stale.Error);
        Assert.Single(fixture.Data);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, fixture.Work.LastPolicy);
        Assert.Equal(["attributes", "data:" + fixture.Subject.ToString("N"), "catalog:" + fixture.Catalog.Id.ToString("N")], fixture.Events.Take(3));
    }

    [Fact]
    public async Task Invalid_or_inactive_catalog_choices_never_create_a_person_data_row()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute();
        foreach (var value in new[] { Guid.CreateVersion7().ToString("D"), fixture.InactiveItem.Id.ToString("D"), "not-an-id" })
        {
            var result = await fixture.Service.SaveDataAsync(fixture.Subject,
                new(null, new Dictionary<string, string?> { [attribute.Id.ToString("D")] = value }), Ct);
            Assert.Equal(FormPersonDataErrors.ValueInvalid.Code, result.Error.Code);
            Assert.Empty(fixture.Data);
        }
        Assert.Equal(0, fixture.Work.Commits);
    }

    [Fact]
    public async Task Saving_active_values_preserves_values_of_inactive_attributes_and_normalizes_text()
    {
        var fixture = new FormPersonDataFixture();
        var inactive = fixture.AddAttribute("old_zone", active: false);
        var current = fixture.AddAttribute("note", catalog: false);
        fixture.Data.Add(BusinessFormPersonData.Create(fixture.Subject, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            [inactive.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D"), [current.Id.ToString("D")] = "Antes",
        })));

        var result = await fixture.Service.SaveDataAsync(fixture.Subject,
            new(1, new Dictionary<string, string?> { [current.Id.ToString("D")] = "  Después  " }), Ct);

        Assert.True(result.IsSuccess);
        var stored = JsonDocument.Parse(Assert.Single(fixture.Data).ValuesJson).RootElement;
        Assert.Equal(fixture.ActiveItem.Id.ToString("D"), stored.GetProperty(inactive.Id.ToString("D")).GetString());
        Assert.Equal("Después", stored.GetProperty(current.Id.ToString("D")).GetString());
        Assert.False(result.Value.Values.ContainsKey(inactive.Id.ToString("D")));
    }

    [Fact]
    public async Task Revoking_write_access_while_taking_the_lock_rolls_back_without_adding_values()
    {
        var fixture = new FormPersonDataFixture();
        fixture.AddAttribute();
        fixture.OnLock = () => fixture.Grant();

        var result = await fixture.Service.SaveDataAsync(fixture.Subject, new(null, new Dictionary<string, string?>()), Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Empty(fixture.Data);
        Assert.Equal(0, fixture.Work.Commits);
        Assert.Equal(1, fixture.Work.Rollbacks);
    }

    [Fact]
    public async Task Revoking_read_access_discards_the_entire_person_data_response_without_a_transaction()
    {
        var fixture = new FormPersonDataFixture();
        fixture.OnRecipientRead = () => fixture.Grant();

        var result = await fixture.Service.GetDataAsync(fixture.Subject, Ct);

        Assert.Equal(FormDefinitionErrors.Forbidden, result.Error);
        Assert.Equal(1, fixture.RecipientReads);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task A_user_manager_can_read_only_active_source_options_without_catalog_read_access()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute();
        fixture.Grant(Permissions.Users.Manage);

        var result = await fixture.Service.ListOptionsAsync(attribute.Id, new(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(fixture.ActiveItem.Id, Assert.Single(result.Value.Items).Id);
        Assert.Equal(0, fixture.RecipientReads);
        Assert.Equal(0, fixture.Work.Transactions);
        fixture.Catalog.UpdateManual(1, new Dictionary<string, string> { ["es"] = "Zonas", ["en"] = "Zones" },
            new Dictionary<string, string> { ["es"] = "Zonas", ["en"] = "Zones" }, false);
        Assert.Equal(FormPersonDataErrors.SourceUnavailable, (await fixture.Service.ListOptionsAsync(attribute.Id, new(), Ct)).Error);
    }

    [Fact]
    public async Task A_stored_item_that_is_not_an_option_of_the_attribute_source_is_returned_without_the_text_of_another_source()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute();
        Assert.True(attribute.Update(1, new Dictionary<string, string> { ["es"] = "Zona" }, BusinessFormPersonAttributeType.Catalog,
            fixture.OtherCatalog.Id, attribute.Position, true).IsSuccess);
        fixture.Data.Add(BusinessFormPersonData.Create(fixture.Subject,
            JsonSerializer.Serialize(new Dictionary<string, string> { [attribute.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D") })));

        var result = await fixture.Service.GetDataAsync(fixture.Subject, Ct);

        var value = result.Value.Values[attribute.Id.ToString("D")];
        Assert.Equal(fixture.ActiveItem.Id.ToString("D"), value.Value);
        Assert.Equal("", value.Text);
    }

    [Fact]
    public async Task Data_revisions_and_values_are_independent_for_each_person()
    {
        var fixture = new FormPersonDataFixture();
        var attribute = fixture.AddAttribute("note", catalog: false);
        var other = fixture.Users.AddUser("person-data-other@example.invalid").Id;
        var first = new UpdateFormPersonDataRequest(null, new Dictionary<string, string?> { [attribute.Id.ToString("D")] = "Primera" });
        var second = new UpdateFormPersonDataRequest(null, new Dictionary<string, string?> { [attribute.Id.ToString("D")] = "Segunda" });

        var savedFirst = await fixture.Service.SaveDataAsync(fixture.Subject, first, Ct);
        var savedSecond = await fixture.Service.SaveDataAsync(other, second, Ct);

        Assert.True(savedFirst.IsSuccess);
        Assert.True(savedSecond.IsSuccess);
        Assert.Equal(1, savedFirst.Value.Revision);
        Assert.Equal(1, savedSecond.Value.Revision);
        Assert.Equal("Primera", (await fixture.Service.GetDataAsync(fixture.Subject, Ct)).Value.Values[attribute.Id.ToString("D")].Value);
        Assert.Equal("Segunda", (await fixture.Service.GetDataAsync(other, Ct)).Value.Values[attribute.Id.ToString("D")].Value);
        Assert.Equal(2, fixture.Work.Commits);
    }

    [Fact]
    public async Task Missing_delta_values_preserve_other_active_values_and_null_clears_only_the_named_attribute()
    {
        var fixture = new FormPersonDataFixture();
        var first = fixture.AddAttribute("first", catalog: false);
        var second = fixture.AddAttribute("second", catalog: false);
        fixture.Data.Add(BusinessFormPersonData.Create(fixture.Subject, JsonSerializer.Serialize(new Dictionary<string, string>
        { [first.Id.ToString("D")] = "Primero", [second.Id.ToString("D")] = "Segundo" })));
        var result = await fixture.Service.SaveDataAsync(fixture.Subject,
            new(1, new Dictionary<string, string?> { [first.Id.ToString("D")] = null }), Ct);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Values.ContainsKey(first.Id.ToString("D")));
        Assert.Equal("Segundo", result.Value.Values[second.Id.ToString("D")].Value);
    }

    [Fact]
    public async Task A_delta_preserves_the_source_label_of_an_unchanged_catalog_value_in_its_receipt()
    {
        var fixture = new FormPersonDataFixture();
        var catalog = fixture.AddAttribute();
        var note = fixture.AddAttribute("note", catalog: false);
        fixture.Data.Add(BusinessFormPersonData.Create(fixture.Subject, JsonSerializer.Serialize(new Dictionary<string, string>
        { [catalog.Id.ToString("D")] = fixture.ActiveItem.Id.ToString("D") })));
        var saved = await fixture.Service.SaveDataAsync(fixture.Subject,
            new(1, new Dictionary<string, string?> { [note.Id.ToString("D")] = "Actualizado" }), Ct);
        Assert.True(saved.IsSuccess);
        Assert.Equal("Centro", saved.Value.Values[catalog.Id.ToString("D")].Text);
    }

    private static Dictionary<string, string?> Names() => new() { ["es"] = "Zona", ["en"] = "Zone" };
}
