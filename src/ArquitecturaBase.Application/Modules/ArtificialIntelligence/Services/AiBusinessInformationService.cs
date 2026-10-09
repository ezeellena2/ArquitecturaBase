using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiBusinessInformationService(IAiBusinessInformationRepository repository, IAiBusinessInformationReader reader,
    IUnitOfWork unitOfWork, IRequestValidator validator, IAiBusinessInformationFingerprint fingerprint,
    ICurrentUser currentUser, TimeProvider timeProvider, ILogger<AiBusinessInformationService> logger) : IAiBusinessInformationService
{
    public Task<Result<AiBusinessInformationResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiBusinessInformationResponse>>(logger, "GetAiBusinessInformation", async () => await reader.FindAsync(cancellationToken));
    public Task<Result<AiBusinessInformationChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiBusinessInformationChangeResponse>>(logger, "GetAiBusinessInformationChange", async () =>
        {
            if (currentUser.UserId is not { } userId) return AiBusinessInformationErrors.SessionRequired;
            var change = await reader.FindChangeAsync(userId, requestId, cancellationToken);
            return change is null ? AiBusinessInformationErrors.ChangeNotFound : Result.Success(change);
        });
    public Task<Result<AiBusinessInformationChangeResponse>> UpdateAsync(UpdateAiBusinessInformationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiBusinessInformationChangeResponse>>(logger, "UpdateAiBusinessInformation", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (currentUser.UserId is not { } userId) return AiBusinessInformationErrors.SessionRequired;
            return await unitOfWork.ExecuteInTransactionAsync(ct => UpdateCoreAsync(request, userId, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }
    private async Task<Result<AiBusinessInformationChangeResponse>> UpdateCoreAsync(UpdateAiBusinessInformationRequest request, Guid operatorId, CancellationToken ct)
    {
        await repository.LockAsync(ct);
        var previous = await repository.GetChangeAsync(operatorId, request.RequestId, ct);
        if (previous is not null)
            return fingerprint.Matches(request, previous.KeyId, previous.Fingerprint)
                ? Result.Success(new AiBusinessInformationChangeResponse(previous.RequestId, previous.Revision)) : AiBusinessInformationErrors.RequestConflict;
        var information = await repository.GetAsync(ct);
        if ((information?.Revision ?? 0) != request.ExpectedRevision) return AiBusinessInformationErrors.RevisionConflict;
        var created = information is null;
        information ??= AiBusinessInformation.Create();
        var result = information.Update(request.Name, request.Description, request.Hours, request.Location, request.Services, request.Policies, request.Faq, request.Details);
        if (result.IsFailure) return result.Error;
        if (created) repository.Add(information);
        await repository.DeleteExpiredChangesAsync(ct);
        var receipt = AiBusinessInformationChange.Create(request.RequestId, operatorId, fingerprint.Create(request, fingerprint.ActiveKeyId),
            fingerprint.ActiveKeyId, information.Revision, timeProvider.GetUtcNow().UtcDateTime);
        repository.AddChange(receipt);
        return new AiBusinessInformationChangeResponse(receipt.RequestId, receipt.Revision);
    }
}
