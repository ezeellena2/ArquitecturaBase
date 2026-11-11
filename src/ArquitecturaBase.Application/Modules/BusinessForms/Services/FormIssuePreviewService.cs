using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The answers go through the same preparation as the send (version, options offered to the person, calculations) and the issuer
/// previews what it would issue; nothing is saved and nothing goes to the provider.
/// </summary>
internal sealed class FormIssuePreviewService(FormEntryLoaderGuard loaders, IFormEntryReader reader, FormEntryRecorder recorder,
    FormEntryVersionPolicy describer, FormEntryCatalogPolicy catalogs, FormIssuePreviewPolicy previews, IRequestValidator validator,
    ILogger<FormIssuePreviewService> logger) : IFormIssuePreviewService
{
    public Task<Result<FormIssuePreviewResponse>> PreviewAsync(Guid definitionId, PreviewFormIssueRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormIssuePreviewResponse>>(logger, "PreviewFormIssue", async () =>
        {
            var loader = await loaders.EntryAsync(cancellationToken); if (loader.IsFailure) return loader.Error;
            var actor = loader.Value.Actor;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, request.VersionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Version is not { } version) return FormRecordErrors.InvalidResponse;
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var entry = recorder.Prepare(definitionId, version, schema, actor, new SubmitFormEntryRequest(Guid.Empty, request.VersionId, request.Answers, []));
            if (entry.IsFailure) return entry.Error;
            var culture = await describer.CultureAsync(definitionId, version, schema, cancellationToken);
            var offers = await catalogs.OffersAsync(actor, schema, entry.Value.Chosen, culture, version.Culture, cancellationToken, entry.Value.Items);
            if (offers.IsFailure) return offers.Error;
            var answers = FormRecordAnswerPolicy.Normalize(schema, entry.Value.Request.Answers, offers.Value);
            if (answers is null) return FormRecordErrors.InvalidResponse;
            var preview = await previews.PreviewAsync(definitionId, version.SchemaJson, answers, offers.Value, cancellationToken);
            if (preview is null) return FormEntryErrors.NoIssuance;
            return new FormIssuePreviewResponse(preview.Document, preview.Receiver, preview.Total, preview.FailureCode,
                preview.FailureCode is { } code ? ErrorMessages.Get(code) : null) { Info = preview.Info, Date = preview.Date, Amounts = preview.Amounts };
        });
}
