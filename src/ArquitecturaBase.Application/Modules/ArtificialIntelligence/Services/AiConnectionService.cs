using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionService(IAiConnectionRepository repository, IAiConnectionReader reader, IUnitOfWork unitOfWork,
    IRequestValidator validator, AiConnectionCandidatePolicy policy, AiConnectionChangeRecorder recorder,
    ICurrentUser currentUser, ILogger<AiConnectionService> logger) : IAiConnectionService
{
    public Task<Result<AiConnectionResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiConnectionResponse>>(logger, "GetAiConnection", async () =>
            (await reader.FindAsync(cancellationToken)).ToResponse());
    public Task<Result<AiConnectionChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiConnectionChangeResponse>>(logger, "GetAiConnectionChange", async () =>
        {
            if (currentUser.UserId is not { } userId) return AiConnectionErrors.SessionRequired;
            var change = await reader.FindChangeAsync(userId, requestId, cancellationToken);
            return change is null ? AiConnectionErrors.ChangeNotFound : Result.Success(change);
        });
    public Task<Result<AiConnectionChangeResponse>> UpdateAsync(UpdateAiConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiConnectionChangeResponse>>(logger, "UpdateAiConnection", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (currentUser.UserId is not { } userId) return AiConnectionErrors.SessionRequired;
            return await unitOfWork.ExecuteInTransactionAsync(ct => UpdateCoreAsync(request, userId, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }
    private async Task<Result<AiConnectionChangeResponse>> UpdateCoreAsync(UpdateAiConnectionRequest request, Guid operatorId, CancellationToken ct)
    {
        await repository.LockAsync(ct);
        var previous = await repository.GetChangeAsync(operatorId, request.RequestId, ct);
        if (previous is not null)
            return recorder.Matches(request, previous) ? Result.Success(AiConnectionChangeRecorder.Response(previous)) : AiConnectionErrors.RequestConflict;
        var connection = await repository.GetAsync(ct);
        var current = connection is null ? new AiConnectionSnapshot(AiProvider.OpenAI, "", null, false, 0)
            : new AiConnectionSnapshot(connection.Provider, connection.Model, connection.ProtectedApiKey, connection.Enabled, connection.Revision);
        if (current.Revision != request.ExpectedRevision) return AiConnectionErrors.RevisionConflict;
        var credential = policy.Resolve(current, request.Provider, request.CredentialChange, request.ApiKey);
        if (credential.IsFailure) return credential.Error;
        var created = connection is null;
        connection ??= AiConnection.Create();
        var result = connection.Update(request.Provider, request.Model, credential.Value,
            request.CredentialChange != AiCredentialChange.Remove && request.Enabled);
        if (result.IsFailure) return result.Error;
        if (created) repository.Add(connection);
        await repository.DeleteExpiredChangesAsync(ct);
        var receipt = recorder.Create(request, operatorId, connection);
        repository.AddChange(receipt);
        return AiConnectionChangeRecorder.Response(receipt);
    }
}
