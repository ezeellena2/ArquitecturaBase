using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed partial class WhatsAppFlowDeprecationService(IWhatsAppFlowRepository repository, IWhatsAppFlowReader reader,
    IWhatsAppFlowProtector protector, IStructuredFormRetirementSource retirement, IUnitOfWork unitOfWork,
    TimeProvider timeProvider, ILogger<WhatsAppFlowDeprecationService> logger) : IWhatsAppFlowDeprecationService
{
    public Task<IReadOnlyList<Guid>> ListDueBindingIdsAsync(CancellationToken cancellationToken) =>
        reader.ListDueDeprecationBindingsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);

    public Task<Result<WhatsAppFlowDeprecationWorkResponse?>> ReserveAsync(Guid? bindingId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppFlowDeprecationWorkResponse?>>(logger, "ReserveWhatsAppFlowDeprecation", () =>
            unitOfWork.ExecuteInTransactionAsync(ct => ReserveCoreAsync(bindingId, ct), CommitPolicy.OnSuccess, cancellationToken));

    private async Task<Result<WhatsAppFlowDeprecationWorkResponse?>> ReserveCoreAsync(Guid? bindingId, CancellationToken ct)
    {
        if (bindingId is null || bindingId == Guid.Empty) return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        await retirement.LockSelectionAsync(ct);
        var row = await reader.FindDeprecationAsync(bindingId.Value, ct);
        if (row is null) return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        await repository.LockAsync(row.NeutralOperationId, ct);
        var operation = await repository.GetByIdAsync(row.OperationId, ct);
        if (operation is null || operation.NeutralOperationId != row.NeutralOperationId
            || operation.DefinitionId != row.DefinitionId || operation.VersionId != row.VersionId)
            return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        var binding = (await repository.ListBindingsAsync(operation.Id, ct)).SingleOrDefault(value => value.Id == row.BindingId);
        if (binding is null || binding.VersionId != operation.VersionId || binding.State != WhatsAppFlowPublicationState.Published
            || binding.MetaFlowId is null || binding.DeprecationState == WhatsAppFlowDeprecationState.Deprecated)
            return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!await IsUnusedAsync(operation.DefinitionId, operation.VersionId, ct))
        {
            binding.DeferDeprecationCheck(nowUtc);
            return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        }
        if (await reader.ExistsOpenVersionWorkAsync(operation.VersionId, nowUtc, ct))
            return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        var reserved = binding.ReserveDeprecation(nowUtc, TimeSpan.FromMinutes(2));
        if (reserved.IsFailure) return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        WhatsAppFlowStoredManifest manifest;
        try { manifest = protector.ReadManifest(operation.Id, operation.NeutralOperationId, operation.VersionId, operation.ProtectedManifest); }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or JsonException)
        {
            binding.RecordDeprecationFailure(reserved.Value, nowUtc);
            LogUnreadableManifest(logger, exception.GetType().Name);
            return Result.Success<WhatsAppFlowDeprecationWorkResponse?>(null);
        }
        return new WhatsAppFlowDeprecationWorkResponse(operation.Id, operation.NeutralOperationId, binding.Id,
            operation.DefinitionId, operation.VersionId, reserved.Value,
            binding.DeprecationInFlight ? WhatsAppFlowDeprecationStep.Verify : WhatsAppFlowDeprecationStep.Deprecate,
            binding.MetaFlowId, binding.TechnicalName, manifest, binding.DeprecationLeaseExpiresAtUtc!.Value);
    }

    public Task<Result> MarkEffectStartedAsync(WhatsAppFlowDeprecationWorkResponse work, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "MarkWhatsAppFlowDeprecationStarted", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var binding = await LoadOwnedBindingAsync(work, ct);
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                if (binding is null || work.Step != WhatsAppFlowDeprecationStep.Deprecate
                    || !await IsUnusedAsync(work.DefinitionId, work.VersionId, ct)
                    || await reader.ExistsOpenVersionWorkAsync(work.VersionId, nowUtc, ct)) return Result.Failure(FlowErrors.StateConflict);
                return binding.MarkDeprecationStarted(work.Fence, nowUtc);
            }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> CompleteEffectAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CompleteWhatsAppFlowDeprecation", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var binding = await LoadOwnedBindingAsync(work, ct);
                if (binding is null) return Result.Failure(FlowErrors.StateConflict);
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                if (response.FailureCode == FlowErrors.DeprecationRejected.Code)
                    return binding.RecordDeprecationRejection(work.Fence, nowUtc);
                if (response.FailureCode is not null)
                    return binding.RecordDeprecationFailure(work.Fence, nowUtc);
                return binding.RecordDeprecationEffect(work.Fence, nowUtc, response.Success, response.Uncertain);
            }, CommitPolicy.OnSuccess, cancellationToken));

    public Task<Result> CompleteVerificationAsync(WhatsAppFlowDeprecationWorkResponse work, WhatsAppFlowStateResponse response, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CompleteWhatsAppFlowDeprecationVerification", () =>
            unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                var binding = await LoadOwnedBindingAsync(work, ct);
                if (binding is null || work.Step != WhatsAppFlowDeprecationStep.Verify) return Result.Failure(FlowErrors.StateConflict);
                return binding.RecordDeprecationVerification(work.Fence, timeProvider.GetUtcNow().UtcDateTime,
                    response.IdentityMatches, response.State == WhatsAppFlowRemoteState.Published, response.State == WhatsAppFlowRemoteState.Deprecated);
            }, CommitPolicy.OnSuccess, cancellationToken));

    private async Task<bool> IsUnusedAsync(Guid definitionId, Guid versionId, CancellationToken ct) =>
        (await retirement.ListUnusedAsync([new StructuredFormVersionReference(definitionId, versionId)], ct))
            .Any(value => value.DefinitionId == definitionId && value.VersionId == versionId);

    private async Task<WhatsAppFlowBinding?> LoadOwnedBindingAsync(WhatsAppFlowDeprecationWorkResponse work, CancellationToken ct)
    {
        await retirement.LockSelectionAsync(ct);
        await repository.LockAsync(work.NeutralOperationId, ct);
        var operation = await repository.GetByIdAsync(work.OperationId, ct);
        if (operation is null || operation.NeutralOperationId != work.NeutralOperationId || operation.DefinitionId != work.DefinitionId
            || operation.VersionId != work.VersionId) return null;
        var binding = (await repository.ListBindingsAsync(operation.Id, ct)).SingleOrDefault(value => value.Id == work.BindingId);
        return binding is not null && binding.VersionId == work.VersionId && binding.MetaFlowId == work.MetaFlowId
            && binding.TechnicalName == work.TechnicalName && binding.OwnsDeprecationLease(work.Fence, timeProvider.GetUtcNow().UtcDateTime) ? binding : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable WhatsApp Flow retirement manifest; retry preserves external-effect uncertainty ({ExceptionType}).")]
    private static partial void LogUnreadableManifest(ILogger logger, string exceptionType);
}
