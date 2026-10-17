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
    public async Task A_missing_translation_used_by_a_message_falls_back_the_whole_message()
    {
        var fixture = new FormActionFixture();
        var captured = ChoiceCapture(fixture, [new(Guid.NewGuid(), "Received", [], [FormActionFixture.Action()])], false);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var snapshot = FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson);
        var recipient = Assert.Single(snapshot.Recipients);
        Assert.Equal("es", recipient.Culture);
        Assert.Equal("Recibimos VIS-000001: Sucursal norte", recipient.Body);
    }

    [Fact]
    public async Task A_missing_translation_used_only_by_the_PDF_also_falls_back_an_earlier_message()
    {
        var fixture = new FormActionFixture();
        var action = FormActionFixture.Action() with { Text = ConstantTexts() };
        var captured = ChoiceCapture(fixture, [
            new(Guid.NewGuid(), "Received", [], [action]),
            new(Guid.NewGuid(), "Approved", [], [FormActionFixture.Action("GeneratePdf")])
        ], false);
        await fixture.Recorder.RecordAsync(captured.Record, captured.Snapshot, "Received", Ct);
        var recipient = Assert.Single(FormDraftPolicy.Read<FormActionSnapshot>(Assert.Single(fixture.Runs.Runs).SnapshotJson).Recipients);
        Assert.Equal("es", recipient.Culture);
        Assert.Equal("Recibimos VIS-000001", recipient.Body);
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
    public async Task An_English_original_cannot_offer_a_Spanish_display_fallback_as_its_only_label()
    {
        var fixture = new FormCatalogPreparationFixture();
        fixture.Version.SealPresentation(FormDraftPolicy.Serialize(FormCatalogPreparationFixture.Texts),
            FormDraftPolicy.Serialize(FormCatalogPreparationFixture.Texts), "en", 1);
        fixture.Items.Add(BusinessFormCatalogItem.Create(fixture.Catalog.Id, "NORTH", "Sucursal norte", null, null, null, 0));
        var result = await fixture.Policy.PrepareAsync(Guid.NewGuid(), "en", fixture.DefinitionId, fixture.Version.Id, true, Ct);
        Assert.False(result.CanSend);
        Assert.Empty(result.Catalogs);
        Assert.Equal(FormRecordErrors.CatalogOptionInvalid.Code, Assert.Single(result.Issues).Code);
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

    private static Dictionary<string, string> ConstantTexts() => new() { ["es"] = "Recibimos {número}", ["en"] = "Received {number}" };

    private static (BusinessFormRecord Record, FormActionRecordSnapshot Snapshot) ChoiceCapture(FormActionFixture fixture,
        IReadOnlyList<FormRule> rules, bool translated, string? englishDetail = "Ground floor")
    {
        var captured = fixture.Capture(rules);
        var catalogId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var field = fixture.Schema.Questions[0] with { Type = "choice", MaxLength = null, Source = new(catalogId) };
        var texts = new Dictionary<string, object> { ["es"] = new { text = "Sucursal norte", detail = "Planta baja", catalogName = "Sucursales" } };
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
