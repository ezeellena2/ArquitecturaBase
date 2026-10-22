using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormPdfPreviewIssuer(IFormPdfRenderer renderer, IAppName appName, TimeProvider timeProvider)
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.PdfPreview", typeof(FormPdfPreviewIssuer).Assembly);
    public async Task<FormPdfContentResponse> IssueAsync(FormRuleDefinitionRow definition, FormPdfDesign design, string requestedCulture, CancellationToken cancellationToken)
    {
        var fields = FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson).Fields;
        var schema = new FormVersionSchema(1, fields);
        var cultures = FormPdfDesignPolicy.CompleteCultures(design, definition.OriginalCulture, schema, definition.NameJson);
        var culture = cultures.Contains(requestedCulture) ? requestedCulture : definition.OriginalCulture;
        var example = Texts.GetString("answer", CultureInfo.GetCultureInfo(culture))!;
        var actorName = Texts.GetString("actor", CultureInfo.GetCultureInfo(culture))!;
        var values = fields.Where(field => field.Type is not ("photo" or "document")).Select(field => new { field.FieldId, Value = Example(field, example) }).Where(value => value.Value is not null).ToArray();
        var recordId = Guid.NewGuid();
        var photos = fields.Where(field => field.Type == "photo").Select(field =>
            new FormRecordFileRow(recordId, field.FieldId, Guid.NewGuid(), 0, "example.jpg", "image/jpeg", 1, false)).ToArray();
        var record = new FormRecordStoredRow(Guid.NewGuid(), recordId, 1, definition.Id, "DEMO", Guid.NewGuid(), 1, definition.NameJson,
            FormDraftPolicy.Serialize(schema), FormDraftPolicy.Serialize(values), Files: photos);
        var response = new FormResponseStoredRow(record, timeProvider.GetUtcNow().UtcDateTime, new(Guid.NewGuid(), actorName));
        var snapshot = new FormPdfActionSnapshotResponse(Guid.NewGuid(), null, 0, design, cultures, appName.Value, response.Actor, null);
        var bytes = await renderer.RenderAsync(new(snapshot, response), culture, true, cancellationToken);
        return new(new MemoryStream(bytes, false), "preview.pdf", culture);
    }
    private static object? Example(CanonicalFormField field, string text) => field.Type switch
    {
        "shortText" or "longText" => text, "decimal" => 12500.5m, "boolean" => true,
        "multiChoice" => field.Options?.Take(Math.Max(1, field.Selection!.MinSelected)).Select(option => option.ChoiceId).ToArray(),
        "date" => new DateOnly(2026, 10, 16), "choice" => field.Options is { Count: > 0 } options ? options[0].ChoiceId : null, _ => null,
    };
}
