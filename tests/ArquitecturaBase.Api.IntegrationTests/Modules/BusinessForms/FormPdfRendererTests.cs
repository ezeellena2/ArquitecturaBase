using System.Text;
using System.Text.RegularExpressions;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPdfRendererTests
{

    [Fact]
    public async Task Mixed_emoji_in_fixed_names_answers_and_footer_have_self_contained_glyph_outlines()
    {
        var fixture = new FormPdfRendererFixture();
        var row = fixture.Snapshot.Response.Record;
        var schema = System.Text.Json.JsonSerializer.Deserialize<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormVersionSchema>(
            row.SchemaJson, System.Text.Json.JsonSerializerOptions.Web)!;
        var notesId = schema.Questions.Single(question => question.Type == "longText").FieldId;
        using var valuesDocument = System.Text.Json.JsonDocument.Parse(row.ValuesJson);
        var values = valuesDocument.RootElement.EnumerateArray().Select(value => new
        {
            fieldId = value.GetProperty("fieldId").GetGuid(),
            value = value.GetProperty("fieldId").GetGuid() == notesId
                ? (object)"Revisá áéíóú ñ 😀 👍🏽 🚧 📸 👩‍💻 🇦🇷 final"
                : value.GetProperty("value").Clone()
        }).ToArray();
        var fixedSnapshot = fixture.Snapshot.Snapshot;
        var request = fixture.Snapshot with
        {
            Snapshot = fixedSnapshot with
            {
                AppName = "Empresa 🚧",
                Design = fixedSnapshot.Design with
                {
                    Name = new Dictionary<string, string> { ["es"] = "Comprobante 📸", ["en"] = "Receipt 📸" },
                    Footer = new Dictionary<string, string> { ["es"] = "Fin 👍🏽", ["en"] = "End 👍🏽" }
                },
                Decision = fixedSnapshot.Decision! with { ActorName = "Ana 🇦🇷" }
            },
            Response = fixture.Snapshot.Response with
            {
                Actor = fixture.Snapshot.Response.Actor with { DisplayName = "Juan 👩‍💻" },
                Record = row with
                {
                    NameJson = "{\"es\":\"Visita 😀\",\"en\":\"Visit 😀\"}",
                    SchemaJson = System.Text.Json.JsonSerializer.Serialize(schema with
                    {
                        Questions = schema.Questions.Select(question => question.FieldId == notesId
                            ? question with { Label = new Dictionary<string, string> { ["es"] = "Observaciones 🚧", ["en"] = "Notes 🚧" } }
                            : question).ToArray()
                    }, System.Text.Json.JsonSerializerOptions.Web),
                    ValuesJson = System.Text.Json.JsonSerializer.Serialize(values)
                }
            }
        };

        var bytes = await fixture.Renderer.RenderAsync(request, "es", false, TestContext.Current.CancellationToken);

        await fixture.ExportAsync(bytes, "emoji-mixed-es");
        var structure = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/Subtype /Type3", structure, StringComparison.Ordinal);
        Assert.Contains("/CharProcs", structure, StringComparison.Ordinal);
        var outlines = Regex.Matches(structure, @"/CharProcs <<(.*?)>>", RegexOptions.Singleline)
            .Cast<Match>().SelectMany(dictionary => Regex.Matches(dictionary.Groups[1].Value, @"/g([0-9A-F]+)\s+\d+ 0 R").Cast<Match>())
            .Select(glyph => glyph.Groups[1].Value).Where(glyph => glyph != "0").Distinct(StringComparer.Ordinal);
        Assert.Equal(6, outlines.Count());
    }

    [Fact]
    public async Task A_preview_with_an_untranslated_form_name_uses_the_original_with_the_real_renderer_and_no_private_reads()
    {
        var fixture = new FormPdfRendererFixture();
        var row = fixture.Snapshot.Response.Record;
        var schema = System.Text.Json.JsonSerializer.Deserialize<ArquitecturaBase.Application.Modules.BusinessForms.Models.FormVersionSchema>(
            row.SchemaJson, System.Text.Json.JsonSerializerOptions.Web)!;
        var definition = new ArquitecturaBase.Application.Modules.BusinessForms.Models.FormRuleDefinitionRow(
            row.DefinitionId, "{\"es\":\"Visita original\"}", "es",
            ArquitecturaBase.Application.Modules.BusinessForms.Services.FormDraftPolicy.Serialize(
                new ArquitecturaBase.Application.Modules.BusinessForms.Models.FormQuestionSchema(1, schema.Questions)), []);
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 10, 17, 12, 0, 0, TimeSpan.Zero));
        var issuer = new ArquitecturaBase.Application.Modules.BusinessForms.Services.FormPdfPreviewIssuer(fixture.Renderer, fixture, clock);

        var result = await issuer.IssueAsync(definition, fixture.Snapshot.Snapshot.Design, "en", TestContext.Current.CancellationToken);

        await using var content = result.Content;
        using var output = new MemoryStream();
        await content.CopyToAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal("es", result.Culture);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(output.ToArray(), 0, 5));
        Assert.Empty(fixture.OpenedKeys);
        Assert.Equal(0, fixture.ReferenceReads);
        await fixture.ExportAsync(output.ToArray(), "preview-name-fallback-es");
    }

    [Fact]
    public void Wrapping_preserves_every_grapheme_in_long_words_and_respects_the_available_width()
    {
        using var face = FormPdfFonts.Load(false);
        using var emoji = FormPdfFonts.LoadEmoji();
        using var layout = new FormPdfTextLayout(face, emoji, 10);
        var text = "Revisá áéíóú ñ " + new string('W', 300) + " e\u0301 😀 final";
        var lines = layout.Wrap(text, 110);
        Assert.True(lines.Count > 3);
        Assert.Equal(text, string.Concat(lines));
        Assert.All(lines, line => Assert.InRange(layout.MeasureText(line), 0, 110.01f));
        Assert.All(lines, line => Assert.False(line.Length > 0 && char.IsLowSurrogate(line[0])));
    }

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task A_receipt_embeds_fonts_and_preserves_color_while_reading_only_photo_thumbnails(string culture)
    {
        var fixture = new FormPdfRendererFixture();
        var bytes = await fixture.Renderer.RenderAsync(fixture.Snapshot, culture, false, TestContext.Current.CancellationToken);
        var structure = Encoding.Latin1.GetString(bytes);
        Assert.Contains("/FontFile2", structure, StringComparison.Ordinal);
        Assert.True(Regex.Count(structure, @"/Type\s*/Page\b") == 1);
        Assert.Contains("/Subtype /Image", structure, StringComparison.Ordinal);
        var imageOffset = structure.IndexOf("/Subtype /Image", StringComparison.Ordinal);
        var contentOffset = structure.IndexOf("stream\n", imageOffset, StringComparison.Ordinal) + 7;
        var contentEnd = structure.IndexOf("\nendstream", contentOffset, StringComparison.Ordinal);
        using var photo = Assert.IsType<SkiaSharp.SKBitmap>(SkiaSharp.SKBitmap.Decode(bytes[contentOffset..contentEnd]));
        var background = photo.GetPixel(10, 10);
        Assert.True(background.Green > background.Red + 5, "The embedded photo must retain its green background.");
        Assert.Equal(new[] { fixture.ThumbnailKey }, fixture.OpenedKeys);
        Assert.Equal(1, fixture.ReferenceReads);
        await fixture.ExportAsync(bytes, "receipt-" + culture);
    }

    [Fact]
    public async Task Disabling_photos_keeps_the_file_metadata_and_never_opens_private_content()
    {
        var fixture = new FormPdfRendererFixture();
        var snapshot = fixture.Snapshot with
        {
            Snapshot = fixture.Snapshot.Snapshot with
            {
                Design = fixture.Snapshot.Snapshot.Design with { IncludePhotos = false }
            }
        };
        var bytes = await fixture.Renderer.RenderAsync(snapshot, "es", false, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("/Subtype /Image", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
        Assert.Empty(fixture.OpenedKeys);
        Assert.Equal(0, fixture.ReferenceReads);
        await fixture.ExportAsync(bytes, "photos-off-es");
    }

    [Fact]
    public async Task Long_responses_flow_to_multiple_A4_pages()
    {
        var fixture = new FormPdfRendererFixture();
        var bytes = await fixture.Renderer.RenderAsync(fixture.LongSnapshot(), "es", false, TestContext.Current.CancellationToken);
        var structure = Encoding.Latin1.GetString(bytes);
        Assert.True(Regex.Count(structure, @"/Type\s*/Page\b") >= 3);
        await fixture.ExportAsync(bytes, "long-es");
    }

    [Fact]
    public async Task Preview_does_not_read_real_files_and_issued_receipts_require_an_approval()
    {
        var fixture = new FormPdfRendererFixture();
        var pending = fixture.Snapshot with
        {
            Snapshot = fixture.Snapshot.Snapshot with { Decision = null }
        };
        await fixture.Renderer.RenderAsync(pending, "es", true, TestContext.Current.CancellationToken);
        Assert.Empty(fixture.OpenedKeys);
        Assert.Equal(0, fixture.ReferenceReads);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Renderer.RenderAsync(pending, "es", false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Explicit_paragraph_breaks_keep_the_blank_line_between_answers()
    {
        using var face = FormPdfFonts.Load(false);
        using var emoji = FormPdfFonts.LoadEmoji();
        using var layout = new FormPdfTextLayout(face, emoji, 10);
        Assert.Equal(new[] { "Primera", "", "Última" }, layout.Wrap("Primera\r\n\r\nÚltima", 200));
    }

    [Fact]
    public async Task A_photo_from_another_response_is_rejected_before_opening_private_storage()
    {
        var fixture = new FormPdfRendererFixture();
        var record = fixture.Snapshot.Response.Record with { RecordId = Guid.NewGuid() };
        var snapshot = fixture.Snapshot with
        {
            Response = fixture.Snapshot.Response with { Record = record }
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Renderer.RenderAsync(snapshot, "es", false, TestContext.Current.CancellationToken));
        Assert.Empty(fixture.OpenedKeys);
    }
}
