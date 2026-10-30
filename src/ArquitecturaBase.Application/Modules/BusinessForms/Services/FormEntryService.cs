using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Loads a response in the app. Only the active published version of an active form is loaded, and the capture goes
/// through <see cref="IBusinessFormRecordCaptureService"/>: the same validations, copies, number, rules and files as WhatsApp.
/// </summary>
internal sealed class FormEntryService(FormEntryAccessGuard access, IFormEntryReader reader, FormEntryCatalogPolicy catalogs,
    FormPresentationCulturePolicy presentation, FormEntryRecorder recorder, IRequestValidator validator, IOptions<BusinessFormsOptions> options,
    ILogger<FormEntryService> logger) : IFormEntryService
{
    public Task<Result<FormEntryResponse>> GetAsync(Guid definitionId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntryResponse>>(logger, "GetFormEntry", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            var definition = await reader.FindDefinitionAsync(definitionId, null, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (definition.Version is not { } version) return FormDefinitionErrors.NotFound;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var culture = await CultureAsync(definitionId, version, schema, cancellationToken);
            FormPersonRecipientDataRow? personData = null;
            var questions = new List<FormEntryQuestionResponse>(schema.Questions.Count);
            foreach (var question in schema.Questions)
            {
                FormEntryCatalogResponse? catalog = null;
                if (question.Source?.Catalog is not null)
                    (catalog, personData) = await catalogs.DescribeAsync(actor.Value, question, culture, personData, cancellationToken);
                questions.Add(new(question.FieldId, question.Type, FormRecordPresentationPolicy.Text(question.Label, culture),
                    question.Help is { Count: > 0 } help ? FormRecordPresentationPolicy.Text(help, culture) : null, question.Required, question.MaxLength,
                    question.Options?.Select(option => new FormEntryChoiceResponse(option.ChoiceId, FormRecordPresentationPolicy.Text(option.Label, culture))).ToArray(),
                    question.Selection, question.Files, catalog));
            }
            if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            return new FormEntryResponse(definitionId, version.VersionId, version.Number, definition.Prefix,
                FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.NameJson), culture), culture,
                FormRecordPresentationPolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(version.SubmitLabelJson), culture),
                options.Value.MaxFileSizeMiB, questions);
        });

    public Task<Result<PagedResult<FormEntryOptionResponse>>> ListOptionsAsync(Guid definitionId, Guid versionId, Guid fieldId,
        ListFormEntryOptionsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormEntryOptionResponse>>>(logger, "ListFormEntryOptions", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, versionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (definition.Version is not { } version) return FormEntryErrors.QuestionNotFound;
            if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var question = schema.Questions.SingleOrDefault(value => value.FieldId == fieldId && value.Source?.Catalog is not null);
            if (question is null) return FormEntryErrors.QuestionNotFound;
            var page = await catalogs.ListAsync(actor.Value, question, await CultureAsync(definitionId, version, schema, cancellationToken), request, cancellationToken);
            return await access.StillAllowedAsync(actor.Value, cancellationToken) ? page : FormDefinitionErrors.Forbidden;
        });

    public Task<Result<FormEntrySubmitResponse>> SubmitAsync(Guid definitionId, SubmitFormEntryRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntrySubmitResponse>>(logger, "SubmitFormEntry", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, request.VersionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Version is not { } version) return FormRecordErrors.InvalidResponse;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var entry = recorder.Prepare(definitionId, version, schema, actor.Value, request);
            if (entry.IsFailure) return entry.Error;
            // A repeated send returns its first result, even after a new version or a deactivation.
            if (await recorder.FindExistingAsync(entry.Value.Request, cancellationToken) is { } prior)
                return await recorder.OutcomeAsync(definition.Prefix, prior, cancellationToken);
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
            var culture = await CultureAsync(definitionId, version, schema, cancellationToken);
            var offers = await catalogs.OffersAsync(actor.Value, schema, entry.Value.Chosen, culture, version.Culture, cancellationToken);
            if (offers.IsFailure) return offers.Error;
            var captured = await recorder.CaptureAsync(entry.Value.Request with
            {
                Catalogs = offers.Value.Count == 0 ? null : offers.Value, PresentationCulture = culture,
            }, cancellationToken);
            return await recorder.OutcomeAsync(definition.Prefix, captured, cancellationToken);
        });

    // The menu language when the version is complete in it; the original otherwise.
    private Task<string> CultureAsync(Guid definitionId, FormEntryVersionRow version, FormVersionSchema schema, CancellationToken cancellationToken) =>
        presentation.CultureAsync(definitionId, version.Culture, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es",
            version.NameJson, version.SubmitLabelJson, schema, cancellationToken);
}
