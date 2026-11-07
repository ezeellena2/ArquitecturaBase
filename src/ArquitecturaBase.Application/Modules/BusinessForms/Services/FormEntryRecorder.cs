using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Turns a send from the app into the common capture and its result into what the page shows. The capture keeps the
/// limits, copies, number and rules; this helper only maps answers and files and reads the saved outcome.
/// </summary>
internal sealed class FormEntryRecorder(IBusinessFormRecordCaptureService capture, IFormEntryReader reader, TimeProvider timeProvider)
{
    /// <summary>The capture request keyed by the version's questions, with the catalog options each question chose.</summary>
    public Result<FormEntryCapture> Prepare(Guid definitionId, FormEntryVersionRow version, FormVersionSchema schema, Guid actor,
        SubmitFormEntryRequest request)
    {
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        var chosen = new Dictionary<Guid, IReadOnlyList<Guid>>();
        IReadOnlyList<IReadOnlyDictionary<Guid, Guid>>? items = null;
        foreach (var answer in request.Answers!)
        {
            var question = schema.Questions.SingleOrDefault(value => value.FieldId == answer.FieldId);
            // A question of the block is answered only inside its items, and only the block takes items.
            if (question is null || question.Type is "photo" or "document" || question.Repeated == true
                || (answer.Rows is not null) != (question.Type == "group")) return FormRecordErrors.InvalidResponse;
            if (question.Type == "group")
            {
                if (answer.Value is not null || answer.ChoiceIds is not null || Items(schema, question, answer.Rows!) is not { } block)
                    return FormRecordErrors.InvalidResponse;
                answers[question.Key] = block.Json;
                items = block.Chosen;
                continue;
            }
            if (question.Type == "multiChoice")
            {
                if (answer.ChoiceIds is not { } choices || answer.Value is not null) return FormRecordErrors.InvalidResponse;
                answers[question.Key] = JsonSerializer.Serialize(choices.Select(choice => choice.ToString("D")));
                if (question.Source?.Catalog is not null && choices.Count > 0) chosen[question.FieldId] = choices;
                continue;
            }
            if (answer.ChoiceIds is not null || answer.Value is not { } value) return FormRecordErrors.InvalidResponse;
            if (value.Length == 0) continue;
            answers[question.Key] = value;
            if (question.Type == "choice" && question.Source?.Catalog is not null)
            {
                if (!Guid.TryParseExact(value, "D", out var choice)) return FormRecordErrors.InvalidResponse;
                chosen[question.FieldId] = [choice];
            }
        }
        var files = new List<StructuredRecordFileInput>(request.Files!.Count);
        foreach (var group in request.Files.GroupBy(file => file.FieldId))
        {
            var question = schema.Questions.SingleOrDefault(value => value.FieldId == group.Key && value.Type is "photo" or "document");
            if (question is null) return FormRecordErrors.InvalidResponse;
            var position = 0;
            foreach (var file in group)
                files.Add(new(question.FieldId, position++, FormEntryChannel.Id, question.Type, file.Name, file.ContentType,
                    Convert.ToHexStringLower(SHA256.HashData(file.Content)), file.Content));
        }
        return new FormEntryCapture(new(request.RequestId, definitionId, version.VersionId, actor, timeProvider.GetUtcNow().UtcDateTime,
            answers, Files: files), chosen, items);
    }

    /// <summary>The block's question, which names the item of a rejected answer in its error path.</summary>
    public static Guid? Group(FormVersionSchema schema) => schema.Questions.FirstOrDefault(question => question.Type == "group")?.FieldId;

    // The items in the capture's shape: a JSON array with an object per item, keyed in the order of the block's questions, and the
    // catalog option each item chose, by question, so that its offer is checked item by item.
    private static (string Json, IReadOnlyList<IReadOnlyDictionary<Guid, Guid>> Chosen)? Items(FormVersionSchema schema, CanonicalFormField group,
        IReadOnlyList<IReadOnlyList<FormEntryAnswer>> rows)
    {
        var block = schema.Questions.SkipWhile(question => question.FieldId != group.FieldId).Skip(1).TakeWhile(question => question.Repeated == true).ToArray();
        var items = new List<Dictionary<string, string>>(rows.Count);
        var chosen = new List<IReadOnlyDictionary<Guid, Guid>>(rows.Count);
        foreach (var row in rows)
        {
            if (row.Any(answer => answer.Value is null || block.All(question => question.FieldId != answer.FieldId))) return null;
            var item = new Dictionary<string, string>(StringComparer.Ordinal);
            var options = new Dictionary<Guid, Guid>();
            foreach (var question in block)
            {
                if (row.SingleOrDefault(answer => answer.FieldId == question.FieldId)?.Value is not { Length: > 0 } value) continue;
                item[question.Key] = value;
                if (question.Type != "choice" || question.Source?.Catalog is null) continue;
                if (!Guid.TryParseExact(value, "D", out var choice)) return null;
                options[question.FieldId] = choice;
            }
            items.Add(item);
            chosen.Add(options);
        }
        return (JsonSerializer.Serialize(items), chosen);
    }

