using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Files;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;
using SkiaSharp;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormPdfRendererFixture : IFormFileReader, IStoredFileReader, IPrivateFileStore, IAppName
{
    private readonly Guid photoId = Guid.NewGuid();
    private readonly Guid recordId = Guid.NewGuid();
    private readonly Guid notesId = Guid.NewGuid();
    private readonly byte[] photo;

    public FormPdfRenderRequest Snapshot { get; }
    public SkiaFormPdfRenderer Renderer { get; }
    public List<string> OpenedKeys { get; } = [];
    public int ReferenceReads { get; private set; }
    public string ThumbnailKey { get; } = "renderer/private-thumb.jpg";
    public byte[] PhotoContent => photo.ToArray();
    public bool IsAvailable => true;
    public string Value => "Technical preview";

    public FormPdfRendererFixture()
    {
        var author = Guid.NewGuid();
        var catalog = Guid.NewGuid();
        var item = Guid.NewGuid();
        var choice = Field("choice", "Cliente", "Customer") with { Source = new(catalog) };
        var date = Field("date", "Fecha de la visita", "Visit date");
        var amount = Field("decimal", "Importe cobrado", "Amount collected");
        var confirmed = Field("boolean", "Entrega pendiente", "Delivery pending");
        var notes = Field("longText", "Observaciones", "Notes") with { FieldId = notesId, MaxLength = 10000 };
        var photos = Field("photo", "Fotos de la obra", "Site photos") with { Files = new(0, 10, "cameraOrGallery") };
        var documents = Field("document", "Remito firmado", "Signed delivery slip") with { Files = new(0, 10) };
        var empty = Field("shortText", "Observación opcional", "Optional note");
        var questions = new[] { choice, date, amount, confirmed, notes, photos, documents, empty };
        var values = new (Guid Id, object Value)[]
        {
            (choice.FieldId, item),
            (date.FieldId, "2026-10-08"),
            (amount.FieldId, 125500.0123m),
            (confirmed.FieldId, false),
            (notes.FieldId, "Revisé la entrega y quedó todo bien. Se conservaron los acentos: á, é, í, ó, ú y ñ.")
        };
        var captured = new FormRecordStoredRow(
            Guid.NewGuid(), recordId, 24, Guid.NewGuid(), "VIS", Guid.NewGuid(), 3,
            JsonSerializer.Serialize(new { es = "Registrar visita", en = "Record visit" }),
            JsonSerializer.Serialize(new FormVersionSchema(1, questions), JsonSerializerOptions.Web),
            JsonSerializer.Serialize(values.Select(value => new { fieldId = value.Id, value = value.Value })),
            [new(recordId, choice.FieldId, catalog, 1, item, "Pérez Hnos.", null, "Clientes")],
            [
                new(recordId, photos.FieldId, photoId, 0, "obra-éxito.jpg", "image/jpeg", 12345, true),
                new(recordId, documents.FieldId, Guid.NewGuid(), 0, "remito-firmado-éxito.pdf", "application/pdf", 1234567, false)
            ]);
        var at = new DateTime(2026, 10, 8, 12, 30, 0, DateTimeKind.Utc);
        var response = new FormResponseStoredRow(captured, at, new(author, "Juan Gómez"));
        var actionRecord = new FormActionRecordSnapshot(response, "es",
            new(Guid.NewGuid(), Guid.NewGuid(), "Ana Pérez", at.AddHours(2), "Approved", null));
        var design = new FormPdfDesign(
            new Dictionary<string, string> { ["es"] = "Comprobante de visita", ["en"] = "Visit receipt" },
            questions.Reverse().Select(question => question.FieldId).ToArray(),
            true,
            new Dictionary<string, string>
            {
                ["es"] = "Este comprobante no reemplaza la factura.",
                ["en"] = "This receipt does not replace the invoice."
            },
            true);
        Snapshot = new(new(Guid.NewGuid(), Guid.NewGuid(), 1, design, ["es", "en"], "Arquitectura Base", response.Actor, actionRecord.Decision), response);
        Renderer = new(this, this, this);

        using var bitmap = new SKBitmap(180, 120);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColor.Parse("#DDE9D4"));
            using var wall = new SKPaint { Color = SKColor.Parse("#D1BEA5") };
            canvas.DrawRect(25, 30, 130, 80, wall);
            using var beam = new SKPaint { Color = SKColor.Parse("#526258") };
            for (var x = 35; x < 155; x += 20)
                canvas.DrawRect(x, 36, 7, 74, beam);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        photo = data.ToArray();
    }

    public FormPdfRenderRequest LongSnapshot()
    {
        var notes = string.Join("\n", Enumerable.Range(1, 65).Select(index =>
            "Observación " + index.ToString("D3", CultureInfo.InvariantCulture) +
            ": Revisé las instalaciones y registré cada detalle para conservar la respuesta completa."));
        using var document = JsonDocument.Parse(Snapshot.Response.Record.ValuesJson);
        var values = document.RootElement.EnumerateArray().Select(value => new
        {
            fieldId = value.GetProperty("fieldId").GetGuid(),
            value = value.GetProperty("fieldId").GetGuid() == notesId
                ? (object)(notes + "\nÚLTIMA RESPUESTA CONSERVADA")
                : value.GetProperty("value").Clone()
        }).ToArray();
        var record = Snapshot.Response.Record with { ValuesJson = JsonSerializer.Serialize(values) };
        return Snapshot with { Response = Snapshot.Response with { Record = record } };
    }

    public async Task ExportAsync(byte[] content, string name)
    {
        var output = Environment.GetEnvironmentVariable("FORM_PDF_RENDERER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;
        Directory.CreateDirectory(output);
        if (name == "receipt-es")
            await File.WriteAllBytesAsync(Path.Combine(output, "source-photo.jpg"), photo, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".pdf"), content, TestContext.Current.CancellationToken);
    }

    private static CanonicalFormField Field(string type, string es, string en) =>
        new(Guid.NewGuid(), type + Guid.NewGuid().ToString("N"), type,
            new Dictionary<string, string> { ["es"] = es, ["en"] = en },
            null, false, type is "shortText" or "longText" ? 600 : null, null);

    public Task<FormFileAccessRow?> FindAsync(Guid id, Guid file, CancellationToken ct)
    {
        ReferenceReads++;
        return Task.FromResult<FormFileAccessRow?>(id == recordId && file == photoId ? new(file, true) : null);
    }

    Task<StoredFileRow?> IStoredFileReader.FindAsync(Guid id, CancellationToken ct) =>
        Task.FromResult<StoredFileRow?>(id == photoId
            ? new(id, Guid.NewGuid(), "new-name-is-not-used.jpg", "image/jpeg", photo.Length, "hash",
                "private-original-must-not-be-read", ThumbnailKey)
            : null);

    public Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> keys, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct)
    {
        OpenedKeys.Add(key);
        return Task.FromResult<Stream?>(key == ThumbnailKey ? new MemoryStream(photo, false) : null);
    }

    public Task WriteAsync(string key, Stream content, string type, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> WriteOnceAsync(string key, Stream content, string type, CancellationToken ct) => throw new NotSupportedException();
    public Task DeleteAsync(string key, CancellationToken ct) => throw new NotSupportedException();
    public IAsyncEnumerable<PrivateFileObjectRow> ListAsync(DateTime olderThanUtc, CancellationToken ct) => throw new NotSupportedException();
}
