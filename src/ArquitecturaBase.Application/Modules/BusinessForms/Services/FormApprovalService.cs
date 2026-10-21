using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormApprovalService(IFormApprovalRepository repository, IFormApprovalReader reader, FormApprovalAccessGuard access,
    IUnitOfWork unitOfWork, IRequestValidator validator, TimeProvider timeProvider, ILogger<FormApprovalService> logger, FormActionRunRecorder runs) : IFormApprovalService
{
    public Task<Result<PagedResult<FormApprovalTaskResponse>>> ListAsync(ListFormApprovalsRequest request, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<PagedResult<FormApprovalTaskResponse>>>(logger, "ListFormApprovals", async () =>
    {
        var actor = await access.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var page = await reader.ListAsync(actor.Value, request, Culture(), cancellationToken);
        return await access.StillAllowedAsync(actor.Value, cancellationToken) ? page : FormDefinitionErrors.Forbidden;
    });
    public Task<Result<FormApprovalCountResponse>> CountAsync(FormApprovalFilterRequest request, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormApprovalCountResponse>>(logger, "CountFormApprovals", async () =>
    {
        var actor = await access.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (request.DefinitionId == Guid.Empty) return FieldErrors.On(FormApprovalErrors.ContentInvalid, "definitionId");
        var count = await reader.CountAsync(actor.Value, request, cancellationToken);
        return await access.StillAllowedAsync(actor.Value, cancellationToken) ? new FormApprovalCountResponse(count) : FormDefinitionErrors.Forbidden;
    });
    public Task<Result<FormApprovalTaskResponse>> GetAsync(Guid id, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormApprovalTaskResponse>>(logger, "ReadFormApproval", async () =>
    {
        var actor = await access.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var task = await reader.FindAsync(id, actor.Value, Culture(), cancellationToken);
        if (!await access.StillAllowedAsync(actor.Value, cancellationToken)) return FormDefinitionErrors.Forbidden;
        return task is null ? FormApprovalErrors.NotFound : task;
    });
    public Task<Result<FormApprovalDecisionResponse>> DecideAsync(Guid id, DecideFormApprovalRequest request, CancellationToken cancellationToken) => OperationLog.RunAsync<Result<FormApprovalDecisionResponse>>(logger, "DecideFormApproval", async () =>
    {
        var actor = await access.CheckAsync(cancellationToken);
        if (actor.IsFailure) return actor.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await repository.LockAsync(ct);
            if (!await access.StillAllowedAsync(actor.Value, ct)) return Result.Failure<FormApprovalDecisionResponse>(FormDefinitionErrors.Forbidden);
            var task = await repository.GetAsync(id, ct);
            if (task is null) return Result.Failure<FormApprovalDecisionResponse>(FormApprovalErrors.NotFound);
            var person = await reader.FindActorAsync(actor.Value, task.RoleId, ct);
            if (person is not { IsActive: true, IsInRole: true }) return Result.Failure<FormApprovalDecisionResponse>(FormApprovalErrors.RoleChanged);
            var record = await repository.GetRecordAsync(task.RecordId, ct);
            if (record is null || record.RuleSetId != task.RuleSetId) return Result.Failure<FormApprovalDecisionResponse>(FormApprovalErrors.RevisionConflict);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var approved = request.Decision == "Approved";
            var decided = task.Decide(request.ExpectedRevision!.Value, actor.Value, approved, request.Reason, now);
            if (decided.IsFailure) return Result.Failure<FormApprovalDecisionResponse>(decided.Error);
            var completed = record.CompleteApproval(approved);
            if (completed.IsFailure) return Result.Failure<FormApprovalDecisionResponse>(completed.Error);
            var snapshot = await reader.FindActionSnapshotAsync(record.Id, ct) ?? throw new InvalidOperationException("The response snapshot for the approval is missing.");
            await runs.RecordAsync(record, snapshot with { Decision = new(task.Id, actor.Value, person.DisplayName, now, request.Decision!, task.Reason) }, request.Decision!, ct);
            return Result.Success(new FormApprovalDecisionResponse(task.Id, record.Id, task.Revision, task.State.ToString(), now, new(actor.Value, person.DisplayName), task.Reason));
        }, CommitPolicy.OnSuccess, cancellationToken);
    });
    private static string Culture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
}