    public Task<StructuredRecordCaptureResponse?> FindExistingAsync(StructuredRecordCaptureRequest entry, CancellationToken cancellationToken) =>
        capture.FindExistingEntryAsync(entry, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest entry, CancellationToken cancellationToken) =>
        capture.CaptureEntryAsync(entry, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingCorrectionAsync(StructuredRecordCaptureRequest entry, FormRecordCorrection correction,
        CancellationToken cancellationToken) => capture.FindExistingCorrectionAsync(entry, correction, cancellationToken);

    public Task<StructuredRecordCaptureResponse> CaptureCorrectionAsync(StructuredRecordCaptureRequest entry, FormRecordCorrection correction,
        CancellationToken cancellationToken) => capture.CaptureCorrectionAsync(entry, correction, cancellationToken);

    /// <summary>The saved number and state; a rule that asks for approval already shows it as pending. A rejection goes to its question.</summary>
    public async Task<Result<FormEntrySubmitResponse>> OutcomeAsync(string prefix, StructuredRecordCaptureResponse captured, Guid? group,
        CancellationToken cancellationToken)
    {
        if (captured.State != "Completed" || captured.RecordId is not { } recordId || captured.Number is not { } number)
            return Rejection(captured, group);
        var outcome = await reader.FindOutcomeAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException("The captured response is missing.");
        return new FormEntrySubmitResponse(recordId, prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture),
            outcome.State == "Received" && outcome.ApprovalRequested ? "PendingApproval" : outcome.State, outcome.SubmittedAtUtc);
    }

    private static Error Rejection(StructuredRecordCaptureResponse captured, Guid? group)
    {
        Error error = captured.ErrorCode switch
        {
            var code when code == FormRecordErrors.CatalogItemInactive.Code => FormRecordErrors.CatalogItemInactive,
            var code when code == FormRecordErrors.ReplayConflict.Code => FormRecordErrors.ReplayConflict,
            var code when code == FormRecordErrors.AuthorizationChanged.Code => FormRecordErrors.AuthorizationChanged,
            var code when code == FormRecordErrors.AmendUnavailable.Code => FormRecordErrors.AmendUnavailable,
            var code when code == FormRecordErrors.NotFound.Code => FormRecordErrors.NotFound,
            var code when code == FormDefinitionErrors.Forbidden.Code => FormDefinitionErrors.Forbidden,
            var code when code == FileErrors.TooLarge.Code => FileErrors.TooLarge,
            var code when code == FileErrors.InvalidType.Code => FileErrors.InvalidType,
            var code when code == FileErrors.HashMismatch.Code => FileErrors.HashMismatch,
            var code when code == FileErrors.InvalidMetadata.Code => FileErrors.InvalidMetadata,
            var code when code == FileErrors.Unavailable.Code => FileErrors.Unavailable,
            _ => FormRecordErrors.InvalidResponse,
        };
        if (captured.RejectedFieldId is not { } fieldId || error.Type != ErrorType.Validation) return error;
        // The answer of an item goes to its cell: answers.<block>.<item from 0>.<question>.
        return FieldErrors.On(error, captured.RejectedRow is { } row && group is { } block
            ? $"answers.{block:D}.{row.ToString(CultureInfo.InvariantCulture)}.{fieldId:D}" : "answers." + fieldId.ToString("D"));
    }
}

/// <summary>The capture request of a send and the catalog options each answered question chose.</summary>
internal sealed record FormEntryCapture(StructuredRecordCaptureRequest Request, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Chosen,
    IReadOnlyList<IReadOnlyDictionary<Guid, Guid>>? Items = null)
{
    public override string ToString() => "FormEntryCapture { Content = [REDACTED] }";
}
