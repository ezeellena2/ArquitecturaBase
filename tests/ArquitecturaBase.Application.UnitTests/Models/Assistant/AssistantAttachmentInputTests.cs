using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.UnitTests.Models.Assistant;

// Documents (task 3): a read document goes with an interpretation within its own budget, measured on the JSON the engine writes, which
// escapes each character outside ASCII. The reading is cut when the attachment is built, always the same way, and says so.
public sealed class AssistantAttachmentInputTests
{
    private static readonly DateTime ExpiresAtUtc = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
    // The engine's options: the web defaults, which escape each character outside ASCII.
    private static readonly JsonSerializerOptions EngineOptions = new(JsonSerializerDefaults.Web);

    private static StructuredDocument Document(string text, bool truncated = false, string fileName = "comprobante.pdf") =>
        new(fileName, StructuredDocumentKind.Pdf, "application/pdf", 4096, new string('d', 64), 2,
            new StructuredDocumentReading(StructuredDocumentReadability.Partial, StructuredDocumentProblem.Blurry, "es", text, truncated));

    private static int Bytes(StructuredDocument document) =>
        JsonSerializer.SerializeToUtf8Bytes(document, EngineOptions).Length;

    [Fact]
    public void A_document_that_fits_keeps_its_reading_as_it_came()
    {
        var document = Document("Transferencia ficticia", truncated: true);

        var fitted = AssistantAttachmentInput.Fit(document, AssistantAttachmentInput.DocumentsMaximumBytes);

        Assert.Equal(document, fitted);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("á")]
    [InlineData("\"")]
    [InlineData("\n")]
    [InlineData("😀")]
    public void A_long_reading_is_cut_to_the_longest_beginning_that_fits_its_escaped_bytes_and_is_marked_truncated(string unit)
    {
        const string start = "Dato ";
        var text = start + string.Concat(Enumerable.Repeat(unit, 20_000 / unit.Length));
        var document = Document(text);

        var fitted = AssistantAttachmentInput.Fit(document, AssistantAttachmentInput.DocumentsMaximumBytes);

        var reading = fitted.Reading!;
        Assert.True(reading.Truncated);
        Assert.StartsWith(reading.Text, text, StringComparison.Ordinal);
        Assert.Equal(0, (reading.Text.Length - start.Length) % unit.Length);
        Assert.Equal(AssistantAttachmentInput.SerializedBytes(fitted), Bytes(fitted));
        Assert.True(Bytes(fitted) <= AssistantAttachmentInput.DocumentsMaximumBytes);
        // The longest one: one more character would not fit.
        var longer = fitted with { Reading = reading with { Text = text[..(reading.Text.Length + unit.Length)] } };
        Assert.True(Bytes(longer) > AssistantAttachmentInput.DocumentsMaximumBytes);
        Assert.Equal(document with { Reading = reading }, fitted);
    }

    // A reading that begins with blank lines would keep only blanks in a small share: the cut starts at its first visible character.
    [Fact]
    public void A_reading_that_does_not_fit_is_cut_from_its_first_visible_character()
    {
        var document = Document(new string('\n', 3_000) + "Transferencia ficticia de $ 1.500");

        var fitted = AssistantAttachmentInput.Fit(document, 2048);

        Assert.StartsWith("Transferencia ficticia", fitted.Reading!.Text, StringComparison.Ordinal);
        Assert.True(fitted.Reading.Truncated);
        Assert.True(Bytes(fitted) <= 2048);
        Assert.Equal(document, AssistantAttachmentInput.Fit(document, AssistantAttachmentInput.DocumentsMaximumBytes));
    }

