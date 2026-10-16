using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormResponseService(IFormResponseReader reader, FormRecordAccessGuard access,
    IRequestValidator validator, ILogger<FormResponseService> logger, FormRecordOriginPolicy origins) : IFormResponseService
{
    public Task<Result<PagedResult<FormNavigationResponse>>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormNavigationResponse>>>(logger, "ListFormResponseNavigation", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var result = await reader.ListNavigationAsync(request with { DisplayCulture = Culture() }, cancellationToken);
            return await access.StillAllowedAsync(actor.Value, cancellationToken) ? result : FormDefinitionErrors.Forbidden;
        });
    public Task<Result<FormResponseSheetResponse>> ListAsync(Guid id, ListFormRecordsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormResponseSheetResponse>>(logger, "ListFormResponses", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(id, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var filter = FormRecordFilterPolicy.Validate(request, definition); if (filter.IsFailure) return filter.Error;
            var culture = Culture();
            var columns = FormResponsePresentationPolicy.Columns(definition, culture);
            var page = await reader.ListAsync(definition, request with { DisplayCulture = culture }, cancellationToken);
            var channels = await origins.ListAsync(actor.Value, page.Items.Select(row => row.Record.SourceReceiptId).ToArray(), cancellationToken);
            if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var active = definition.Versions.Single(version => version.Id == definition.ActiveVersionId);
            var rows = page.Items.Select(row => {
                var reply = FormRecordPresentationPolicy.Project(row.Record, culture, definition.Schemas[row.Record.VersionId]);
                return new FormResponseRow(row.Record.RecordId, row.Record.VersionId, row.Record.VersionNumber, reply.Number,
                    row.SubmittedAtUtc, row.Actor, channels.GetValueOrDefault(row.Record.SourceReceiptId)?.ChannelId ?? "", FormResponsePresentationPolicy.Cells(columns, reply), row.State);
            }).ToArray();
            return new FormResponseSheetResponse(id, FormDraftPolicy.Read<Dictionary<string,string>>(active.NameJson)[culture], definition.Prefix,
                active.Number, columns, definition.Versions.Select(version => new FormResponseVersionResponse(version.Id, version.Number)).ToArray(),
                new(rows, page.Page, page.PageSize, page.TotalCount), definition.HasApproval);
        });
    public Task<Result<PagedResult<FormResponseFacetOptionResponse>>> CountFacetsAsync(Guid id, FormRecordFacetRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormResponseFacetOptionResponse>>>(logger, "CountFormResponseFacets", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            if (await validator.ValidateAsync(request.Filter, cancellationToken) is { } invalidFilter) return invalidFilter;
            var definition = await reader.FindDefinitionAsync(id, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var filter = FormRecordFilterPolicy.Validate(request.Filter, definition); if (filter.IsFailure) return filter.Error;
            if (request.Dimension is "choice" or "presence"
                && (request.FieldId is not { } fieldId || !FormResponsePresentationPolicy.Columns(definition, Culture()).Any(field => field.FieldId == fieldId))) return FormRecordErrors.FilterInvalid;
            var result = await reader.ListFacetsAsync(definition, request with { Filter = request.Filter with { DisplayCulture = Culture() } }, cancellationToken);
            return await access.StillAllowedAsync(actor.Value, cancellationToken) ? result : FormDefinitionErrors.Forbidden;
        });
    public Task<Result<FormResponseDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormResponseDetailResponse>>(logger, "GetFormResponse", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var row = await reader.FindRecordAsync(id, cancellationToken);
            if (row is null) return FormRecordErrors.NotFound;
            var channels = await origins.ListAsync(actor.Value, [row.Record.SourceReceiptId], cancellationToken);
            if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var reply = FormRecordPresentationPolicy.Project(row.Record, Culture(), FormDraftPolicy.Read<FormVersionSchema>(row.Record.SchemaJson));
            var origin = channels.GetValueOrDefault(row.Record.SourceReceiptId);
            var history = new List<FormResponseHistoryResponse>();
            if (origin is not null) history.Add(new("Sent", origin.SentAtUtc, origin.SenderUserId is { } senderId ? new(senderId, origin.SenderName) : null));
            history.Add(new("Submitted", row.SubmittedAtUtc, row.Actor));
            history.Add(new("Saved", row.CreatedAtUtc, null));
            return new FormResponseDetailResponse(id, row.Record.DefinitionId, row.Record.VersionId, row.Record.VersionNumber, reply.Number, reply.Name,
                row.SubmittedAtUtc, row.Actor, origin?.ChannelId ?? "", origin, reply.Values, history);
        });
    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
