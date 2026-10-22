using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogTypedCaptureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("Integer", "1500")]
    [InlineData("Decimal", "1500.5")]
    [InlineData("Date", "2026-10-22")]
    public async Task Capture_seals_the_offered_value_and_type_with_the_reference(string type, string value)
    {
        var f = new BusinessFormCaptureFixture("choice", true, Guid.NewGuid());
        var field = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions[0];
        var option = new StructuredFormCatalogOption(Guid.NewGuid(), "Primero", "Detalle", Value: value);
        var offer = new StructuredFormCatalogOffer(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 3,
            "Manual", "Opciones", [option], ValueType: type);
        var request = f.Request(new Dictionary<string, string> { [f.Key] = option.Id.ToString("D") }) with { Catalogs = [offer] };

        var result = await f.Sink.CaptureAsync(request, Ct);

        Assert.Equal("Completed", result.State);
        var reference = Assert.Single(f.Repository.References);
        Assert.Equal(value, reference.Value);
        Assert.Equal(type, reference.ValueType);
        Assert.Equal(3, reference.CatalogRevision);
        Assert.Equal(option.Text, reference.Text);
        Assert.Equal(result, await f.Sink.CaptureAsync(request, Ct));
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task Editing_the_catalog_after_preparation_does_not_replace_the_offered_value()
    {
        var source = new FormCatalogPreparationFixture(valueType: BusinessFormCatalogValueType.Decimal);
        var item = source.Catalog.AddManualItem("ONE", "Primero", "First", null, null, 1, "1500").Value;
        source.Items.Add(item);
        var f = new BusinessFormCaptureFixture("choice", true, source.Catalog.Id);
        var schema = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson);
        var preparation = await new FormCatalogOptionsPolicy(source, source).PrepareAsync(f.Actor, "es", schema, "es", Ct);
        Assert.True(preparation.CanSend);
        var offer = Assert.Single(preparation.Catalogs);
        Assert.True(source.Catalog.UpdateManual(1, FormCatalogPreparationFixture.Texts, FormCatalogPreparationFixture.Texts).IsSuccess);
        Assert.True(source.Catalog.UpdateManualItem(item, item.Code, "Renombrado", item.TextEn,
            item.DetailEs, item.DetailEn, item.Position, "9999").IsSuccess);
        Assert.Equal("9999", item.Value);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = item.Id.ToString("D") })
            with { Catalogs = [offer] }, Ct);

        Assert.Equal("Completed", result.State);
        var reference = Assert.Single(f.Repository.References);
        Assert.Equal("1500", reference.Value);
        Assert.Equal("Decimal", reference.ValueType);
        Assert.Equal("Primero", reference.Text);
        Assert.Equal(1, reference.CatalogRevision);
    }

    [Theory]
    [InlineData("Manual", null, "42")]
    [InlineData("Manual", "Integer", null)]
    [InlineData("Manual", "Text", null)]
    [InlineData("Manual", "Text", "42")]
    [InlineData("Manual", "Unknown", "42")]
    [InlineData("Manual", "integer", "42")]
    [InlineData("Manual", "1", "42")]
    [InlineData("Manual", "Integer", "01")]
    [InlineData("Manual", "Integer", " 1 ")]
    [InlineData("Manual", "Integer", "-0")]
    [InlineData("Manual", "Integer", "1.0")]
    [InlineData("Manual", "Decimal", "1.00")]
    [InlineData("Manual", "Decimal", "1e3")]
    [InlineData("Manual", "Date", "2026-02-30")]
    [InlineData("Manual", "Date", "22/10/2026")]
    [InlineData("Filters", "Integer", "1")]
    public async Task Invalid_value_pairs_types_or_noncanonical_values_reject_before_capturing(string kind, string? type, string? value)
    {
        var f = new BusinessFormCaptureFixture("choice", true, Guid.NewGuid());
        var field = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions[0];
        var option = new StructuredFormCatalogOption(Guid.NewGuid(), "Primero", null, Value: value);
        var offer = new StructuredFormCatalogOffer(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 1,
            kind, "Opciones", [option], ValueType: type);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = option.Id.ToString("D") })
            with { Catalogs = [offer] }, Ct);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Empty(f.Repository.Records);
        Assert.Empty(f.Repository.Values);
        Assert.Empty(f.Repository.References);
        Assert.Empty(f.Actions.Runs.Runs);
        Assert.Equal(0, f.Definition.LastRecordNumber);
        Assert.Equal("Rejected", Assert.Single(f.Repository.Receipts).State);
    }

    [Fact]
    public async Task An_invalid_typed_offer_never_uploads_a_valid_photo_from_the_same_response()
    {
        var f = new BusinessFormCaptureFixture("choice", true, Guid.NewGuid(), withPhoto: true);
        var field = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions[0];
        var item = Guid.NewGuid();
        var offer = new StructuredFormCatalogOffer(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 1,
            "Manual", "Opciones", [new(item, "Primero", null, Value: "01")], ValueType: "Integer");
        var request = f.Request(new Dictionary<string, string> { [f.Key] = item.ToString("D") }) with
        {
            Catalogs = [offer],
            Files = [new(f.PhotoFieldId!.Value, 0, "media", "photo", "photo.png", "image/png", new string('a', 64), [1, 2, 3])]
        };

        var result = await f.Sink.CaptureAsync(request, Ct);

        Assert.Equal("Rejected", result.State);
        Assert.Equal(FormRecordErrors.InvalidResponse.Code, result.ErrorCode);
        Assert.Equal(0, f.FileStore.WriteCount);
        Assert.Empty(f.StoredFiles.Files);
        Assert.Empty(f.Repository.Records);
        Assert.Equal(0, f.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task Capture_keeps_the_proposed_and_final_options_own_typed_values()
    {
        var f = new BusinessFormCaptureFixture("choice", true, Guid.NewGuid());
        var field = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions[0];
        var proposed = Guid.NewGuid();
        var final = Guid.NewGuid();
        var offer = new StructuredFormCatalogOffer(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 3,
            "Manual", "Opciones", [new(proposed, "Propuesto", null, Value: "10"), new(final, "Final", null, Value: "20")], ValueType: "Integer");
        var origin = new StructuredRecordCaptureOrigin(StructuredRecordSenderKind.Manual, Guid.NewGuid(), null, null,
            [new(field.FieldId, "choice", proposed.ToString("D"))], [field.FieldId]);

        var result = await f.Sink.CaptureAsync(f.Request(new Dictionary<string, string> { [f.Key] = final.ToString("D") })
            with { Catalogs = [offer], Origin = origin }, Ct);

        Assert.Equal("Completed", result.State);
        var record = Assert.Single(f.Repository.Records);
        var snapshot = FormDraftPolicy.Read<FormRecordCaptureOriginSnapshot>(record.CaptureOriginJson!);
        var proposedReference = Assert.Single(snapshot.ProposedReferences);
        Assert.Equal(proposed, proposedReference.ItemId);
        Assert.Equal("10", proposedReference.Value);
        Assert.Equal("Integer", proposedReference.ValueType);
        var finalReference = Assert.Single(f.Repository.References);
        Assert.Equal(final, finalReference.ItemId);
        Assert.Equal("20", finalReference.Value);
        Assert.Equal("Integer", finalReference.ValueType);
        Assert.Equal([field.FieldId], snapshot.ChangedFieldIds);
    }

    [Fact]
    public async Task A_changed_typed_offer_is_a_replay_conflict_and_never_consumes_another_number()
    {
        var f = new BusinessFormCaptureFixture("choice", true, Guid.NewGuid());
        var field = FormDraftPolicy.Read<FormVersionSchema>(f.Version.SchemaJson).Questions[0];
        var item = Guid.NewGuid();
        var offer = new StructuredFormCatalogOffer(field.FieldId, field.Key, "Opción", field.Source!.Catalog!.Value, 1,
            "Manual", "Opciones", [new(item, "Primero", null, Value: "10")], ValueType: "Integer");
        var request = f.Request(new Dictionary<string, string> { [f.Key] = item.ToString("D") }) with { Catalogs = [offer] };
        Assert.Equal("Completed", (await f.Sink.CaptureAsync(request, Ct)).State);

        var conflict = await f.Sink.CaptureAsync(request with { Catalogs = [offer with { Options = [offer.Options[0] with { Value = "20" }] }] }, Ct);

        Assert.Equal("Rejected", conflict.State);
        Assert.Equal(FormRecordErrors.ReplayConflict.Code, conflict.ErrorCode);
        Assert.Single(f.Repository.Records);
        Assert.Equal(1, f.Definition.LastRecordNumber);
    }
}
