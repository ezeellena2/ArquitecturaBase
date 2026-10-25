using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogFieldCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Capture_seals_only_active_present_fields_and_keeps_canonical_zero_false_and_localized_names()
    {
        var catalogId = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("choice", true, catalogId);
        var fields = new[] { Field("amount", "integer", "data"), Field("enabled", "boolean", "filter"),
            Field("missing", "text", "data"), Field("retired", "text", "data", active: false) };
        var itemId = Guid.NewGuid();
        f.Repository.CatalogFieldValues[itemId] = new(itemId, catalogId, FormDraftPolicy.Serialize(fields),
            FormDraftPolicy.Serialize(new Dictionary<string, object> { [fields[0].Id.ToString("D")] = 0,
                [fields[1].Id.ToString("D")] = false, [fields[3].Id.ToString("D")] = "Retirado" }));
        var offer = Offer(f, "Manual", [new(itemId, "Norte", null)]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = itemId.ToString("D") })
            with { Catalogs = [offer] }, Ct);

        Assert.Equal("Completed", result.State);
        var reference = Assert.Single(f.Repository.References);
        Assert.NotNull(reference.FieldValuesJson);
        var snapshot = FormDraftPolicy.Read<FormCatalogCapturedField[]>(reference.FieldValuesJson);
        Assert.Equal(fields.Take(2).Select(field => field.Id), snapshot.Select(field => field.Id));
        Assert.Equal("amount", snapshot[0].Key);
        Assert.Equal("integer", snapshot[0].Type);
        Assert.Equal("data", snapshot[0].Role);
        Assert.Equal("Campo amount", snapshot[0].Name["es"]);
        Assert.False(snapshot[0].Name.ContainsKey("en"));
        Assert.Equal(JsonValueKind.Number, snapshot[0].Value.ValueKind);
        Assert.Equal(0, snapshot[0].Value.GetInt32());
        Assert.Equal(JsonValueKind.False, snapshot[1].Value.ValueKind);
        Assert.Equal("filter", snapshot[1].Role);
        Assert.All(snapshot, field => Assert.Null(field.Text));
        Assert.Equal([itemId], Assert.Single(f.Repository.FieldValueBatches));
        Assert.Contains(catalogId, Assert.Single(f.Repository.FieldValueReadLocks));
    }

    [Fact]
    public async Task Multiple_selected_items_and_their_source_texts_are_read_in_two_batches_and_replay_keeps_the_snapshot()
    {
        var catalogId = Guid.NewGuid();
        var sourceCatalogId = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("multiChoice", true, catalogId, selection: new(1, 2));
        var fields = new[] { Field("region", "catalog", "data", sourceCatalogId: sourceCatalogId) };
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var sourceItem = Guid.NewGuid();
        var valuesJson = FormDraftPolicy.Serialize(new Dictionary<string, object> { [fields[0].Id.ToString("D")] = sourceItem.ToString("D") });
        foreach (var itemId in new[] { first, second })
            f.Repository.CatalogFieldValues[itemId] = new(itemId, catalogId, FormDraftPolicy.Serialize(fields), valuesJson);
        f.Repository.CatalogFieldValues[sourceItem] = new(sourceItem, sourceCatalogId, null, "{}", "Zona norte", "North area");
        var request = f.Request(new Dictionary<string, string> { [f.Key] = FormDraftPolicy.Serialize(new[] { second, first }) })
            with { Catalogs = [Offer(f, "Manual", [new(first, "Primera", null), new(second, "Segunda", null)])] };

        var result = await f.Sink.CaptureAsync(request, Ct);

        Assert.Equal("Completed", result.State);
        Assert.Equal([second, first], f.Repository.References.Select(reference => reference.ItemId));
        var snapshots = f.Repository.References.Select(reference => reference.FieldValuesJson).ToArray();
        foreach (var reference in f.Repository.References)
        {
            Assert.NotNull(reference.FieldValuesJson);
            var captured = Assert.Single(FormDraftPolicy.Read<FormCatalogCapturedField[]>(reference.FieldValuesJson));
            Assert.Equal(sourceItem.ToString("D"), captured.Value.GetString());
            Assert.Equal("Zona norte", captured.Text!["es"]);
            Assert.Equal("North area", captured.Text["en"]);
        }
        Assert.Equal(2, f.Repository.FieldValueBatches.Count);
        Assert.Equal(new[] { first, second }.Order(), f.Repository.FieldValueBatches[0].Order());
        Assert.Equal([sourceItem], f.Repository.FieldValueBatches[1]);
        Assert.All(f.Repository.FieldValueReadLocks, locks => Assert.Contains(catalogId, locks));

        f.Repository.CatalogFieldValues[sourceItem] = new(sourceItem, sourceCatalogId, null, "{}", "Zona editada", "Edited area");
        Assert.Equal(result, await f.Sink.CaptureAsync(request, Ct));
        Assert.Equal(2, f.Repository.FieldValueBatches.Count);
        Assert.Equal(snapshots, f.Repository.References.Select(reference => reference.FieldValuesJson));
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_catalog_field_whose_source_option_has_only_English_text_seals_that_text()
    {
        var catalogId = Guid.NewGuid();
        var sourceCatalogId = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("choice", true, catalogId);
        var fields = new[] { Field("region", "catalog", "data", sourceCatalogId: sourceCatalogId) };
        var itemId = Guid.NewGuid();
        var sourceItem = Guid.NewGuid();
        f.Repository.CatalogFieldValues[itemId] = new(itemId, catalogId, FormDraftPolicy.Serialize(fields),
            FormDraftPolicy.Serialize(new Dictionary<string, object> { [fields[0].Id.ToString("D")] = sourceItem.ToString("D") }));
        f.Repository.CatalogFieldValues[sourceItem] = new(sourceItem, sourceCatalogId, null, "{}", null, "North area");

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = itemId.ToString("D") })
            with { Catalogs = [Offer(f, "Manual", [new(itemId, "Norte", null)])] }, Ct);

        Assert.Equal("Completed", result.State);
        var captured = Assert.Single(FormDraftPolicy.Read<FormCatalogCapturedField[]>(Assert.Single(f.Repository.References).FieldValuesJson!));
        Assert.Equal(sourceItem.ToString("D"), captured.Value.GetString());
        Assert.Equal(["en"], captured.Text!.Keys);
        Assert.Equal("North area", captured.Text["en"]);
    }

    [Fact]
    public async Task A_saved_value_that_is_not_an_option_of_the_current_source_is_left_out_of_the_seal()
    {
        var catalogId = Guid.NewGuid();
        var sourceCatalogId = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("choice", true, catalogId);
        var fields = new[] { Field("region", "catalog", "data", sourceCatalogId: sourceCatalogId), Field("cuit", "text", "data") };
        var itemId = Guid.NewGuid();
        var previousSourceItem = Guid.NewGuid();
        f.Repository.CatalogFieldValues[itemId] = new(itemId, catalogId, FormDraftPolicy.Serialize(fields),
            FormDraftPolicy.Serialize(new Dictionary<string, object> { [fields[0].Id.ToString("D")] = previousSourceItem.ToString("D"),
                [fields[1].Id.ToString("D")] = "20-1" }));
        f.Repository.CatalogFieldValues[previousSourceItem] = new(previousSourceItem, Guid.NewGuid(), null, "{}", "Zona vieja", null);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = itemId.ToString("D") })
            with { Catalogs = [Offer(f, "Manual", [new(itemId, "Norte", null)])] }, Ct);

        Assert.Equal("Completed", result.State);
        var captured = Assert.Single(FormDraftPolicy.Read<FormCatalogCapturedField[]>(Assert.Single(f.Repository.References).FieldValuesJson!));
        Assert.Equal("cuit", captured.Key);
    }

    [Theory]
    [InlineData("Manual", 1)]
    [InlineData("Filters", 0)]
    public async Task Catalogs_without_fields_keep_null_snapshots_and_legacy_reference_json(string kind, int batches)
    {
        var catalogId = Guid.NewGuid();
        var f = new BusinessFormCaptureFixture("choice", true, catalogId);
        var itemId = Guid.NewGuid();
        f.Repository.CatalogFieldValues[itemId] = new(itemId, catalogId, null, "{}");
        var offer = Offer(f, kind, [new(itemId, "Norte", null)]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = itemId.ToString("D") })
            with { Catalogs = [offer] }, Ct);

        Assert.Equal("Completed", result.State);
        var reference = Assert.Single(f.Repository.References);
        Assert.Null(reference.FieldValuesJson);
        Assert.Equal(batches, f.Repository.FieldValueBatches.Count);
        var row = new FormRecordReferenceRow(reference.RecordId, reference.FieldId, reference.CatalogId,
            reference.CatalogRevision, reference.ItemId, reference.Text, reference.Detail, reference.CatalogName);
        Assert.DoesNotContain("fieldValuesJson", FormDraftPolicy.Serialize(row), StringComparison.Ordinal);
    }

    private static BusinessFormCatalogField Field(string key, string type, string role, bool active = true, Guid? sourceCatalogId = null) =>
        new(Guid.NewGuid(), key, new Dictionary<string, string> { ["es"] = "Campo " + key }, type, sourceCatalogId,
            false, false, null, null, type == "text" ? 300 : null, role, 0, active);

    private static StructuredFormCatalogOffer Offer(BusinessFormCaptureFixture fixture, string kind,
        IReadOnlyList<StructuredFormCatalogOption> options)
    {
        var field = Assert.Single(FormDraftPolicy.Read<FormVersionSchema>(fixture.Version.SchemaJson).Questions);
        return new(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 1, kind, "Opciones", options);
    }
}
