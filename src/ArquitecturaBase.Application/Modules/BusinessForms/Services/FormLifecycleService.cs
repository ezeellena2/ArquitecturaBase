using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormLifecycleService(IFormDraftRepository drafts, IFormPublicationRepository publications, IFormLifecycleReader reader,
    FormLifecycleAccessGuard access, IUnitOfWork unitOfWork, TimeProvider clock, ILogger<FormLifecycleService> logger, IStructuredFormChannelStateSource channelState) : IFormLifecycleService
{
    public Task<Result> DeleteAsync(Guid id, DeleteFormRequest request, CancellationToken ct) =>
        OperationLog.RunAsync<Result>(logger, "DeleteForm", async () =>
    {
        var actor = await access.CheckDeletionAsync(ct); if (actor.IsFailure) return actor.Error;
        if (await access.ValidateAsync(request, ct) is {} invalid) return invalid;
        return await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await drafts.LockDefinitionsAsync(innerCt);
            if (!await access.StillAllowedDeletionAsync(actor.Value, innerCt)) return Result.Failure(FormDefinitionErrors.Forbidden);
            var definition = await drafts.GetAsync(id, innerCt); if (definition is null) return Result.Failure(FormDefinitionErrors.NotFound);
            if (definition.Revision != request.ExpectedRevision) return Result.Failure(FormDraftErrors.RevisionConflict);
            if (await publications.ExistsOpenOperationAsync(id, innerCt)) return Result.Failure(FormPublicationErrors.InProgress);
            if (definition.LastRecordNumber > 0) return Result.Failure(FormDefinitionErrors.HasResponses);
            var nowUtc = clock.GetUtcNow().UtcDateTime;
            var state = (await channelState.ListAsync([id], nowUtc, innerCt)).SingleOrDefault(value => value.DefinitionId == id);
            if (FormDefinitionLifecyclePolicy.HasOpenSends(state, nowUtc)) return Result.Failure(FormDefinitionErrors.OpenSends);
            drafts.Remove(definition);
            return Result.Success();
        }, CommitPolicy.OnSuccess, ct);
    });
    public Task<Result<FormDeactivationPreviewResponse>> GetDeactivationAsync(Guid id, CancellationToken ct) =>
        OperationLog.RunAsync<Result<FormDeactivationPreviewResponse>>(logger, "ReadFormDeactivation", async () =>
    {
        var actor = await access.CheckAsync(ct); if (actor.IsFailure) return actor.Error;
        var row = (await reader.ListFactsAsync([id], ct)).SingleOrDefault(value => value.DefinitionId == id);
        if (row is null) return FormDefinitionErrors.NotFound;
        var incoming = await reader.ListIncomingSuggestionsAsync(id, ct);
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var state = (await channelState.ListAsync([id], nowUtc, ct)).SingleOrDefault(value => value.DefinitionId == id);
        var canManage = await access.CanManageAsync(actor.Value, ct);
        var canReadRecords = await access.CanReadRecordsAsync(actor.Value, ct);
        if (!await access.StillAllowedAsync(actor.Value, ct)) return FormDefinitionErrors.Forbidden;
        return new FormDeactivationPreviewResponse(id, row.Revision, FormDefinitionLifecyclePolicy.View(row, state, nowUtc, canManage, true, canReadRecords), incoming);
    });
    public Task<Result<FormDeactivationResponse>> DeactivateAsync(Guid id, DeactivateFormRequest request, CancellationToken ct) =>
        OperationLog.RunAsync<Result<FormDeactivationResponse>>(logger, "DeactivateForm", async () =>
    {
        var actor = await access.CheckAsync(ct); if (actor.IsFailure) return actor.Error;
        if (await access.ValidateAsync(request, ct) is {} invalid) return invalid;
        long committedRevision = 0; DateTime deactivatedAtUtc = default;
        var changed = await unitOfWork.ExecuteInTransactionAsync(async innerCt =>
        {
            await drafts.LockDefinitionsAsync(innerCt);
            if (!await access.StillAllowedAsync(actor.Value, innerCt)) return Result.Failure(FormDefinitionErrors.Forbidden);
            var definition = await drafts.GetAsync(id, innerCt); if (definition is null) return Result.Failure(FormDefinitionErrors.NotFound);
            if (definition.Revision != request.ExpectedRevision) return Result.Failure(FormDraftErrors.RevisionConflict);
            if (await publications.ExistsOpenOperationAsync(id, innerCt)) return Result.Failure(FormPublicationErrors.InProgress);
            var deactivated = definition.Deactivate(request.ExpectedRevision, actor.Value, clock.GetUtcNow().UtcDateTime);
            if (deactivated.IsSuccess) { committedRevision = definition.Revision; deactivatedAtUtc = definition.DeactivatedAtUtc!.Value; }
            return deactivated;
        }, CommitPolicy.OnSuccess, ct);
        if (changed.IsFailure) return changed.Error;
        var row = (await reader.ListFactsAsync([id], ct)).SingleOrDefault(value => value.DefinitionId == id);
        if (row is null) return FormDefinitionErrors.NotFound;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var state = (await channelState.ListAsync([id], nowUtc, ct)).SingleOrDefault(value => value.DefinitionId == id);
        var canManage = await access.CanManageAsync(actor.Value, ct);
        var canReadRecords = await access.CanReadRecordsAsync(actor.Value, ct);
        if (!await access.StillAllowedAsync(actor.Value, ct)) return FormDefinitionErrors.Forbidden;
        var ownChange = row with { Revision = committedRevision, DeactivatedAtUtc = deactivatedAtUtc,
            DeactivatedBy = new(actor.Value, row.DeactivatedBy?.UserId == actor.Value ? row.DeactivatedBy.DisplayName : null) };
        return new FormDeactivationResponse(id, committedRevision, FormDefinitionLifecyclePolicy.View(ownChange, state, nowUtc, canManage, true, canReadRecords));
    });
}
