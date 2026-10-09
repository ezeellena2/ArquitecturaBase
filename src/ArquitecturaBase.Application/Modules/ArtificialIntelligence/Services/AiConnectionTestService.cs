using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
internal sealed class AiConnectionTestService(IAiConnectionReader reader, AiConnectionCandidatePolicy policy,
    ILlmClient client, IRequestValidator validator, ILogger<AiConnectionTestService> logger) : IAiConnectionTestService
{
    public Task<Result<AiModelsResponse>> ListModelsAsync(ListAiModelsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiModelsResponse>>(logger, "ListAiModels", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            var current = await reader.FindAsync(cancellationToken);
            if (current.Revision != request.ExpectedRevision) return AiConnectionErrors.RevisionConflict;
            var secret = policy.RevealCandidate(current, request.Provider, request.CredentialChange, request.ApiKey);
            if (secret.IsFailure) return secret.Error;
            var result = await client.ListModelsAsync(new(request.Provider, secret.Value), cancellationToken);
            return result.IsFailure ? result.Error : new AiModelsResponse(request.Provider, current.Revision, result.Value);
        });
    }
    public Task<Result<AiConnectionTestResponse>> TestAsync(TestAiConnectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OperationLog.RunAsync<Result<AiConnectionTestResponse>>(logger, "TestAiConnection", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            var current = await reader.FindAsync(cancellationToken);
            if (current.Revision != request.ExpectedRevision) return AiConnectionErrors.RevisionConflict;
            var secret = policy.RevealCandidate(current, request.Provider, request.CredentialChange, request.ApiKey);
            if (secret.IsFailure) return secret.Error;
            var result = await client.TestAsync(new LlmTestRequest(request.Provider, request.Model.Trim(), secret.Value), cancellationToken);
            return result.IsFailure ? result.Error : new AiConnectionTestResponse(current.Revision, result.Value.InputTokens, result.Value.OutputTokens);
        });
    }
}
