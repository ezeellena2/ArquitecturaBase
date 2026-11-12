using System.Globalization;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormExportService(IFormResponseReader reader, IFormWorkbookWriter writer,
    FormRecordAccessGuard access, IRequestValidator validator, TimeProvider timeProvider, ILogger<FormExportService> logger,
    FormExportResponsePolicy responses, IFormRecordResultReader resultReader) : IFormExportService
{
    internal const int MaximumRecords = 10000;
    // A row per item fits the budget up to here: 100,000 rows of 13 columns took 11 s and 0.4 GB to write on a loaded machine.
    internal const int MaximumRows = 100_000;
    private static readonly FormWorkbookCell Missing = new("text", "—");
    private static readonly FormWorkbookCell Empty = new("text", null);

    public Task<Result<FormExportResponse>> ExportAsync(Guid id, FormExportRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormExportResponse>>(logger, "ExportFormResponses", async () =>
        {
            var actor = await access.CheckExportAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            if (await validator.ValidateAsync(request.Filter, cancellationToken) is { } invalidFilter) return invalidFilter;
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(30), timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, budget.Token);
            try
            {
                var ct = linked.Token;
                var maximum = await reader.FindLatestNumberAsync(id, ct);
                if (maximum is null) return FormRecordErrors.NotFound;
                var definition = await reader.FindDefinitionAsync(id, ct);
                if (definition is null) return FormRecordErrors.NotFound;
                var valid = FormRecordFilterPolicy.Validate(request.Filter, definition); if (valid.IsFailure) return valid.Error;
                var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
                var availableColumns = FormResponsePresentationPolicy.Columns(definition, culture);
                if (request.Columns?.Any(fieldId => !availableColumns.Any(column => column.FieldId == fieldId)) == true) return FormRecordErrors.FilterInvalid;
                if (request.CatalogFields?.Any(field => !availableColumns.SelectMany(column => column.Questions ?? [column]).Any(column => column.FieldId == field!.QuestionId
                    && column.Fields?.Any(extra => extra.FieldId == field.FieldId) == true)) == true) return FormRecordErrors.FilterInvalid;
                var selectedColumns = SelectColumns(availableColumns, request, culture);
                // XL1-A: an exported block goes in the same sheet, with «Ítem» and its questions in its place and a row per item.
                var blocks = selectedColumns.Where(column => column.Part == ExportPart.BlockQuestion).Select(column => column.BlockId!.Value).Distinct().ToArray();
                var hasItems = selectedColumns.Any(column => column.Part == ExportPart.Item);
                var filter = request.Filter with { UpToNumber = maximum, DisplayCulture = culture };
                var ids = await reader.ListExportIdsAsync(definition, filter, MaximumRecords + 1, ct);
                if (ids.Count > MaximumRecords) return FormRecordErrors.ExportTooLarge;
                var zone = await responses.ZoneAsync(actor.Value, ct);
                // With «Incluir las anuladas» a column says which ones were voided; otherwise there are none.
                var voided = request.Filter.IncludeVoided && request.Filter.State is null;
                var headers = FormExportResponsePolicy.Headers(voided, culture);
                headers.AddRange(selectedColumns.Select(column => column.Header));
                // RE1-A: the data of the results go after the questions, a column each; with «Solo las visibles», the ones the request
                // names. They come from the latest result of each source, which may change after the sheet was read: an id that is no
                // longer there is left out instead of failing the export.
                var resultColumns = FormRecordResultPresentationPolicy.Columns(await resultReader.ListLatestBySourceAsync(id, ct), culture)
                    .Where(column => request.ResultColumns is null || request.ResultColumns.Contains(column.Id)).ToArray();
                headers.AddRange(resultColumns.Select(column => column.Label));
                var rows = new List<IReadOnlyList<FormWorkbookCell>>(ids.Count);
                var groupStarts = hasItems ? new List<int>(ids.Count) : null;
                foreach (var batch in ids.Chunk(100))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                    var records = (await reader.ListByReceiptIdsAsync(definition, batch, ct)).ToDictionary(row => row.Record.SourceReceiptId);
                    var channels = await responses.ChannelsAsync(actor.Value, records.Values, ct);
                    var resultValues = resultColumns.Length == 0 ? new Dictionary<Guid, IReadOnlyDictionary<string, string?>>()
                        : FormRecordResultPresentationPolicy.Values(await resultReader.ListAsync(records.Values.Select(row => row.Record.RecordId).ToArray(), ct));
                    foreach (var receiptId in batch)
                    {
                        var row = records[receiptId];
                        var reply = FormRecordPresentationPolicy.Project(row.Record, definition.VersionsById[row.Record.VersionId].DisplayCulture(culture), definition.Schemas[row.Record.VersionId]);
                        var values = FormResponsePresentationPolicy.Cells(availableColumns, reply).ToDictionary(value => value.FieldId);
                        var response = FormExportResponsePolicy.Cells(row, reply.Number, channels[receiptId], zone, voided, culture);
                        var results = resultValues.GetValueOrDefault(row.Record.RecordId);
                        var resultCells = resultColumns.Select(column => FormExportPolicy.ResultCell(column.Kind, results?.GetValueOrDefault(column.Id))).ToArray();
                        // The block of its version, if it was exported: none if the version had no block, empty without items.
                        var own = blocks.Select(block => values[block]).FirstOrDefault(cell => cell.State != "NotAsked");
                        var items = own?.Value as IReadOnlyList<FormResponseRowCells>;
                        var count = Math.Max(1, items?.Count ?? 0);
                        if (rows.Count + count > MaximumRows) return FormRecordErrors.ExportTooManyRows;
                        groupStarts?.Add(rows.Count);
                        for (var index = 0; index < count; index++)
                        {
                            var answers = items is { Count: > 0 } ? items[index].Cells.ToDictionary(cell => cell.FieldId) : null;
                            var cells = new List<FormWorkbookCell>(headers.Count);
                            cells.AddRange(response);
                            foreach (var column in selectedColumns)
                                cells.Add(column.Part switch
                                {
                                    ExportPart.Item => own is null ? Missing : answers is null ? Empty : new("integer", (long)index + 1),
                                    ExportPart.BlockQuestion => own is null || own.FieldId != column.BlockId ? Missing
                                        : answers is null ? Empty : Export(answers[column.QuestionId], column),
                                    _ => Export(values[column.QuestionId], column),
                                });
                            cells.AddRange(resultCells);
                            rows.Add(cells);
                        }
                    }
                }
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                var sheet = new FormWorkbookSheet(headers, rows, FormResponseTexts.Get("Sheet", culture), groupStarts,
                    FormExportPolicy.DateFormat(culture), FormExportPolicy.DateTimeFormat(culture));
                var bytes = writer.Write(new([sheet]), ct);
                ct.ThrowIfCancellationRequested();
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                return new FormExportResponse(bytes, definition.Prefix + "-" + timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return FormRecordErrors.ExportBudgetExceeded;
            }
        });

    private enum ExportPart { Question, Item, BlockQuestion }

    // Coordinate is the part of a point a location column carries (UB4-A): 0 latitude, 1 longitude, 2 accuracy.
    private sealed record ExportColumn(Guid QuestionId, string Header, bool IsValue = false, Guid? ExtraId = null,
        ExportPart Part = ExportPart.Question, Guid? BlockId = null, int? Coordinate = null);

    private static FormWorkbookCell Export(FormResponseCell cell, ExportColumn column) => column.Coordinate is { } part
        ? FormExportPolicy.LocationCell(cell, part) : column.ExtraId is { } extraId
        ? FormExportPolicy.CatalogField(cell, extraId) : column.IsValue ? FormExportPolicy.CatalogValue(cell) : FormExportPolicy.Cell(cell);

    /// <summary>
    /// The columns in the order of the sheet. An exported block leaves «Ítem», once, and every question of the block in its
    /// place, with the catalog fields chosen; the fields of a hidden question still follow <c>CatalogFields</c>.
    /// </summary>
    private static List<ExportColumn> SelectColumns(IReadOnlyList<FormResponseColumn> available, FormExportRequest request, string culture)
    {
        var selected = new List<ExportColumn>();
        foreach (var column in available)
        {
            var shown = request.Columns is null || request.Columns.Contains(column.FieldId);
            if (column.Type != "group")
            {
                selected.AddRange(QuestionColumns(column, request, culture, shown));
                continue;
            }
            if (!shown) continue;
            if (!selected.Any(item => item.Part == ExportPart.Item)) selected.Add(new(column.FieldId, FormResponseTexts.Get("Item", culture), Part: ExportPart.Item));
            foreach (var question in column.Questions ?? [])
                selected.AddRange(QuestionColumns(question, request, culture, true).Select(item => item with { Part = ExportPart.BlockQuestion, BlockId = column.FieldId }));
        }
        return selected;
    }

    private static IEnumerable<ExportColumn> QuestionColumns(FormResponseColumn column, FormExportRequest request, string culture, bool shown)
    {
        var header = column.Label + (column.IntroducedInVersion > 1
            ? DisplayNumbers.Format(culture, FormResponseTexts.Get("SinceVersion", culture), column.IntroducedInVersion) : "");
        if (shown && column.Type == "location")
        {
            // UB4-A: a point goes in three number columns.
            yield return new(column.FieldId, header + " · " + FormResponseTexts.Get("Latitude", culture), Coordinate: 0);
            yield return new(column.FieldId, header + " · " + FormResponseTexts.Get("Longitude", culture), Coordinate: 1);
            yield return new(column.FieldId, header + " · " + FormResponseTexts.Get("AccuracyMeters", culture), Coordinate: 2);
        }
        else if (shown)
        {
            yield return new(column.FieldId, header);
            if (column.ValueType is not null) yield return new(column.FieldId, header + " · " + FormResponseTexts.Get("Value", culture), true);
        }
        foreach (var extra in column.Fields ?? [])
            if (request.CatalogFields is null || request.CatalogFields.Any(field => field!.QuestionId == column.FieldId && field.FieldId == extra.FieldId))
                yield return new(column.FieldId, header + " · " + extra.Label, ExtraId: extra.FieldId);
    }
}
