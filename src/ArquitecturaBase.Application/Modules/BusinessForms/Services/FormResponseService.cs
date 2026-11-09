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
    IRequestValidator validator, ILogger<FormResponseService> logger, FormRecordOriginPolicy origins, IFormSuggestionReader suggestions,
    FormRecordAmendmentGuard amendments, IFormRecordResultReader resultReader) : IFormResponseService
{
    public Task<Result<FormRecordNavigationResponse>> FindRecordNavigationAsync(Guid definitionId, Guid recordId,
        ListFormRecordsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormRecordNavigationResponse>>(logger, "FindFormRecordNavigation", async () =>
        {
            var read = await access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var filter = FormRecordFilterPolicy.Validate(request, definition); if (filter.IsFailure) return filter.Error;
            var row = await reader.FindRecordNavigationAsync(definition, recordId, read.Value.Apply(request) with { DisplayCulture = Culture() }, cancellationToken);
            if (!await access.StillAllowedAsync(read.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            if (row is null) return FormRecordErrors.NotFound;
            return new FormRecordNavigationResponse(row.RecordId, row.PreviousId, row.NextId, row.Position, row.TotalCount);
        });
    public Task<Result<FormResponseSheetResponse>> ListAsync(Guid id, ListFormRecordsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormResponseSheetResponse>>(logger, "ListFormResponses", async () =>
        {
            var read = await access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(id, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var filter = FormRecordFilterPolicy.Validate(request, definition); if (filter.IsFailure) return filter.Error;
            var culture = Culture();
            var columns = FormResponsePresentationPolicy.Columns(definition, culture);
            var page = await reader.ListAsync(definition, read.Value.Apply(request) with { DisplayCulture = culture }, cancellationToken);
            // A correction arrived through the channel of its first version.
            var channels = await origins.ListAsync(read.Value.Actor, page.Items.Select(row => row.OriginReceiptId).ToArray(), cancellationToken);
            var resultColumns = FormRecordResultPresentationPolicy.Columns(await resultReader.ListLatestBySourceAsync(id, cancellationToken), culture);
            var resultValues = resultColumns.Count == 0 ? new Dictionary<Guid, IReadOnlyDictionary<string, string?>>()
                : FormRecordResultPresentationPolicy.Values(await resultReader.ListAsync(page.Items.Select(row => row.Record.RecordId).ToArray(), cancellationToken));
            if (!await access.StillAllowedAsync(read.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var active = definition.Versions.Single(version => version.Id == definition.ActiveVersionId);
            var rows = page.Items.Select(row => {
                var reply = FormRecordPresentationPolicy.Project(row.Record, definition.VersionsById[row.Record.VersionId].DisplayCulture(culture), definition.Schemas[row.Record.VersionId]);
                return new FormResponseRow(row.Record.RecordId, row.Record.VersionId, row.Record.VersionNumber, reply.Number,
                    row.SubmittedAtUtc, row.Actor, channels.GetValueOrDefault(row.OriginReceiptId)?.ChannelId ?? "", FormResponsePresentationPolicy.Cells(columns, reply), row.State, row.HasPdf,
                    row.ReplacesRecordId is not null) { Results = resultValues.GetValueOrDefault(row.Record.RecordId) };
            }).ToArray();
            return new FormResponseSheetResponse(id, FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string,string>>(active.NameJson), active.DisplayCulture(culture)), definition.Prefix,
                active.Number, columns, definition.Versions.Select(version => new FormResponseVersionResponse(version.Id, version.Number)).ToArray(),
                new(rows, page.Page, page.PageSize, page.TotalCount), definition.HasApproval) { ResultColumns = resultColumns.Count == 0 ? null : resultColumns };
        });
    public Task<Result<PagedResult<FormResponseFacetOptionResponse>>> CountFacetsAsync(Guid id, FormRecordFacetRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormResponseFacetOptionResponse>>>(logger, "CountFormResponseFacets", async () =>
        {
            var read = await access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            if (await validator.ValidateAsync(request.Filter, cancellationToken) is { } invalidFilter) return invalidFilter;
            var definition = await reader.FindDefinitionAsync(id, cancellationToken);
            if (definition is null) return FormRecordErrors.NotFound;
            var filter = FormRecordFilterPolicy.Validate(request.Filter, definition); if (filter.IsFailure) return filter.Error;
            // A question of the block of rows counts by itself; the block does not.
            if (request.Dimension is "choice" or "presence"
                && (request.FieldId is not { } fieldId || !FormResponsePresentationPolicy.Columns(definition, Culture())
                    .SelectMany(column => column.Questions ?? [column]).Any(field => field.FieldId == fieldId))) return FormRecordErrors.FilterInvalid;
            var result = await reader.ListFacetsAsync(definition, request with { Filter = read.Value.Apply(request.Filter) with { DisplayCulture = Culture() } }, cancellationToken);
            return await access.StillAllowedAsync(read.Value, cancellationToken) ? result : FormDefinitionErrors.Forbidden;
        });
    public Task<Result<FormResponseDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormResponseDetailResponse>>(logger, "GetFormResponse", async () =>
        {
            var read = await access.CheckReadAsync(cancellationToken); if (read.IsFailure) return read.Error;
            var row = await reader.FindRecordAsync(id, cancellationToken);
            if (row is null || read.Value.OwnOnly && row.Actor.UserId != read.Value.Actor) return FormRecordErrors.NotFound;
            FormResponseSuggestionResponse? suggestion = null;
            if (row.CaptureOriginJson is { } captureJson
                && FormDraftPolicy.Read<FormRecordCaptureOriginSnapshot>(captureJson).Origin.Suggestion is { } lineage)
            {
                var set = await suggestions.FindSetAsync(lineage.SuggestionSetId, cancellationToken)
                    ?? throw new InvalidOperationException("The captured suggestion revision is missing.");
                var historical = FormDraftPolicy.Read<FormSuggestion[]>(set.SuggestionsJson).Single(value => value.Id == lineage.OriginSuggestionId);
                suggestion = new(lineage.OriginRecordId, historical.Id, historical.Mode);
            }
            var chain = await reader.ListChainAsync(id, cancellationToken);
            var amendment = await reader.FindAmendmentAsync(id, cancellationToken);
            var channels = await origins.ListAsync(read.Value.Actor, [row.OriginReceiptId], cancellationToken);
            var amend = await amendments.FindAsync(cancellationToken);
            var results = await resultReader.ListAsync([id], cancellationToken);
            if (!await access.StillAllowedAsync(read.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            var contentCulture = (row.Version ?? throw new InvalidOperationException("The response version presentation is missing.")).DisplayCulture(Culture());
            var reply = FormRecordPresentationPolicy.Project(row.Record, contentCulture, FormDraftPolicy.Read<FormVersionSchema>(row.Record.SchemaJson));
            var origin = channels.GetValueOrDefault(row.OriginReceiptId);
            var history = new List<FormResponseHistoryResponse>();
            if (origin is not null && origin.ChannelId != FormEntryChannel.Id)
                history.Add(new("Sent", origin.SentAtUtc, origin.SenderUserId is { } senderId ? new(senderId, origin.SenderName) : null));
            history.Add(new("Submitted", row.SubmittedAtUtc, row.Actor));
            history.Add(new("Saved", row.CreatedAtUtc, null));
            return new FormResponseDetailResponse(id, row.Record.DefinitionId, row.Record.VersionId, row.Record.VersionNumber, reply.Number, reply.Name,
                row.SubmittedAtUtc, row.Actor, origin?.ChannelId ?? "", origin, reply.Values, history)
            {
                Assistance = FormResponseAssistancePolicy.Project(row, contentCulture) is { } assistance ? assistance with { Suggestion = suggestion } : null,
                Chain = chain.Count > 1 ? chain.OrderBy(record => record.SubmittedAtUtc).ThenBy(record => record.Id)
                    .Select(record => new FormResponseChainRecordResponse(record.Id, record.DefinitionId, record.OriginRecordId,
                        record.Prefix + "-" + record.Number.ToString("D6", CultureInfo.InvariantCulture),
                        FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string,string>>(record.NameJson), (record.Version ?? throw new InvalidOperationException("The chain version presentation is missing.")).DisplayCulture(Culture())), record.SubmittedAtUtc)).ToArray() : [],
                Correction = amendment.Correction, ReplacedBy = amendment.ReplacedBy, Void = amendment.Void,
                Results = results.Count == 0 ? null : results.Select(result => FormRecordResultPresentationPolicy.Project(result, contentCulture)).ToArray(),
                CanAmend = amend is not null && FormRecordAmendmentGuard.Allows(amend, row.Actor.UserId, Enum.Parse<FormRecordState>(row.State),
                    row.ReplacedByRecordId is not null).IsSuccess,
            };
        });
    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
