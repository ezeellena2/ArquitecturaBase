using ArquitecturaBase.Application.Common.Logging;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowDispatchService(IWhatsAppFlowRepository repository, IWhatsAppFlowReader reader,
    IWhatsAppFlowProtector protector, IUnitOfWork unitOfWork, IPermissionService permissions, TimeProvider timeProvider,
    ILogger<WhatsAppFlowDispatchService> logger) : IWhatsAppFlowDispatchService
{
    public Task<Result<WhatsAppFlowWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppFlowWorkResponse?>>(logger, "ReserveWhatsAppFlowPublication", async () =>
    {
        var ids = operationId.HasValue ? new[] { operationId.Value } : await reader.ListDueIdsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        var id = ids.Count == 0 ? Guid.Empty : ids[0];
        return await unitOfWork.ExecuteInTransactionAsync(ct => ReserveCoreAsync(id, ct), CommitPolicy.OnSuccess, cancellationToken);
    });
    private async Task<Result<WhatsAppFlowWorkResponse?>> ReserveCoreAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return Result.Success<WhatsAppFlowWorkResponse?>(null);
        // The reader returns neutral IDs; acquire the resource before loading tracked state.
        await repository.LockAsync(id, ct);
        var operation = await repository.GetAsync(id, ct); if (operation is null) return Result.Success<WhatsAppFlowWorkResponse?>(null);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var reserved = operation.Reserve(nowUtc, TimeSpan.FromMinutes(2));
        if (reserved.IsFailure) return Result.Success<WhatsAppFlowWorkResponse?>(null);
        var bindings = await repository.ListBindingsAsync(operation.Id, ct);
        var manifest = protector.ReadManifest(operation.Id, operation.NeutralOperationId, operation.VersionId, operation.ProtectedManifest);
        foreach (var permission in manifest.RequiredPermissions)
            if (!await permissions.HasPermissionAsync(operation.ActorUserId, permission, ct))
            {
                foreach (var pending in bindings.Where(binding => binding.State != WhatsAppFlowPublicationState.Published))
                    pending.SuspendAuthorization();
                Release(operation, bindings, nowUtc); return Result.Success<WhatsAppFlowWorkResponse?>(null);
            }
        var binding = bindings.OrderByDescending(value => value.State == WhatsAppFlowPublicationState.Uncertain || value.InFlight)
            .ThenBy(value => value.Culture, StringComparer.Ordinal)
            .FirstOrDefault(value => value.State is not (WhatsAppFlowPublicationState.Published or WhatsAppFlowPublicationState.Failed));
        if (binding is null || bindings.Any(value => value.State == WhatsAppFlowPublicationState.Failed)
            && binding.State != WhatsAppFlowPublicationState.Uncertain && !binding.InFlight)
        {
            operation.Release(nowUtc, true, false); return Result.Success<WhatsAppFlowWorkResponse?>(null);
        }
        var step = binding.Begin(reserved.Value, nowUtc); if (step.IsFailure) return step.Error;
        var projection = protector.ReadProjection(operation.Id, binding.Id, operation.VersionId, binding.Culture, binding.ProtectedProjection);
        return new WhatsAppFlowWorkResponse(operation.Id, operation.NeutralOperationId, binding.Id, reserved.Value, step.Value,
            binding.MetaFlowId, binding.TechnicalName, manifest, projection, operation.LeaseExpiresAtUtc!.Value);
    }
    public Task<Result> CompleteEffectAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CompleteWhatsAppFlowEffect",
            () => unitOfWork.ExecuteInTransactionAsync(ct => CompleteEffectCoreAsync(work, response, ct), CommitPolicy.OnSuccess, cancellationToken));
    private async Task<Result> CompleteEffectCoreAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowEffectResponse response, CancellationToken ct)
    {
        await repository.LockAsync(work.NeutralOperationId, ct);
        var operation = await repository.GetByIdAsync(work.OperationId, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (operation is null || operation.NeutralOperationId != work.NeutralOperationId || !operation.OwnsLease(work.Fence, nowUtc)) return FlowErrors.StateConflict;
        var bindings = await repository.ListBindingsAsync(operation.Id, ct);
        var binding = bindings.SingleOrDefault(value => value.Id == work.BindingId);
        if (binding is null || binding.Fence != work.Fence) return FlowErrors.StateConflict;
        if (response.FailureCode is { } code)
        {
            if (code == FlowErrors.AccountUnavailable.Code)
            {
                var retry = binding.RetryWithoutEffect(work.Fence);
                if (retry.IsFailure) return retry.Error;
            }
            else if (code == FlowErrors.AccountMismatch.Code && (work.Step != WhatsAppFlowStep.Create || binding.MetaFlowId is not null))
            {
                var retry = binding.PreserveUncertainWithoutEffect(work.Fence);
                if (retry.IsFailure) return retry.Error;
            }
            else binding.Reject(WhatsAppFlowPublicationPolicy.ErrorFor(code).Code, "[]");
        }
        else
        {
            var recorded = binding.RecordEffect(work.Fence, nowUtc, response.MetaFlowId, response.Success, response.ValidationErrors.Count > 0, response.Uncertain);
            if (recorded.IsFailure) return recorded.Error;
            if (response.ValidationErrors.Count > 0)
            {
                var diagnostics = response.ValidationErrors.Select(pointer => Map(pointer, work.Projection)).ToArray();
                binding.SetDiagnostics(JsonSerializer.Serialize(diagnostics, JsonSerializerOptions.Web));
            }
        }
        Release(operation, bindings, nowUtc); return Result.Success();
    }
    public Task<Result> CompleteVerificationAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowVerificationResponse response, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CompleteWhatsAppFlowVerification",
            () => unitOfWork.ExecuteInTransactionAsync(ct => CompleteVerificationCoreAsync(work, response, ct), CommitPolicy.OnSuccess, cancellationToken));
    private async Task<Result> CompleteVerificationCoreAsync(WhatsAppFlowWorkResponse work, WhatsAppFlowVerificationResponse response, CancellationToken ct)
    {
        await repository.LockAsync(work.NeutralOperationId, ct);
        var operation = await repository.GetByIdAsync(work.OperationId, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (operation is null || operation.NeutralOperationId != work.NeutralOperationId || !operation.OwnsLease(work.Fence, nowUtc)) return FlowErrors.StateConflict;
        var bindings = await repository.ListBindingsAsync(operation.Id, ct);
        var binding = bindings.SingleOrDefault(value => value.Id == work.BindingId);
        if (binding is null || binding.Fence != work.Fence) return FlowErrors.StateConflict;
        var recorded = binding.RecordVerification(work.Fence, nowUtc, response.Published, response.Draft, response.AssetAndOwnerMatch, response.Validated, response.Healthy);
        if (recorded.IsFailure) return recorded.Error;
        if (work.MetaFlowId is null && response.CreationCandidateCount is { } count)
        {
            var kind = count == 0 ? "none" : count == 1 ? "one" : "multiple";
            binding.SetDiagnostics(JsonSerializer.Serialize(new[] { new WhatsAppFlowDiagnostic(null, null, "channel.creationCandidates." + kind, work.Projection.Culture) }, JsonSerializerOptions.Web));
        }
        Release(operation, bindings, nowUtc); return Result.Success();
    }
    private static void Release(WhatsAppFlowOperation operation, IReadOnlyList<WhatsAppFlowBinding> bindings, DateTime nowUtc) => operation.Release(nowUtc,
        !bindings.Any(binding => binding.State == WhatsAppFlowPublicationState.Uncertain || binding.InFlight)
            && (bindings.Any(binding => binding.State == WhatsAppFlowPublicationState.Failed) || bindings.All(binding => binding.State == WhatsAppFlowPublicationState.Published)),
        bindings.Any(binding => binding.State == WhatsAppFlowPublicationState.Uncertain));
    private static WhatsAppFlowDiagnostic Map(WhatsAppFlowValidationPointer pointer, WhatsAppFlowProjection projection)
    {
        var culture = projection.Culture;
        if (pointer.Property == "name") return new(null, null, "name." + culture, culture);
        if (pointer.FieldIndex == projection.Mapping.Count && pointer.Property == "label") return new(null, null, "draft.submitLabel." + culture, culture);
        if (pointer.FieldIndex is not { } index || index < 0 || index >= projection.Mapping.Count) return new(null, null, "channel", culture);
        var field = projection.Mapping[index];
        if (pointer.ChoiceIndex is { } option && option >= 0 && option < field.Choices.Count)
            return new(field.FieldId, field.Choices[option], $"draft.fields[{index}].options[{option}].label.{culture}", culture);
        return pointer.Property switch
        {
            "label" => new(field.FieldId, null, $"draft.fields[{index}].label.{culture}", culture),
            "helper-text" or "description" => new(field.FieldId, null, $"draft.fields[{index}].help.{culture}", culture),
            _ => new(field.FieldId, null, $"draft.fields[{index}]", culture),
        };
    }
}