    [Fact]
    public void More_documents_than_the_ones_that_may_go_after_the_first_fail_before_building()
    {
        var more = Enumerable.Range(0, AssistantAttachmentInput.MaximumMoreDocuments + 1).Select(index => Document("Otro", fileName: $"otro-{index}.pdf")).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AssistantAttachmentInput.ForDocument("fixture/documents/primero.pdf", "Primero.pdf", ExpiresAtUtc, Document("Primero"), more));
    }

    // A name too long for its share leaves no room for the text: that document is left out and the others share what it left.
    [Fact]
    public void A_document_after_the_first_that_cannot_keep_any_text_is_left_out()
    {
        var text = new string('x', 20_000);
        StructuredDocument[] more = [Document(text, fileName: new string('ñ', 400) + ".pdf"), Document(text, fileName: "b.pdf"),
            Document(text, fileName: "c.pdf"), Document(text, fileName: "e.pdf")];

        var attachment = AssistantAttachmentInput.ForDocument("fixture/documents/primero.pdf", "Primero.pdf", ExpiresAtUtc, Document(text), more);

        Assert.Equal(["b.pdf", "c.pdf", "e.pdf"], attachment.MoreDocuments!.Select(document => document.FileName));
        Assert.All(attachment.MoreDocuments!.Select((document, index) => (document, index)), entry =>
            Assert.True(AssistantAttachmentInput.Admits(entry.document, AssistantAttachmentInput.DocumentBytes(entry.index + 1, 4))));
        Assert.True(AssistantAttachmentInput.Admits(attachment.Document!, AssistantAttachmentInput.DocumentBytes(0, 4)));
    }

    [Fact]
    public void A_first_document_that_can_never_be_admitted_fails_before_building()
    {
        Assert.Throws<ArgumentException>(() => AssistantAttachmentInput.ForDocument("fixture/documents/primero.pdf", "Primero.pdf", ExpiresAtUtc,
            Document("Primero", fileName: new string('n', 300) + ".pdf")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("carpeta/comprobante.pdf")]
    [InlineData("carpeta\\comprobante.pdf")]
    [InlineData("compro\nbante.pdf")]
    public void A_name_with_folders_controls_or_nothing_is_not_safe(string? name)
    {
        Assert.False(AssistantAttachmentInput.IsSafeName(name));
        Assert.False(AssistantAttachmentInput.IsSafeName(new string('n', 256)));
        Assert.True(AssistantAttachmentInput.IsSafeName("Comprobante ñandú 😀.pdf"));
    }

    // Sealed as a number in the profile: the values never change.
    [Fact]
    public void The_kinds_of_file_keep_the_numbers_they_are_sealed_with()
    {
        Assert.Equal([0, 1, 2], Enum.GetValues<AssistantAttachmentKind>().Select(kind => (int)kind));
    }

    [Fact]
    public void The_cut_is_the_same_every_time()
    {
        var document = Document(string.Concat(Enumerable.Range(0, 3_000).Select(index => $"Renglón {index} · ")));

        Assert.Equal(AssistantAttachmentInput.Fit(document, 4096), AssistantAttachmentInput.Fit(document, 4096));
    }

    [Fact]
    public void The_first_document_takes_half_of_the_budget_and_the_others_share_the_other_half()
    {
        Assert.Equal(16 * 1024, AssistantAttachmentInput.DocumentBytes(0, 1));
        Assert.Equal([8192, 8192], new[] { AssistantAttachmentInput.DocumentBytes(0, 2), AssistantAttachmentInput.DocumentBytes(1, 2) });
        Assert.Equal([8192, 2048, 2048, 2048, 2048], Enumerable.Range(0, 5).Select(index => AssistantAttachmentInput.DocumentBytes(index, 5)));

        var text = new string('x', 20_000);
        var attachment = AssistantAttachmentInput.ForDocument("fixture/documents/primero.pdf", "Primero.pdf", ExpiresAtUtc, Document(text),
            [.. Enumerable.Range(1, AssistantAttachmentInput.MaximumMoreDocuments).Select(index => Document(text, fileName: $"otro-{index}.pdf"))]);

        Assert.Equal(AssistantAttachmentKind.Document, attachment.Kind);
        StructuredDocument[] documents = [attachment.Document!, .. attachment.MoreDocuments!];
        Assert.Equal(5, documents.Length);
        Assert.All(documents.Select((document, index) => (document, index)), entry =>
        {
            Assert.True(entry.document.Reading!.Truncated);
            Assert.InRange(Bytes(entry.document), AssistantAttachmentInput.DocumentBytes(entry.index, 5) - 8, AssistantAttachmentInput.DocumentBytes(entry.index, 5));
        });
    }

    [Fact]
    public void The_copy_of_a_document_is_described_by_its_media_type_size_and_hash_and_no_more_documents_is_none()
    {
        var document = Document("Transferencia ficticia");

        var attachment = AssistantAttachmentInput.ForDocument("fixture/documents/comprobante.pdf", "Comprobante.pdf", ExpiresAtUtc, document, []);

        Assert.Equal(("fixture/documents/comprobante.pdf", "Comprobante.pdf", "application/pdf", 4096L, new string('d', 64), ExpiresAtUtc),
            (attachment.StorageKey, attachment.Name, attachment.ContentType, attachment.Size, attachment.Sha256, attachment.ExpiresAtUtc));
        Assert.Equal((document, (StructuredSpreadsheet?)null, (IReadOnlyList<StructuredDocument>?)null),
            (attachment.Document, attachment.Spreadsheet, attachment.MoreDocuments));
        Assert.DoesNotContain("spreadsheet", JsonSerializer.Serialize(attachment, JsonSerializerOptions.Web), StringComparison.Ordinal);
        Assert.DoesNotContain("moreDocuments", JsonSerializer.Serialize(attachment, JsonSerializerOptions.Web), StringComparison.Ordinal);
    }

    // A turn sealed with a spreadsheet before documents existed: the same JSON reads into the same attachment and writes the same bytes.
    [Fact]
    public void A_spreadsheet_sealed_before_documents_reads_and_writes_the_same_bytes()
    {
        const string sealedJson = """{"storageKey":"ai/assistant/attachments/0193a5f2c4d87e6fb1a2c3d4e5f60718.xlsx","name":"Clientes ficticios.xlsx","contentType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","size":2048,"sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","expiresAtUtc":"2026-12-11T12:00:00Z","spreadsheet":{"fileName":"clientes.xlsx","kind":0,"mediaType":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet","size":2048,"sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc","sheetName":"Clientes ficticios","rowCount":12,"columnCount":3,"rows":[{"number":1,"cells":{"A":"C\u00F3digo","B":"Nombre"}}]}}""";

        var attachment = JsonSerializer.Deserialize<AssistantAttachmentInput>(sealedJson, JsonSerializerOptions.Web)!;

        Assert.Equal(AssistantAttachmentKind.Spreadsheet, attachment.Kind);
        Assert.Equal(("Clientes ficticios", "Código"), (attachment.Spreadsheet!.SheetName, attachment.Spreadsheet.Rows[0].Cells["A"]));
        Assert.Null(attachment.Document);
        Assert.Equal(sealedJson, JsonSerializer.Serialize(attachment, JsonSerializerOptions.Web));
    }
}
