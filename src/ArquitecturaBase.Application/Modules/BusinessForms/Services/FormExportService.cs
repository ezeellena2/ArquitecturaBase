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
    FormRecordOriginPolicy origins) : IFormExportService
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
                var columns = availableColumns.Where(column => request.Columns is null || request.Columns.Contains(column.FieldId)).ToArray();
                var filter = request.Filter with { UpToNumber = maximum, DisplayCulture = culture };
                var ids = await reader.ListExportIdsAsync(definition, filter, MaximumRecords + 1, ct);
                if (ids.Count > MaximumRecords) return FormRecordErrors.ExportTooLarge;
                var headers = new List<string> { FormResponseTexts.Get("Number", culture), FormResponseTexts.Get("ReceivedUtc", culture), FormResponseTexts.Get("Actor", culture), FormResponseTexts.Get("Channel", culture) };
                headers.AddRange(columns.Select(column => column.Label + (column.IntroducedInVersion > 1 ? string.Format(CultureInfo.GetCultureInfo(culture), FormResponseTexts.Get("SinceVersion", culture), column.IntroducedInVersion) : "")));
                var rows = new List<IReadOnlyList<FormWorkbookCell>>(ids.Count);
                foreach (var batch in ids.Chunk(100))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                    var records = (await reader.ListByReceiptIdsAsync(batch, ct)).ToDictionary(row => row.Record.SourceReceiptId);
                    var channels = await origins.ListAsync(actor.Value, batch, ct);
                    foreach (var receiptId in batch)
                    {
                        var row = records[receiptId];
                        var reply = FormRecordPresentationPolicy.Project(row.Record, culture, definition.Schemas[row.Record.VersionId]);
                        var values = FormResponsePresentationPolicy.Cells(columns, reply);
                        var channel = channels.GetValueOrDefault(receiptId)?.ChannelId ?? "";
                        var cells = new List<FormWorkbookCell> { new("text", reply.Number), new("dateTime", row.SubmittedAtUtc),
                            new("text", row.Actor.DisplayName ?? FormResponseTexts.Get("unknown", culture)),
                            new("text", FormResponseTexts.Get(channel.Length == 0 ? "unknown" : channel, culture)) };
                        cells.AddRange(values.Select(FormExportPolicy.Cell));
                        rows.Add(cells);
                    }
                }
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                var bytes = writer.Write(new(headers, rows, FormResponseTexts.Get("Sheet", culture)), ct);
                ct.ThrowIfCancellationRequested();
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                return new FormExportResponse(bytes, definition.Prefix + "-" + timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return FormRecordErrors.ExportBudgetExceeded;
            }
        });
}
