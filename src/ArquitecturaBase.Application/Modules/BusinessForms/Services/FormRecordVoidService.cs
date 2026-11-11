using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// Voids a current response under the definitions lock, the one approvals take: a decision cannot cross it. What already
/// happened stays (notices, PDF); only the open approval task closes (<see cref="FormRecordVoidRecorder"/>). It is internal
/// configuration of a response, so a changed response answers 409 and nothing is retried. A response with a live issuance is not
/// voided here (NC1-A): <see cref="FormIssueReversalRecorder"/> starts a reversal run for each one, that the worker carries outside
/// any transaction, and the response is voided when it ends. That takes, besides voiding, the permission of the issuer.
/// </summary>
internal sealed class FormRecordVoidService(FormRecordAmendmentGuard access, IFormRecordRepository records, IUnitOfWork unitOfWork,
    IRequestValidator validator, TimeProvider timeProvider, ILogger<FormRecordVoidService> logger, FormIssueReversalRecorder reversals,
    FormRecordVoidRecorder voids) : IFormRecordVoidService
{
    public Task<Result> VoidAsync(Guid definitionId, Guid recordId, VoidFormRecordRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "VoidFormRecord", async () =>
        {
            var actor = await access.CheckAsync(cancellationToken); if (actor.IsFailure) return actor.Error;
            if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
            return await unitOfWork.ExecuteInTransactionAsync<Result>(async ct =>
            {
                await records.LockDefinitionsAsync(ct);
                if (!await access.StillAllowedAsync(actor.Value, ct)) return FormDefinitionErrors.Forbidden;
                var record = await records.GetRecordAsync(recordId, ct);
                if (record is null || record.DefinitionId != definitionId) return FormRecordErrors.NotFound;
                var allowed = FormRecordAmendmentGuard.Allows(actor.Value, record.ActorUserId, record.State, record.ReplacedByRecordId is not null);
                if (allowed.IsFailure) return allowed.Error;
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var reversed = await reversals.ReverseAsync(record, actor.Value.Actor, request.Reason, now, ct);
                if (reversed is not null) return reversed;
                return await voids.VoidAsync(record, actor.Value.Actor, request.Reason, null, now, ct);
            }, CommitPolicy.OnSuccess, cancellationToken);
        });
}
