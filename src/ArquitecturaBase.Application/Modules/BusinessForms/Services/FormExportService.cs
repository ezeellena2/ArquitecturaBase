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
internal sealed class FormExportService(IFormResponseService responses, IFormResponseReader reader, IFormWorkbookWriter writer,
    FormRecordAccessGuard access, IRequestValidator validator, TimeProvider timeProvider, ILogger<FormExportService> logger) : IFormExportService
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
                var filter = request.Filter with { Page = 1, PageSize = 100, UpToNumber = maximum };
                var first = await responses.ListAsync(id, filter, ct); if (first.IsFailure) return first.Error;
                if (first.Value.Records.TotalCount > MaximumRecords) return FormRecordErrors.ExportTooLarge;
                if (request.Columns?.Any(id => !first.Value.Columns.Any(column => column.FieldId == id)) == true) return FormRecordErrors.FilterInvalid;
                var columns = first.Value.Columns.Where(column => request.Columns is null || request.Columns.Contains(column.FieldId)).ToArray();
                var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
                var headers = new List<string> { FormResponseTexts.Get("Number", culture), FormResponseTexts.Get("ReceivedUtc", culture), FormResponseTexts.Get("Actor", culture), FormResponseTexts.Get("Channel", culture) };
                headers.AddRange(columns.Select(column => column.Label + (column.IntroducedInVersion > 1 ? string.Format(CultureInfo.GetCultureInfo(culture), FormResponseTexts.Get("SinceVersion", culture), column.IntroducedInVersion) : "")));
                var rows = new List<IReadOnlyList<FormWorkbookCell>>();
                var page = first.Value;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    if (page.Records.TotalCount > MaximumRecords || rows.Count + page.Records.Items.Count > MaximumRecords) return FormRecordErrors.ExportTooLarge;
                    foreach (var record in page.Records.Items)
                    {
                        var cells = new List<FormWorkbookCell> { new("text", record.Number), new("dateTime", record.SubmittedAtUtc),
                            new("text", record.Actor.DisplayName ?? FormResponseTexts.Get("unknown", culture)),
                            new("text", FormResponseTexts.Get(record.Channel.Length == 0 ? "unknown" : record.Channel, culture)) };
                        cells.AddRange(columns.Select(column => FormExportPolicy.Cell(record.Cells.Single(cell => cell.FieldId == column.FieldId))));
                        rows.Add(cells);
                    }
                    if (!page.Records.HasNext) break;
                    if (page.Records.Page >= MaximumRecords / 100) return FormRecordErrors.ExportTooLarge;
                    if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                    filter = filter with { Page = filter.Page + 1 };
                    var next = await responses.ListAsync(id, filter, ct); if (next.IsFailure) return next.Error;
                    page = next.Value;
                }
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                var bytes = writer.Write(new(headers, rows, FormResponseTexts.Get("Sheet", culture)), ct);
                ct.ThrowIfCancellationRequested();
                if (!await access.StillAllowedAsync(actor.Value, ct, true)) return FormDefinitionErrors.Forbidden;
                return new FormExportResponse(bytes, first.Value.Prefix + "-" + timeProvider.GetUtcNow().ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".xlsx");
            }
            catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                return FormRecordErrors.ExportBudgetExceeded;
            }
        });
}
