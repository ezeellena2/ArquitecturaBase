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
        foreach (var answer in request.Answers!)
        {
            var question = schema.Questions.SingleOrDefault(value => value.FieldId == answer.FieldId);
            if (question is null || question.Type is "photo" or "document") return FormRecordErrors.InvalidResponse;
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
            answers, Files: files), chosen);
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
    public async Task<Result<FormEntrySubmitResponse>> OutcomeAsync(string prefix, StructuredRecordCaptureResponse captured, CancellationToken cancellationToken)
    {
        if (captured.State != "Completed" || captured.RecordId is not { } recordId || captured.Number is not { } number)
            return Rejection(captured);
        var outcome = await reader.FindOutcomeAsync(recordId, cancellationToken)
            ?? throw new InvalidOperationException("The captured response is missing.");
        return new FormEntrySubmitResponse(recordId, prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture),
            outcome.State == "Received" && outcome.ApprovalRequested ? "PendingApproval" : outcome.State, outcome.SubmittedAtUtc);
    }

    private static Error Rejection(StructuredRecordCaptureResponse captured)
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
        return captured.RejectedFieldId is not { } fieldId || error.Type != ErrorType.Validation ? error
            : FieldErrors.On(error, "answers." + fieldId.ToString("D"));
    }
}

/// <summary>The capture request of a send and the catalog options each answered question chose.</summary>
internal sealed record FormEntryCapture(StructuredRecordCaptureRequest Request, IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> Chosen)
{
    public override string ToString() => "FormEntryCapture { Content = [REDACTED] }";
}
