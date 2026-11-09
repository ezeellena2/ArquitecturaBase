using System.Globalization;
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
    FormRecordOriginPolicy origins, IFormRecordResultReader resultReader) : IFormExportService
{
    internal const int MaximumRecords = 10000;
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
                // The catalog fields of a question of the block go to the sheet of its block.
                if (request.CatalogFields?.Any(field => !availableColumns.SelectMany(column => column.Questions ?? [column]).Any(column => column.FieldId == field!.QuestionId
                    && column.Fields?.Any(extra => extra.FieldId == field.FieldId) == true)) == true) return FormRecordErrors.FilterInvalid;
                var selectedColumns = SelectColumns(availableColumns, request, culture);
                // A block exported in «Respuestas» has a sheet with a row per item: every question of the block, with their extra columns.
                var blocks = availableColumns.Where(column => column.Type == "group" && (request.Columns is null || request.Columns.Contains(column.FieldId)))
                    .Select(column => new ExportBlock(column, SelectColumns(column.Questions ?? [], request with { Columns = null }, culture))).ToArray();
                var filter = request.Filter with { UpToNumber = maximum, DisplayCulture = culture };
                var ids = await reader.ListExportIdsAsync(definition, filter, MaximumRecords + 1, ct);
                if (ids.Count > MaximumRecords) return FormRecordErrors.ExportTooLarge;
                var headers = new List<string> { FormResponseTexts.Get("Number", culture), FormResponseTexts.Get("ReceivedUtc", culture), FormResponseTexts.Get("Actor", culture), FormResponseTexts.Get("Channel", culture) };
                // With «Incluir las anuladas» a column says which ones were voided; otherwise there are none.
                var voided = request.Filter.IncludeVoided && request.Filter.State is null;
                if (voided) headers.Add(FormResponseTexts.Get("Voided", culture));
                headers.AddRange(selectedColumns.Select(column => column.Header));
                // RE1-A: the data of the results go after the questions, a column each.
                var resultColumns = FormRecordResultPresentationPolicy.Columns(await resultReader.ListLatestBySourceAsync(id, ct), culture);
                headers.AddRange(resultColumns.Select(column => column.Label));
                var rows = new List<IReadOnlyList<FormWorkbookCell>>(ids.Count);
                foreach (var batch in ids.Chunk(100))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                    var records = (await reader.ListByReceiptIdsAsync(definition, batch, ct)).ToDictionary(row => row.Record.SourceReceiptId);
                    // A correction arrived through the channel of its first version.
                    var channels = await origins.ListAsync(actor.Value, records.Values.Select(row => row.OriginReceiptId).ToArray(), ct);
                    var resultValues = resultColumns.Count == 0 ? new Dictionary<Guid, IReadOnlyDictionary<string, string?>>()
                        : FormRecordResultPresentationPolicy.Values(await resultReader.ListAsync(records.Values.Select(row => row.Record.RecordId).ToArray(), ct));
                    foreach (var receiptId in batch)
                    {
                        var row = records[receiptId];
                        var reply = FormRecordPresentationPolicy.Project(row.Record, definition.VersionsById[row.Record.VersionId].DisplayCulture(culture), definition.Schemas[row.Record.VersionId]);
                        var values = FormResponsePresentationPolicy.Cells(availableColumns, reply).ToDictionary(value => value.FieldId);
                        var channel = channels.GetValueOrDefault(row.OriginReceiptId)?.ChannelId ?? "";
                        var cells = new List<FormWorkbookCell> { new("text", reply.Number), new("dateTime", row.SubmittedAtUtc),
                            new("text", row.Actor.DisplayName ?? FormResponseTexts.Get("unknown", culture)),
                            new("text", FormResponseTexts.Get(channel.Length == 0 ? "unknown" : channel, culture)) };
                        if (voided) cells.Add(new("text", row.State == nameof(FormRecordState.Voided) ? FormResponseTexts.Get("true", culture) : ""));
                        cells.AddRange(selectedColumns.Select(column => Export(values[column.QuestionId], column)));
                        var results = resultValues.GetValueOrDefault(row.Record.RecordId);
                        cells.AddRange(resultColumns.Select(column => FormExportPolicy.ResultCell(column.Kind, results?.GetValueOrDefault(column.Id))));
                        rows.Add(cells);
                        foreach (var block in blocks)
                        {
                            if (values[block.Column.FieldId].Value is not IReadOnlyList<FormResponseRowCells> items) continue;
                            foreach (var (item, index) in items.Select((item, index) => (item, index)))
                            {
                                var answers = item.Cells.ToDictionary(cell => cell.FieldId);
                                block.Rows.Add([new("text", reply.Number), new("integer", (long)index + 1),
                                    ..block.Columns.Select(column => Export(answers[column.QuestionId], column))]);
                            }
                        }
                    }
                }
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                var sheets = new List<FormWorkbookSheet> { new(headers, rows, FormResponseTexts.Get("Sheet", culture)) };
                foreach (var block in blocks)
                    sheets.Add(new([FormResponseTexts.Get("Number", culture), FormResponseTexts.Get("Item", culture), ..block.Columns.Select(column => column.Header)],
                        block.Rows, FormExportPolicy.SheetName(block.Column.Label, sheets.Select(sheet => sheet.Name).ToArray(), culture)));
                var bytes = writer.Write(new(sheets), ct);
                ct.ThrowIfCancellationRequested();
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                return new FormExportResponse(bytes, definition.Prefix + "-" + timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return FormRecordErrors.ExportBudgetExceeded;
            }
        });

    private sealed record ExportColumn(Guid QuestionId, string Header, bool IsValue = false, Guid? ExtraId = null);

    private sealed record ExportBlock(FormResponseColumn Column, IReadOnlyList<ExportColumn> Columns)
    {
        public List<IReadOnlyList<FormWorkbookCell>> Rows { get; } = [];
    }

    private static FormWorkbookCell Export(FormResponseCell cell, ExportColumn column) => column.ExtraId is { } extraId
        ? FormExportPolicy.CatalogField(cell, extraId) : column.IsValue ? FormExportPolicy.CatalogValue(cell) : FormExportPolicy.Cell(cell);

    private static List<ExportColumn> SelectColumns(IReadOnlyList<FormResponseColumn> available, FormExportRequest request, string culture)
    {
        var selected = new List<ExportColumn>();
        foreach (var column in available)
        {
            var header = column.Label + (column.IntroducedInVersion > 1
                ? string.Format(CultureInfo.GetCultureInfo(culture), FormResponseTexts.Get("SinceVersion", culture), column.IntroducedInVersion) : "");
            if (request.Columns is null || request.Columns.Contains(column.FieldId))
            {
                selected.Add(new(column.FieldId, header));
                if (column.ValueType is not null) selected.Add(new(column.FieldId, header + " · " + FormResponseTexts.Get("Value", culture), true));
            }
            foreach (var extra in column.Fields ?? [])
                if (request.CatalogFields is null || request.CatalogFields.Any(field => field!.QuestionId == column.FieldId && field.FieldId == extra.FieldId))
                    selected.Add(new(column.FieldId, header + " · " + extra.Label, ExtraId: extra.FieldId));
        }
        return selected;
    }

}
