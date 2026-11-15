using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The way out of a publication that stays unconfirmed: anyone who can publish closes it as failed. The version stays inactive and the
// channel port that retires unused versions takes its Flows down.
internal sealed class FormPublicationDiscardService(IFormDraftRepository drafts, IFormPublicationRepository publications,
    FormPublicationAccessGuard accessGuard, IUnitOfWork unitOfWork, TimeProvider timeProvider,
    ILogger<FormPublicationDiscardService> logger) : IFormPublicationDiscardService
{
    public Task<Result> DiscardAsync(Guid id, Guid requestId, DiscardFormPublicationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "DiscardFormPublication", async () =>
    {
        var access = await accessGuard.CheckAsync(cancellationToken); if (access.IsFailure) return access.Error;
        return await unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            await drafts.LockDefinitionsAsync(ct);
            var stillAllowed = await accessGuard.CheckAsync(ct); if (stillAllowed.IsFailure) return Result.Failure(stillAllowed.Error);
            var operation = await publications.GetOpenOperationAsync(id, requestId, ct);
            if (operation is null) return Result.Failure(FormPublicationErrors.NotFound);
            if (operation.DraftRevision != request.ExpectedRevision) return Result.Failure(FormDraftErrors.RevisionConflict);
            return operation.Discard(timeProvider.GetUtcNow().UtcDateTime);
        }, CommitPolicy.OnSuccess, cancellationToken);
    });
}
