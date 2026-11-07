using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Loads a response in the app. Only the active published version of an active form is loaded, and the capture goes
/// through <see cref="IBusinessFormRecordCaptureService"/>: the same validations, copies, number, rules and files as WhatsApp.
/// A correction (CW10-A) loads the same way, in the name of whoever answered the response: its catalogs filter by that person.
/// </summary>
internal sealed class FormEntryService(FormEntryLoaderGuard loaders, FormEntryVersionPolicy describer, IFormEntryReader reader,
    FormEntryCatalogPolicy catalogs, FormEntryRecorder recorder, IRequestValidator validator, ILogger<FormEntryService> logger) : IFormEntryService
{
    public Task<Result<FormEntryResponse>> GetAsync(Guid definitionId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntryResponse>>(logger, "GetFormEntry", async () =>
        {
            var loader = await loaders.EntryAsync(cancellationToken); if (loader.IsFailure) return loader.Error;
            var described = await describer.DescribeAsync(definitionId, loader.Value.Person, cancellationToken); if (described.IsFailure) return described.Error;
            if (!await loaders.StillAllowedAsync(loader.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
            return described.Value.Entry;
        });

    public Task<Result<FormEntryCorrectionResponse>> GetCorrectionAsync(Guid definitionId, Guid recordId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntryCorrectionResponse>>(logger, "GetFormEntryCorrection", async () =>
        {
            var loaded = await loaders.CorrectionAsync(definitionId, recordId, cancellationToken); if (loaded.IsFailure) return loaded.Error;
            var (loader, row) = loaded.Value;
            var described = await describer.DescribeAsync(definitionId, loader.Person, cancellationToken); if (described.IsFailure) return described.Error;
            var (entry, schema) = described.Value;
            // What was there before, mapped to the active version: the questions that remain keep their answers and files.
            var recorded = FormDraftPolicy.Read<FormVersionSchema>(row.Record.SchemaJson);
            var reply = FormRecordPresentationPolicy.Project(row.Record, entry.Culture, recorded);
            var answers = reply.Values.Select(value => schema.Questions.SingleOrDefault(question => question.FieldId == value.FieldId) is { } question
                ? Answer(question, value.Value) : null).OfType<FormEntryCorrectionAnswer>().ToList();
            if (BlockAnswer(schema, recorded, row.Record, entry.Culture) is { } block) answers.Add(block);
            var grouped = await catalogs.WithGroupsAsync(schema, answers, entry.Culture, cancellationToken);
            var files = (row.Record.Files ?? []).Where(file => schema.Questions.Any(question => question.FieldId == file.FieldId && question.Files is not null))
                .OrderBy(file => file.FieldId).ThenBy(file => file.Position)
                .Select(file => new FormEntryCorrectionFile(file.FileId, file.FieldId, file.Name, file.ContentType, file.Size, file.HasThumbnail)).ToArray();
            if (!await loaders.StillAllowedAsync(loader, cancellationToken)) return FormDefinitionErrors.Forbidden;
            return new FormEntryCorrectionResponse(entry, recordId, reply.Number, grouped, files);
        });

    public Task<Result<PagedResult<FormEntryOptionResponse>>> ListOptionsAsync(Guid definitionId, Guid versionId, Guid fieldId,
        ListFormEntryOptionsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormEntryOptionResponse>>>(logger, "ListFormEntryOptions", async () =>
        {
            var loaded = await LoadQuestionAsync(definitionId, versionId, fieldId, request.RecordId, request, false, cancellationToken);
            if (loaded.IsFailure) return loaded.Error;
            var (loader, question, culture) = loaded.Value;
            var page = await catalogs.ListAsync(loader.Person, question, culture, request, cancellationToken);
            return await loaders.StillAllowedAsync(loader, cancellationToken) ? page : FormDefinitionErrors.Forbidden;
        });

    public Task<Result<FormEntryGroupsResponse>> ListGroupsAsync(Guid definitionId, Guid versionId, Guid fieldId,
        ListFormEntryGroupsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntryGroupsResponse>>(logger, "ListFormEntryGroups", async () =>
        {
            var loaded = await LoadQuestionAsync(definitionId, versionId, fieldId, request.RecordId, request, true, cancellationToken);
            if (loaded.IsFailure) return loaded.Error;
            var (loader, question, culture) = loaded.Value;
            var groups = await catalogs.ListGroupsAsync(loader.Person, question, culture, request, cancellationToken);
            return await loaders.StillAllowedAsync(loader, cancellationToken) ? groups : FormDefinitionErrors.Forbidden;
        });

    // The catalog question of the active version, for whoever loads or, while correcting, in the name of whoever answered;
    // grouped asks for a question that chooses first by a field (LD1-A).
    private async Task<Result<(FormEntryLoader Loader, CanonicalFormField Question, string Culture)>> LoadQuestionAsync<TRequest>(Guid definitionId,
        Guid versionId, Guid fieldId, Guid? recordId, TRequest request, bool grouped, CancellationToken cancellationToken) where TRequest : class
    {
        Result<FormEntryLoader> loader;
        if (recordId is { } id)
        {
            var correction = await loaders.CorrectionAsync(definitionId, id, cancellationToken);
            loader = correction.IsFailure ? correction.Error : correction.Value.Loader;
        }
        else loader = await loaders.EntryAsync(cancellationToken);
        if (loader.IsFailure) return loader.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var definition = await reader.FindDefinitionAsync(definitionId, versionId, cancellationToken);
        if (definition is null) return FormDefinitionErrors.NotFound;
        if (definition.Deactivated) return FormEntryErrors.Unavailable;
        if (definition.Version is not { } version) return FormEntryErrors.QuestionNotFound;
        if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        var question = schema.Questions.SingleOrDefault(value => value.FieldId == fieldId && value.Source?.Catalog is not null
            && (!grouped || value.Source.GroupBy is not null));
        if (question is null) return FormEntryErrors.QuestionNotFound;
        return (loader.Value, question, await describer.CultureAsync(definitionId, version, schema, cancellationToken));
    }

    public Task<Result<FormEntrySubmitResponse>> SubmitAsync(Guid definitionId, SubmitFormEntryRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntrySubmitResponse>>(logger, "SubmitFormEntry", async () =>
        {
            var loader = await loaders.EntryAsync(cancellationToken); if (loader.IsFailure) return loader.Error;
            var actor = loader.Value.Actor;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, request.VersionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Version is not { } version) return FormRecordErrors.InvalidResponse;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var entry = recorder.Prepare(definitionId, version, schema, actor, request);
            if (entry.IsFailure) return entry.Error;
            var group = FormEntryRecorder.Group(schema);
            // A repeated send returns its first result, even after a new version or a deactivation.
            if (await recorder.FindExistingAsync(entry.Value.Request, cancellationToken) is { } prior)
                return await recorder.OutcomeAsync(definition.Prefix, prior, group, cancellationToken);
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
            var culture = await describer.CultureAsync(definitionId, version, schema, cancellationToken);
            var offers = await catalogs.OffersAsync(actor, schema, entry.Value.Chosen, culture, version.Culture, cancellationToken, entry.Value.Items);
            if (offers.IsFailure) return offers.Error;
            var captured = await recorder.CaptureAsync(entry.Value.Request with
            {
                Catalogs = offers.Value.Count == 0 ? null : offers.Value, PresentationCulture = culture,
            }, cancellationToken);
            return await recorder.OutcomeAsync(definition.Prefix, captured, group, cancellationToken);
        });

    public Task<Result<FormEntrySubmitResponse>> CorrectAsync(Guid definitionId, Guid recordId, CorrectFormEntryRequest request,
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormEntrySubmitResponse>>(logger, "CorrectFormEntry", async () =>
        {
            var amend = await loaders.AmendAsync(cancellationToken); if (amend.IsFailure) return amend.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            var definition = await reader.FindDefinitionAsync(definitionId, request.VersionId, cancellationToken);
            if (definition is null) return FormDefinitionErrors.NotFound;
            if (definition.Version is not { } version) return FormRecordErrors.InvalidResponse;
            var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
            var entry = recorder.Prepare(definitionId, version, schema, amend.Value.Actor, request.Entry);
            if (entry.IsFailure) return entry.Error;
            var group = FormEntryRecorder.Group(schema);
            var correction = new FormRecordCorrection(recordId, request.Reason!.Trim(), request.KeptFileIds!);
            // A repeated send returns its first result, even after the response changed again.
            if (await recorder.FindExistingCorrectionAsync(entry.Value.Request, correction, cancellationToken) is { } prior)
                return await recorder.OutcomeAsync(definition.Prefix, prior, group, cancellationToken);
            var loaded = await loaders.CorrectionAsync(definitionId, recordId, cancellationToken); if (loaded.IsFailure) return loaded.Error;
            if (definition.Deactivated) return FormEntryErrors.Unavailable;
            if (version.VersionId != definition.ActiveVersionId) return FormEntryErrors.VersionChanged;
            var culture = await describer.CultureAsync(definitionId, version, schema, cancellationToken);
            var offers = await catalogs.OffersAsync(loaded.Value.Loader.Person, schema, entry.Value.Chosen, culture, version.Culture, cancellationToken,
                entry.Value.Items);
            if (offers.IsFailure) return offers.Error;
            var captured = await recorder.CaptureCorrectionAsync(entry.Value.Request with
            {
                Catalogs = offers.Value.Count == 0 ? null : offers.Value, PresentationCulture = culture,
            }, correction, cancellationToken);
            return await recorder.OutcomeAsync(definition.Prefix, captured, group, cancellationToken);
        });

    // The items of the block as the form sends them, when the active version keeps that block: each item with its answers to the
    // questions that remain in it. A block the active version no longer has does not come back.
    private static FormEntryCorrectionAnswer? BlockAnswer(FormVersionSchema active, FormVersionSchema recorded, FormRecordStoredRow record, string culture)
    {
        var group = active.Questions.FirstOrDefault(question => question.Type == "group");
        if (group is null || !recorded.Questions.Any(question => question.FieldId == group.FieldId && question.Type == "group")) return null;
        var block = active.Questions.SkipWhile(question => question.FieldId != group.FieldId).Skip(1).TakeWhile(question => question.Repeated == true).ToArray();
        return new(group.FieldId, null, null, null)
        {
            Rows = FormRecordPresentationPolicy.Rows(record, culture, recorded).Select(item => (IReadOnlyList<FormEntryCorrectionAnswer>)item.Values
                .Select(value => block.SingleOrDefault(question => question.FieldId == value.FieldId) is { } question ? Answer(question, value.Value) : null)
                .OfType<FormEntryCorrectionAnswer>().ToArray()).ToArray(),
        };
    }

    // An answer as the form sends it: canonical text, or the options chosen; a choice the active version no longer has is dropped.
    private static FormEntryCorrectionAnswer? Answer(CanonicalFormField question, object? value)
    {
        IReadOnlyList<StructuredRecordChoiceValue>? choices = value switch
        {
            StructuredRecordChoiceValue choice => [choice],
            StructuredRecordChoiceValue[] many => many,
            _ => null,
        };
        if (choices is not null)
        {
            var catalog = question.Source?.Catalog is not null;
            var kept = choices.Where(choice => catalog || question.Options?.Any(option => option.ChoiceId.ToString("D") == choice.ChoiceId) == true).ToArray();
            var ids = kept.Select(choice => Guid.Parse(choice.ChoiceId)).ToArray();
            var texts = catalog ? kept.Select(choice => new FormEntryOptionResponse(Guid.Parse(choice.ChoiceId), choice.Label, choice.Detail)).ToArray() : null;
            if (question.Type == "multiChoice") return new(question.FieldId, null, ids, texts);
            return ids.Length == 1 ? new(question.FieldId, ids[0].ToString("D"), null, texts) : null;
        }
        return value switch
        {
            string text => new(question.FieldId, text, null, null),
            bool boolean => new(question.FieldId, boolean ? "true" : "false", null, null),
            _ => null,
        };
    }
}
