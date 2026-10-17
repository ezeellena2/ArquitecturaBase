using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Files;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using SkiaSharp;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

internal sealed class SkiaFormPdfRenderer(IFormFileReader references, IStoredFileReader files, IPrivateFileStore store) : IFormPdfRenderer
{
    public async Task<byte[]> RenderAsync(FormPdfRenderRequest request, string culture, bool preview, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = request.Snapshot;
        var response = request.Response;
        if (culture is not ("es" or "en") || !snapshot.Cultures.Contains(culture) || !snapshot.Design.Name.ContainsKey(culture))
            throw new InvalidOperationException("The PDF culture is not complete in its fixed snapshot.");
        if (!preview && snapshot.Decision?.Decision != "Approved")
            throw new InvalidOperationException("An issued form PDF requires its confirmed approval.");
        using var regular = FormPdfFonts.Load(false);
        using var bold = FormPdfFonts.Load(true);
        using var emoji = FormPdfFonts.LoadEmoji();
        using var layout = new DocumentLayout(request, culture, preview, regular, bold, emoji, cancellationToken);
        var record = response.Record;
        var schema = JsonSerializer.Deserialize<FormVersionSchema>(record.SchemaJson, JsonSerializerOptions.Web)
            ?? throw new InvalidOperationException("The PDF response schema is invalid.");
        using var values = JsonDocument.Parse(record.ValuesJson);
        var answers = values.RootElement.EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value"));
        var selected = snapshot.Design.QuestionIds.ToHashSet();
        foreach (var question in schema.Questions.Where(question => selected.Contains(question.FieldId)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var label = question.Label[culture];
            var attached = (record.Files ?? []).Where(file => file.FieldId == question.FieldId).OrderBy(file => file.Position).ToArray();
            if (question.Type == "photo" && snapshot.Design.IncludePhotos && attached.Length > 0)
            {
                layout.WriteField(label, "");
                for (var index = 0; index < attached.Length; index++)
                {
                    if (preview)
                    {
                        layout.WritePhoto(null, index, attached.Length);
                        continue;
                    }
                    var reference = await references.FindAsync(record.RecordId, attached[index].FileId, cancellationToken);
                    if (reference?.IsPhoto != true)
                        throw new InvalidOperationException("The PDF photo does not belong to its response.");
                    var file = await files.FindAsync(attached[index].FileId, cancellationToken);
                    if (file?.ThumbnailKey is not string key)
                        throw new FileNotFoundException("The private photo thumbnail is missing.");
                    await using var content = await store.OpenReadAsync(key, cancellationToken)
                        ?? throw new FileNotFoundException("The private photo thumbnail is missing.");
                    using var bitmap = SKBitmap.Decode(content) ?? throw new InvalidOperationException("The private photo thumbnail is invalid.");
                    using var image = SKImage.FromBitmap(bitmap);
                    layout.WritePhoto(image, index, attached.Length);
                }
                layout.EndPhotos();
            }
            else if (question.Type == "photo")
                layout.WriteField(label, attached.Length == 0 ? FormPdfTexts.Get("NoFiles", culture) : FormPdfTexts.Format(attached.Length == 1 ? "PhotoCountOne" : "PhotoCountMany", culture, attached.Length));
            else if (question.Type == "document")
                layout.WriteField(label, attached.Length == 0 ? FormPdfTexts.Get("NoFiles", culture) : string.Join("\n", attached.Select(file => file.Name)));
            else
                layout.WriteField(label, answers.TryGetValue(question.FieldId, out var answer) ? Value(question, answer, culture, record) : FormPdfTexts.Get("NoAnswer", culture));
        }
        layout.WriteApproval();
        var footer = snapshot.Design.Footer?.GetValueOrDefault(culture);
        if (!string.IsNullOrEmpty(footer))
            layout.WriteFooter(footer);
        return layout.Finish();
    }

    private static string Value(CanonicalFormField question, JsonElement answer, string culture, FormRecordStoredRow record)
    {
        var locale = CultureInfo.GetCultureInfo(culture == "es" ? "es-AR" : "en-US");
        return question.Type switch
        {
            "shortText" or "longText" => answer.GetString() ?? FormPdfTexts.Get("NoAnswer", culture),
            "decimal" => answer.GetDecimal().ToString("#,0.############################", locale),
            "date" => DateOnly.ParseExact(answer.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("d MMM yyyy", locale),
            "boolean" => FormPdfTexts.Get(answer.GetBoolean() ? "Yes" : "No", culture),
            "choice" when question.Source?.Catalog is not null => (record.References ?? []).Single(reference => reference.FieldId == question.FieldId && reference.ItemId == answer.GetGuid()).Text,
            "choice" => question.Options!.Single(option => option.ChoiceId == answer.GetGuid()).Label[culture],
            _ => throw new InvalidOperationException("The PDF contains an unsupported response value."),
        };
    }
    private sealed class DocumentLayout : IDisposable
    {
        private const float Width = 595.276f;
        private const float Height = 841.89f;
        private const float Margin = 42;
        private const float RightX = 224;
        private const float Bottom = Height - 58;
        private const float LineHeight = 15;
        private const float PhotoWidth = 86;
        private const float PhotoHeight = 72;
        private readonly FormPdfActionSnapshotResponse snapshot;
        private readonly FormResponseStoredRow response;
        private readonly string culture;
        private readonly bool preview;
        private readonly CancellationToken cancellationToken;
        private readonly FormPdfTextLayout body;
        private readonly FormPdfTextLayout small;
        private readonly FormPdfTextLayout heading;
        private readonly FormPdfTextLayout title;

        private readonly SKPaint ink = new()
        {
            Color = SKColor.Parse("#203227"),
            IsAntialias = true
        };

        private readonly SKPaint muted = new()
        {
            Color = SKColor.Parse("#526258"),
            IsAntialias = true
        };

        private readonly SKPaint line = new()
        {
            Color = SKColor.Parse("#DFE8D9"),
            StrokeWidth = 0.7f,
            IsAntialias = true
        };

        private readonly SKPaint accent = new()
        {
            Color = SKColor.Parse("#285A3D"),
            StrokeWidth = 1.4f,
            IsAntialias = true
        };

        private readonly SKPaint sample = new()
        {
            Color = SKColor.Parse("#F4F6F0"),
            IsAntialias = true
        };
        private readonly List<SKPicture> pages = [];
        private SKPictureRecorder? recorder;
        private SKCanvas canvas = null!;
        private float y;
        private float photoTop;
        private bool photosOpen;

        public DocumentLayout(FormPdfRenderRequest request, string culture, bool preview, SKTypeface regular, SKTypeface bold, SKTypeface emoji, CancellationToken cancellationToken)
        {
            snapshot = request.Snapshot;
            response = request.Response;
            this.culture = culture;
            this.preview = preview;
            this.cancellationToken = cancellationToken;
            body = new(regular, emoji, 10);
            small = new(regular, emoji, 8);
            heading = new(bold, emoji, 11);
            title = new(bold, emoji, 18);
            StartPage();
        }

        private void StartPage()
        {
            recorder = new();
            canvas = recorder.BeginRecording(SKRect.Create(Width, Height));
            canvas.Clear(SKColors.White);
            y = Margin;
            var app = heading.Wrap(snapshot.AppName, 265);
            var design = small.Wrap(snapshot.Design.Name[culture], Width - Margin * 2 - 280);
            for (var index = 0; index < Math.Max(app.Count, design.Count); index++)
            {
                if (index < app.Count) heading.DrawText(canvas, app[index], Margin, y + 11, ink);
                if (index < design.Count) small.DrawText(canvas, design[index], Margin + 280, y + 11, muted);
                y += 15;
            }
            canvas.DrawLine(Margin, y + 5, Width - Margin, y + 5, accent);
            y += 30;
            var record = response.Record;
            var number = record.Prefix + "-" + record.Number.ToString("D6", CultureInfo.InvariantCulture);
            title.DrawText(canvas, number, Margin, y, ink);
            y += 20;
            var name = JsonSerializer.Deserialize<Dictionary<string, string>>(record.NameJson)![culture];
            foreach (var text in body.Wrap(name, Width - Margin * 2))
            {
                body.DrawText(canvas, text, Margin, y, ink);
                y += 14;
            }
            var author = response.Actor.DisplayName ?? FormPdfTexts.Get("UnknownActor", culture);
            var received = FormPdfTexts.Format("Received", culture, Date(response.SubmittedAtUtc), author);
            foreach (var text in small.Wrap(received, Width - Margin * 2))
            {
                small.DrawText(canvas, text, Margin, y, muted);
                y += 12;
            }
            if (preview)
            {
                small.DrawText(canvas, FormPdfTexts.Get("Preview", culture), Margin, y, muted);
                y += 13;
            }
            y += 12;
        }

        private bool Ensure(float height)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (y + height <= Bottom) return false;
            EndPage();
            StartPage();
            return true;
        }

        private void EndPage()
        {
            var current = recorder ?? throw new InvalidOperationException("The PDF page is not open.");
            pages.Add(current.EndRecording());
            current.Dispose();
            recorder = null;
        }

        public void WriteField(string label, string value)
        {
            Ensure(24);
            canvas.DrawLine(Margin, y - 5, Width - Margin, y - 5, line);
            var labels = body.Wrap(label, RightX - Margin - 18);
            var values = body.Wrap(value, Width - Margin - RightX);
            for (var index = 0; index < Math.Max(labels.Count, values.Count); index++)
            {
                var continued = Ensure(LineHeight);
                if (continued && index >= labels.Count) small.DrawText(canvas, FormPdfTexts.Get("Continued", culture), Margin, y + 10, muted);
                if (index < labels.Count) body.DrawText(canvas, labels[index], Margin, y + 10, muted);
                if (index < values.Count) body.DrawText(canvas, values[index], RightX, y + 10, ink);
                y += LineHeight;
            }
            y += 10;
        }

        public void WritePhoto(SKImage? image, int index, int count)
        {
            const int columns = 3;
            if (index % columns == 0)
            {
                Ensure(PhotoHeight + 15);
                photoTop = y;
                photosOpen = true;
            }
            var x = RightX + (index % columns) * (PhotoWidth + 10);
            if (image is null)
            {
                canvas.DrawRect(x, photoTop, PhotoWidth, PhotoHeight, sample);
                canvas.DrawLine(x + 8, photoTop + PhotoHeight - 12, x + 32, photoTop + 18, line);
                canvas.DrawLine(x + 32, photoTop + 18, x + PhotoWidth - 8, photoTop + PhotoHeight - 12, line);
                var words = small.Wrap(FormPdfTexts.Get("ExamplePhoto", culture), PhotoWidth - 10);
                for (var part = 0; part < words.Count; part++) small.DrawText(canvas, words[part], x + 5, photoTop + PhotoHeight + 10 + part * 10, muted);
            }
            else
            {
                var ratio = Math.Min(PhotoWidth / image.Width, PhotoHeight / image.Height);
                var width = image.Width * ratio;
                var height = image.Height * ratio;
                canvas.DrawImage(image, new SKRect(x, photoTop, x + width, photoTop + height), new SKSamplingOptions(SKFilterMode.Linear), ink);
            }
            if (index % columns == columns - 1 || index == count - 1) y = photoTop + PhotoHeight + 24;
        }

        public void EndPhotos()
        {
            if (photosOpen)
            {
                y = Math.Max(y, photoTop + PhotoHeight + 24);
                photosOpen = false;
            }
        }

        public void WriteApproval()
        {
            if (!snapshot.Design.IncludeApproval) return;
            y += 14;
            Ensure(50);
            canvas.DrawLine(Margin, y - 5, Width - Margin, y - 5, line);
            small.DrawText(canvas, FormPdfTexts.Get("ApprovalLabel", culture), Margin, y + 10, muted);
            y += 23;
            var text = preview ? FormPdfTexts.Get("ApprovalPlaceholder", culture) : FormPdfTexts.Format("Approval", culture,
                snapshot.Decision!.ActorName ?? FormPdfTexts.Get("UnknownActor", culture), Date(snapshot.Decision.DecidedAtUtc));
            foreach (var part in body.Wrap(text, Width - Margin * 2))
            {
                Ensure(LineHeight);
                body.DrawText(canvas, part, Margin, y, ink);
                y += LineHeight;
            }
            y += 8;
        }

        public void WriteFooter(string text)
        {
            y += 18;
            foreach (var part in small.Wrap(text, Width - Margin * 2))
            {
                Ensure(12);
                small.DrawText(canvas, part, Margin, y, muted);
                y += 12;
            }
        }
        private string Date(DateTime value) => value.ToString("d MMM yyyy HH:mm", CultureInfo.GetCultureInfo(culture == "es" ? "es-AR" : "en-US"));

        public byte[] Finish()
        {
            EndPage();
            using var output = new MemoryStream();
            using var document = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
            {
                Title = snapshot.Design.Name[culture],
                Creator = snapshot.AppName,
                EncodingQuality = 90
            });
            for (var index = 0; index < pages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.BeginPage(Width, Height);
                page.DrawPicture(pages[index]);
                var version = FormPdfTexts.Format("Version", culture, response.Record.VersionNumber);
                var pageNumber = FormPdfTexts.Format("Page", culture, index + 1, pages.Count);
                small.DrawText(page, version, Margin, Height - 30, muted);
                small.DrawText(page, pageNumber, Width - Margin - small.MeasureText(pageNumber), Height - 30, muted);
                document.EndPage();
            }
            document.Close();
            return output.ToArray();
        }

        public void Dispose()
        {
            foreach (var page in pages) page.Dispose();
            recorder?.Dispose();
            body.Dispose();
            small.Dispose();
            heading.Dispose();
            title.Dispose();
            ink.Dispose();
            muted.Dispose();
            line.Dispose();
            accent.Dispose();
            sample.Dispose();
        }
    }
}
