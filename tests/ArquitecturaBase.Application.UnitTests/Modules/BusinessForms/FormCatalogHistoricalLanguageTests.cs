using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using Microsoft.Extensions.Logging.Abstractions;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;

public sealed class FormCatalogHistoricalLanguageTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_manual_offer_keeps_raw_translations_in_addition_to_its_display_text()
    {
        var fixture = new FormCatalogPreparationFixture();
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", "Sucursal norte", "North branch", "Planta baja", "Ground floor", 0));
        var result = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, true, Ct);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal("North branch", Assert.Single(offer.Options).Text);
        using var json = JsonDocument.Parse(FormDraftPolicy.Serialize(offer));
        var translations = json.RootElement.GetProperty("options")[0].GetProperty("translations");
        Assert.Equal("Sucursal norte", translations.GetProperty("es").GetProperty("text").GetString());
        Assert.Equal("North branch", translations.GetProperty("en").GetProperty("text").GetString());
        Assert.Equal("Planta baja", translations.GetProperty("es").GetProperty("detail").GetString());
        Assert.Equal("Personas", json.RootElement.GetProperty("catalogNames").GetProperty("es").GetString());
    }

    [Fact]
    public async Task A_display_fallback_is_not_recorded_as_a_complete_English_translation()
    {
        var fixture = new FormCatalogPreparationFixture();
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", "Sucursal norte", null, null, null, 0));
        var result = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, true, Ct);
        var option = Assert.Single(Assert.Single(result.Catalogs).Options);
        Assert.Equal("Sucursal norte", option.Text);
        using var json = JsonDocument.Parse(FormDraftPolicy.Serialize(option));
        var translations = json.RootElement.GetProperty("translations");
        Assert.True(translations.TryGetProperty("es", out _));
        Assert.False(translations.TryGetProperty("en", out _));
    }

    [Fact]
    public void A_captured_manual_choice_uses_its_historical_translation_in_each_language()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], true);
        foreach (var (culture, expected) in new[] { ("es", "Sucursal norte"), ("en", "North branch") })
        {
            var row = captured.Snapshot.Response.Record;
            var reply = FormRecordPresentationPolicy.Project(row, culture, FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson));
            Assert.Equal(expected, Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(reply.Values).Value).Label);
        }
    }

    [Fact]
    public async Task A_catalog_item_without_English_uses_its_original_text_inside_an_English_message()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], false);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var snapshot = FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        var recipient = Assert.Single(snapshot.Recipients);
        Assert.Equal("en", recipient.Culture);
        Assert.Equal("Received VIS-000001: Sucursal norte", recipient.Body);
    }

    [Fact]
    public async Task A_catalog_item_without_English_used_by_the_PDF_does_not_change_an_earlier_messages_language()
    {
        var fixture = new FormActionFixture();
        var action = FormActionFixture.Action() with { Text = ConstantTexts() };
        var captured = ChoiceCapture(fixture, [
            new(Guid.NewGuid(), "Received", [], [action]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])
        ], false);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var recipient = Assert.Single(FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson).Recipients);
        Assert.Equal("en", recipient.Culture);
        Assert.Equal("Received VIS-000001", recipient.Body);
    }

    [Fact]
    public async Task A_missing_translation_that_neither_the_PDF_nor_a_placeholder_uses_does_not_force_fallback()
    {
        var fixture = new FormActionFixture();
        var action = FormActionFixture.Action() with { Text = ConstantTexts() };
        var captured = ChoiceCapture(fixture, [
            new(Guid.NewGuid(), "Received", [], [action]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])
        ], false);
        var design = new FormPdfDesign(new Dictionary<string, string> { ["es"] = "Constancia", ["en"] = "Receipt" }, [], true, null, true);
        fixture.CurrentDesign = new(Guid.NewGuid(), captured.Record.DefinitionId, 1, "es", FormDraftPolicy.Serialize(design), fixture.Clock.GetUtcNow().UtcDateTime, new(Guid.NewGuid(), null));
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var recipient = Assert.Single(FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson).Recipients);
        Assert.Equal("en", recipient.Culture);
        Assert.Equal("Received VIS-000001", recipient.Body);
    }

    [Fact]
    public async Task A_detail_that_is_not_rendered_does_not_make_an_otherwise_complete_label_unavailable()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], true, englishDetail: null);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var recipient = Assert.Single(FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson).Recipients);
        Assert.Equal("en", recipient.Culture);
        Assert.Equal("Received VIS-000001: North branch", recipient.Body);
    }

    [Fact]
    public void A_captured_item_without_English_keeps_the_catalog_name_that_was_actually_offered()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], false);
        var row = captured.Snapshot.Response.Record;

        var reply = FormRecordPresentationPolicy.Project(row, "en", FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson));

        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(reply.Values).Value);
        Assert.Equal("Sucursal norte", choice.Label);
        Assert.Equal("Planta baja", choice.Detail);
        Assert.Equal("Branches", choice.CatalogName);
        var reference = Assert.Single(row.References!);
        Assert.False(FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson!).ContainsKey("en"));
    }

    [Fact]
    public void A_captured_item_with_an_English_name_keeps_its_original_detail_when_that_detail_is_untranslated()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], true, englishDetail: null);
        var row = captured.Snapshot.Response.Record;

        var reply = FormRecordPresentationPolicy.Project(row, "en", FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson));

        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(reply.Values).Value);
        Assert.Equal("North branch", choice.Label);
        Assert.Equal("Planta baja", choice.Detail);
        var reference = Assert.Single(row.References!);
        Assert.Null(FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson!)["en"].Detail);
    }

    [Fact]
    public void A_captured_item_with_a_Spanish_name_takes_its_English_original_detail_when_that_detail_is_untranslated()
    {
        // The catalog was written in English: the Spanish translation has a name but no detail, so only English can supply it.
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], true, spanishDetail: null);
        var row = captured.Snapshot.Response.Record;

        var reply = FormRecordPresentationPolicy.Project(row, "es", FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson));

        var choice = Assert.IsType<StructuredRecordChoiceValue>(Assert.Single(reply.Values).Value);
        Assert.Equal("Sucursal norte", choice.Label);
        Assert.Equal("Ground floor", choice.Detail);
        Assert.Equal("Sucursales", choice.CatalogName);
        var reference = Assert.Single(row.References!);
        Assert.Null(FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson!)["es"].Detail);
    }

    [Fact]
    public async Task A_new_notification_snapshot_does_not_duplicate_unrelated_captured_answers()
    {
        var fixture = new FormActionFixture();
        var action = FormActionFixture.Action() with { Text = ConstantTexts() };
        var privateAnswer = "PRIVATE-UNUSED-ANSWER-" + new string('x', 9000);
        var captured = fixture.Capture([new(Guid.NewGuid(), "Received", [], [action])], privateAnswer);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var stored = Assert.Single(fixture.Runs.Runs).SnapshotJson;
        Assert.DoesNotContain(privateAnswer, stored, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(stored);
        Assert.False(json.RootElement.TryGetProperty("record", out _));
        Assert.False(json.RootElement.TryGetProperty("ruleQuestions", out _));
        Assert.True(stored.Length < 2000);
    }

    [Fact]
    public async Task A_new_PDF_snapshot_keeps_fixed_author_and_decision_without_the_response_body()
    {
        var fixture = new FormActionFixture();
        var privateAnswer = "PRIVATE-PDF-ANSWER-" + new string('x', 9000);
        var captured = fixture.Capture([new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])], privateAnswer);
        var snapshot = captured.Snapshot with { Decision = new(Guid.NewGuid(), Guid.NewGuid(), "Confirmed approver", fixture.Clock.GetUtcNow().UtcDateTime, "Approved", null) };
        await fixture.Recorder.RecordAsync(captured.Record, snapshot, "Approved", Ct);
        var stored = Assert.Single(fixture.Runs.Runs).SnapshotJson;
        Assert.DoesNotContain(privateAnswer, stored, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(stored);
        var pdf = json.RootElement.GetProperty("pdf");
        Assert.False(pdf.TryGetProperty("record", out _));
        Assert.Equal(captured.Record.ActorUserId, pdf.GetProperty("author").GetProperty("userId").GetGuid());
        Assert.Equal("Confirmed approver", pdf.GetProperty("decision").GetProperty("actorName").GetString());
    }

    [Fact]
    public async Task Capture_keeps_the_offered_translations_even_if_the_source_dictionary_changes()
    {
        var catalogId = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalogId);
        var fieldId = Guid.ParseExact(fixture.Key[6..], "N");
        var itemId = Guid.NewGuid();
        var translations = new Dictionary<string, StructuredFormCatalogOptionText>
        {
            ["es"] = new("Sucursal norte", "Planta baja"),
            ["en"] = new("North branch", "Ground floor")
        };
        var names = new Dictionary<string, string> { ["es"] = "Sucursales", ["en"] = "Branches" };
        var offer = new StructuredFormCatalogOffer(fieldId, fixture.Key, "Sucursal", catalogId, 1, "Manual", "Branches",
            [new(itemId, "North branch", "Ground floor", translations)], names);
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = itemId.ToString("D") }) with { Catalogs = [offer] };
        fixture.Repository.ItemActive = true;
        var result = await fixture.Sink.CaptureAsync(request, Ct);
        Assert.Equal("Completed", result.State);
        Assert.Equal(result, await fixture.Sink.CaptureAsync(request, Ct));
        translations["es"] = new("Renombrada", null);
        names["es"] = "Otro catálogo";
        var reference = Assert.Single(fixture.Repository.References);
        var saved = FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson!);
        Assert.Equal("Sucursal norte", saved["es"].Text);
        Assert.Equal("Sucursales", saved["es"].CatalogName);
        Assert.Equal("North branch", reference.Text);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Fact]
    public async Task A_Spanish_form_captures_an_option_offered_only_in_its_English_catalog_original()
    {
        var catalogId = Guid.NewGuid();
        var fixture = new BusinessFormCaptureFixture("choice", true, catalogId);
        Assert.Equal("es", fixture.Version.Culture);
        var fieldId = Guid.ParseExact(fixture.Key[6..], "N");
        var itemId = Guid.NewGuid();
        // The catalog was written in English and the option has no Spanish translation: the sealed offer carries only its original.
        var translations = new Dictionary<string, StructuredFormCatalogOptionText> { ["en"] = new("North branch", "Ground floor") };
        var offer = new StructuredFormCatalogOffer(fieldId, fixture.Key, "Sucursal", catalogId, 1, "Manual", "Branches",
            [new(itemId, "North branch", "Ground floor", translations)], new Dictionary<string, string> { ["en"] = "Branches" });
        var request = fixture.Request(new Dictionary<string, string> { [fixture.Key] = itemId.ToString("D") }) with { Catalogs = [offer] };

        var result = await fixture.Sink.CaptureAsync(request, Ct);

        Assert.Equal("Completed", result.State);
        Assert.Null(result.ErrorCode);
        var reference = Assert.Single(fixture.Repository.References);
        Assert.Equal(["en"], FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(reference.TranslationsJson!).Keys);
        Assert.Equal(1, fixture.Definition.LastRecordNumber);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public async Task An_English_original_can_offer_the_items_original_text_without_inventing_English(string recipientCulture)
    {
        var fixture = new FormCatalogPreparationFixture(originalCulture: "en", originalOnly: true);
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", "Sucursal norte", null, "Planta baja", null, 0));

        var result = await fixture.Policy.PrepareAsync(Guid.NewGuid(), recipientCulture, fixture.DefinitionId, fixture.Version.Id, true, Ct);

        Assert.True(result.CanSend);
        Assert.Equal("en", result.Culture);
        var offer = Assert.Single(result.Catalogs);
        Assert.Equal("People", offer.Label);
        Assert.Equal("People", offer.CatalogName);
        var option = Assert.Single(offer.Options);
        Assert.Equal("Sucursal norte", option.Text);
        Assert.Equal("Planta baja", option.Detail);
        Assert.False(option.Translations!.ContainsKey("en"));
    }

    [Fact]
    public async Task Rendering_uses_the_historical_choice_translation_for_the_fixed_PDF_culture()
    {
        var actions = new FormActionFixture();
        var captured = ChoiceCapture(actions, [new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])], true);
        var fixture = new FormPdfFixture();
        fixture.Records.ExportRows = [captured.Snapshot.Response];
        var service = new FormPdfActionService(fixture, new MemoryPrivateFileStore(), fixture, fixture, fixture.Records,
            NullLogger<FormPdfActionService>.Instance);
        var prepared = (await service.PrepareAsync(Guid.NewGuid(), captured.Snapshot, Ct)).Value;
        Assert.Contains("en", prepared.Cultures);

        var outcome = await service.ExecuteAsync(captured.Record.Id, prepared with { Cultures = ["en"] }, false, Ct);

        Assert.Equal(FormPdfActionState.Completed, outcome.Value.State);
        Assert.Equal("en", fixture.RenderCulture);
        Assert.Equal("North branch", Assert.Single(fixture.Rendered!.Response.Record.References!).Text);
        Assert.Equal("Ground floor", Assert.Single(fixture.Rendered.Response.Record.References!).Detail);
    }

    [Fact]
    public async Task An_English_fixed_PDF_uses_the_original_catalog_item_without_forging_a_translation()
    {
        var actions = new FormActionFixture();
        var captured = ChoiceCapture(actions, [new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])], false);
        var fixture = new FormPdfFixture();
        fixture.Records.ExportRows = [captured.Snapshot.Response];
        var service = new FormPdfActionService(fixture, new MemoryPrivateFileStore(), fixture, fixture, fixture.Records,
            NullLogger<FormPdfActionService>.Instance);

        var prepared = (await service.PrepareAsync(Guid.NewGuid(), captured.Snapshot, Ct)).Value;

        Assert.Contains("en", prepared.Cultures);
        var outcome = await service.ExecuteAsync(captured.Record.Id, prepared with { Cultures = ["en"] }, false, Ct);
        Assert.Equal(FormPdfActionState.Completed, outcome.Value.State);
        Assert.Equal("en", fixture.RenderCulture);
        var reference = Assert.Single(fixture.Rendered!.Response.Record.References!);
        Assert.Equal("Sucursal norte", reference.Text);
        Assert.Equal("Planta baja", reference.Detail);
        var stored = Assert.Single(captured.Snapshot.Response.Record.References!);
        Assert.False(FormDraftPolicy.Read<Dictionary<string, FormRecordReferenceTextResponse>>(stored.TranslationsJson!).ContainsKey("en"));
    }

    [Fact]
    public async Task A_reference_from_an_English_original_catalog_does_not_make_the_Spanish_original_incomplete()
    {
        var actions = new FormActionFixture();
        var captured = ChoiceCapture(actions, [new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])], true, spanish: false);
        var fixture = new FormPdfFixture();
        fixture.Records.ExportRows = [captured.Snapshot.Response];
        var service = new FormPdfActionService(fixture, new MemoryPrivateFileStore(), fixture, fixture, fixture.Records,
            NullLogger<FormPdfActionService>.Instance);

        var prepared = (await service.PrepareAsync(Guid.NewGuid(), captured.Snapshot, Ct)).Value;

        Assert.Contains("es", prepared.Cultures);
        var outcome = await service.ExecuteAsync(captured.Record.Id, prepared with { Cultures = ["es"] }, false, Ct);
        Assert.Equal(FormPdfActionState.Completed, outcome.Value.State);
        Assert.Equal("es", fixture.RenderCulture);
        var reference = Assert.Single(fixture.Rendered!.Response.Record.References!);
        Assert.Equal("North branch", reference.Text);
        Assert.Equal("Ground floor", reference.Detail);
        Assert.Equal("Branches", reference.CatalogName);
    }

    private static Dictionary<string, string> ConstantTexts() => new() { ["es"] = "Recibimos {número}", ["en"] = "Received {number}" };

    private static (BusinessFormRecord Record, FormActionRecordSnapshot Snapshot) ChoiceCapture(FormActionFixture fixture,
        IReadOnlyList<FormRule> rules, bool translated, string? englishDetail = "Ground floor", bool spanish = true,
        string? spanishDetail = "Planta baja")
    {
        var captured = fixture.Capture(rules);
        var catalogId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var field = fixture.Schema.Questions[0] with { Type = "choice", MaxLength = null, Source = new(catalogId) };
        var texts = new Dictionary<string, object>();
        if (spanish) texts["es"] = new { text = "Sucursal norte", detail = spanishDetail, catalogName = "Sucursales" };
        if (translated) texts["en"] = new { text = "North branch", detail = englishDetail, catalogName = "Branches" };
        // The JSON path permits a behavioral red against today's model: the extra member is ignored until green adds it.
        var reference = FormDraftPolicy.Read<FormRecordReferenceRow>(FormDraftPolicy.Serialize(new
        {
            recordId = captured.Record.Id, fieldId = field.FieldId, catalogId, catalogRevision = 1, itemId,
            text = "North branch", detail = "Ground floor", catalogName = "Branches", translationsJson = FormDraftPolicy.Serialize(texts)
        }));
        var row = captured.Snapshot.Response.Record with
        {
            SchemaJson = FormDraftPolicy.Serialize(new FormVersionSchema(1, [field])),
            ValuesJson = FormDraftPolicy.Serialize(new[] { new { fieldId = field.FieldId, value = itemId } }),
            References = [reference]
        };
        return (captured.Record, captured.Snapshot with { Response = captured.Snapshot.Response with { Record = row } });
    }
}
