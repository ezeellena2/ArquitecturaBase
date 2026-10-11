using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class BusinessFormRecordRecorder(IFormRecordRepository records,
    IPermissionService permissions, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<BusinessFormRecordRecorder> logger) : IBusinessFormRecordCaptureService
{
    public async Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "CaptureBusinessFormRecord");
        if (request.SourceReceiptId == Guid.Empty || request.ActorUserId == Guid.Empty
            || request.SubmittedAtUtc.Kind != DateTimeKind.Utc
            || !await permissions.HasPermissionAsync(request.ActorUserId, BusinessFormsPermissions.RecordsCreate, cancellationToken))
        {
            OperationLog.Failed(logger, "CaptureBusinessFormRecord", FormRecordErrors.AuthorizationChanged.Code);
            return new("Rejected", null, null, FormRecordErrors.AuthorizationChanged.Code);
        }

        var result = (await unitOfWork.ExecuteInTransactionAsync(
            ct => CaptureCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken)).Value;
        if (result.State == "Completed") OperationLog.Handled(logger, "CaptureBusinessFormRecord");
        else OperationLog.Failed(logger, "CaptureBusinessFormRecord", result.ErrorCode ?? FormRecordErrors.InvalidResponse.Code);
        return result;
    }

    private async Task<Result<StructuredRecordCaptureResponse>> CaptureCoreAsync(StructuredRecordCaptureRequest request, CancellationToken ct)
    {
        await records.LockSourceReceiptAsync(request.SourceReceiptId, ct);
        var fingerprint = Fingerprint(request);
        var prior = await records.GetReceiptAsync(request.SourceReceiptId, ct);
        if (prior is not null)
        {
            return prior.Fingerprint == fingerprint
                ? new StructuredRecordCaptureResponse(prior.State, prior.RecordId, prior.Number, prior.ErrorCode)
                : new StructuredRecordCaptureResponse("Rejected", null, null, FormRecordErrors.ReplayConflict.Code);
        }

        if (!await permissions.HasPermissionAsync(request.ActorUserId, BusinessFormsPermissions.RecordsCreate, ct))
            return Reject(request, fingerprint, FormRecordErrors.AuthorizationChanged.Code);

        await records.LockDefinitionsAsync(ct);
        var definition = await records.GetDefinitionAsync(request.DefinitionId, ct);
        var version = await records.GetVersionAsync(request.VersionId, ct);
        if (definition is null || version is null || version.DefinitionId != request.DefinitionId
            || (definition.ActiveVersionId != version.Id && !await records.ExistsPublishedAsync(version.Id, ct)))
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        var schema = FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson);
        var answers = Normalize(schema, request.Answers);
        if (answers is null)
            return Reject(request, fingerprint, FormRecordErrors.InvalidResponse.Code);

        var number = definition.AllocateRecordNumber();
        var record = BusinessFormRecord.Create(definition.Id, version.Id, number, request.ActorUserId,
            request.SourceReceiptId, request.SubmittedAtUtc, FormDraftPolicy.Serialize(answers.Select(value => new { value.FieldId, value.Value }).ToArray()));
        records.Add(record);
        foreach (var value in answers)
            records.Add(BusinessFormRecordValue.Create(record.Id, definition.Id, value.FieldId, value.Type,
                value.Text, value.Decimal, value.Date, value.Boolean, value.ChoiceId));
        records.Add(BusinessFormCaptureReceipt.Complete(request.SourceReceiptId, definition.Id, version.Id,
            fingerprint, record.Id, number, timeProvider.GetUtcNow().UtcDateTime));
        return new StructuredRecordCaptureResponse("Completed", record.Id, number, null);
    }

    private Result<StructuredRecordCaptureResponse> Reject(StructuredRecordCaptureRequest request, string fingerprint, string code)
    {
        records.Add(BusinessFormCaptureReceipt.Reject(request.SourceReceiptId, request.DefinitionId, request.VersionId,
            fingerprint, code, timeProvider.GetUtcNow().UtcDateTime));
        return new StructuredRecordCaptureResponse("Rejected", null, null, code);
    }

    private static List<NormalizedAnswer>? Normalize(FormVersionSchema schema, IReadOnlyDictionary<string, string> answers)
    {
        if (schema.Format != 1 || schema.Questions.Count > 40) return null;
        var questions = schema.Questions;
        if (answers.Keys.Any(key => !questions.Any(question => question.Key == key))) return null;
        var result = new List<NormalizedAnswer>();
        foreach (var question in questions)
        {
            if (!answers.TryGetValue(question.Key, out var raw))
            {
                if (question.Required) return null;
                continue;
            }
            if (raw.Length == 0)
            {
                if (question.Required) return null;
                continue;
            }
            switch (question.Type)
            {
                case "shortText":
                case "longText":
                    if (raw.Length > question.MaxLength || !StorableText.IsStorable(raw.AsSpan())) return null;
                    result.Add(new(question.FieldId, question.Type, raw, null, null, null, null));
                    break;
                case "decimal":
                    if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                        || NormalizeDecimal(raw) != NormalizeDecimal(number.ToString("0.############################", CultureInfo.InvariantCulture))) return null;
                    result.Add(new(question.FieldId, question.Type, null, number, null, null, null));
                    break;
                case "date":
                    if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, date, null, null));
                    break;
                case "boolean":
                    if (!bool.TryParse(raw, out var boolean)) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, null, boolean, null));
                    break;
                case "choice":
                    if (!Guid.TryParseExact(raw, "D", out var choice) || question.Options?.All(option => option.ChoiceId != choice) != false) return null;
                    result.Add(new(question.FieldId, question.Type, null, null, null, null, choice));
                    break;
                default: return null;
            }
        }
        return result;
    }

    private static string Fingerprint(StructuredRecordCaptureRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            request.SourceReceiptId, request.DefinitionId, request.VersionId, request.ActorUserId,
            Answers = request.Answers.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { pair.Key, pair.Value }),
        });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string NormalizeDecimal(string value)
    {
        var negative = value[0] == '-';
        var unsigned = value.TrimStart('-', '+');
        var point = unsigned.IndexOf('.');
        var integer = point < 0 ? unsigned : unsigned[..point];
        var fraction = point < 0 ? "" : unsigned[(point + 1)..];
        integer = integer.TrimStart('0');
        fraction = fraction.TrimEnd('0');
        var normalized = (integer.Length == 0 ? "0" : integer) + (fraction.Length == 0 ? "" : "." + fraction);
        return negative && normalized != "0" ? "-" + normalized : normalized;
    }

    private sealed record NormalizedAnswer(Guid FieldId, string Type, string? Text, decimal? Decimal,
        DateOnly? Date, bool? Boolean, Guid? ChoiceId)
    {
        public object Value => Type switch
        {
            "shortText" or "longText" => Text!, "decimal" => Decimal!.Value, "date" => Date!.Value,
            "boolean" => Boolean!.Value, "choice" => ChoiceId!.Value, _ => throw new InvalidOperationException("Unknown form answer type."),
        };
    }
}
