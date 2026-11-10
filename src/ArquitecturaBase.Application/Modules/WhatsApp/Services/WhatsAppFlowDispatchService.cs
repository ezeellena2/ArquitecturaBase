using ArquitecturaBase.Application.Common.Logging;
using System.Security.Cryptography;
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

internal sealed partial class WhatsAppFlowDispatchService(IWhatsAppFlowRepository repository, IWhatsAppFlowReader reader,
    IWhatsAppFlowProtector protector, IUnitOfWork unitOfWork, IPermissionService permissions, TimeProvider timeProvider,
    ILogger<WhatsAppFlowDispatchService> logger) : IWhatsAppFlowDispatchService
{
    public async Task<IReadOnlyList<Guid>> ListDueOperationIdsAsync(CancellationToken cancellationToken)
    {
        OperationLog.Handling(logger, "ListDueWhatsAppFlowPublications");
        var ids = await reader.ListDueIdsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        OperationLog.Handled(logger, "ListDueWhatsAppFlowPublications");
        return ids;
    }

    public Task<Result<WhatsAppFlowWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<WhatsAppFlowWorkResponse?>>(logger, "ReserveWhatsAppFlowPublication", async () =>
    {
        if (operationId is not { } id) return Result.Success<WhatsAppFlowWorkResponse?>(null);
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
        WhatsAppFlowStoredManifest manifest;
        try { manifest = protector.ReadManifest(operation.Id, operation.NeutralOperationId, operation.VersionId, operation.ProtectedManifest); }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or JsonException)
        {
            DeferUnreadablePayload(operation, nowUtc);
            LogUnreadablePayload(logger, "manifest", exception.GetType().Name);
            return Result.Success<WhatsAppFlowWorkResponse?>(null);
        }
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
        WhatsAppFlowProjection projection;
        try { projection = protector.ReadProjection(operation.Id, binding.Id, operation.VersionId, binding.Culture, binding.ProtectedProjection); }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or JsonException)
        {
            DeferUnreadablePayload(operation, nowUtc);
            LogUnreadablePayload(logger, "projection", exception.GetType().Name);
            return Result.Success<WhatsAppFlowWorkResponse?>(null);
        }
        var step = binding.Begin(reserved.Value, nowUtc); if (step.IsFailure) return step.Error;
        return new WhatsAppFlowWorkResponse(operation.Id, operation.NeutralOperationId, binding.Id, reserved.Value, step.Value,
            binding.MetaFlowId, binding.TechnicalName, manifest, projection, operation.LeaseExpiresAtUtc!.Value);
    }
    public Task<Result> MarkEffectStartedAsync(WhatsAppFlowWorkResponse work, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "MarkWhatsAppFlowEffectStarted",
            () => unitOfWork.ExecuteInTransactionAsync(ct => MarkEffectStartedCoreAsync(work, ct), CommitPolicy.OnSuccess, cancellationToken));
    private async Task<Result> MarkEffectStartedCoreAsync(WhatsAppFlowWorkResponse work, CancellationToken ct)
    {
        await repository.LockAsync(work.NeutralOperationId, ct);
        var operation = await repository.GetByIdAsync(work.OperationId, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (operation is null || operation.NeutralOperationId != work.NeutralOperationId || !operation.OwnsLease(work.Fence, nowUtc)) return FlowErrors.StateConflict;
        var bindings = await repository.ListBindingsAsync(operation.Id, ct);
        var binding = bindings.SingleOrDefault(value => value.Id == work.BindingId);
        return binding is not null && binding.Fence == work.Fence ? binding.MarkEffectStarted(work.Fence) : FlowErrors.StateConflict;
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
    private static void DeferUnreadablePayload(WhatsAppFlowOperation operation, DateTime nowUtc) => operation.Release(nowUtc, false, true);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unreadable WhatsApp Flow {PayloadKind}; retry scheduled without altering external-effect uncertainty ({ExceptionType}).")]
    private static partial void LogUnreadablePayload(ILogger logger, string payloadKind, string exceptionType);
    // The subtitles of a sealed screen by position, and how many the screens before it have; none without titles. From r13 a
    // subtitle may go inside an If, shown with the questions of its section (CO1-A).
    private static (int Before, int[] Positions) Subtitles(string json, int screenIndex)
    {
        if (!json.Contains("\"TextSubheading\"", StringComparison.Ordinal)) return (0, []);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var screens = document.RootElement.GetProperty("screens").EnumerateArray().Select(screen => screen.GetProperty("layout").GetProperty("children")[0]
            .GetProperty("children").EnumerateArray().Select((child, at) => (Subtitle: IsSubtitle(child)
                || (child.GetProperty("type").GetString() == "If" && child.GetProperty("then").EnumerateArray().Any(IsSubtitle)), At: at))
            .Where(child => child.Subtitle).Select(child => child.At).ToArray()).ToArray();
        return screenIndex < screens.Length ? (screens.Take(screenIndex).Sum(positions => positions.Length), screens[screenIndex]) : (0, []);
    }
    private static bool IsSubtitle(JsonElement child) => child.GetProperty("type").GetString() == "TextSubheading";
    // LD2-A (r11): the positions of the dropdowns of values, each right before its question.
    private static int[] Pickers(IReadOnlyList<int> indices, IReadOnlyList<WhatsAppFlowProjectionField> mapping)
    {
        var positions = new List<int>();
        for (var order = 0; order < indices.Count; order++)
            if (mapping[indices[order]].ChoosesFirst == true) positions.Add(order + positions.Count);
        return [.. positions];
    }
    internal static WhatsAppFlowDiagnostic Map(WhatsAppFlowValidationPointer pointer, WhatsAppFlowProjection projection)
    {
        var culture = projection.Culture;
        if (pointer.Property == "name") return new(null, null, "name." + culture, culture);
        var screens = WhatsAppFlowScreenBuilder.Plan(projection.Mapping.Select(field =>
            new WhatsAppFlowSlot(field.Type, field.Type is "photo" or "document", field.Repeated == true, field.Rows, field.Calculated == true)).ToArray());
        if (pointer.ScreenIndex < 0 || pointer.ScreenIndex >= screens.Count || pointer.FieldIndex is not { } position || position < 0)
            return new(null, null, "channel", culture);
        // A section subtitle (CW15-B) points to its title; the components after it count without the subtitles before them.
        var (before, subtitles) = Subtitles(projection.Json, pointer.ScreenIndex);
        if (subtitles.Contains(position))
            return new(null, null, $"draft.sections[{before + subtitles.Count(at => at < position)}].title.{culture}", culture);
        position -= subtitles.Count(at => at < position);
        var screen = screens[pointer.ScreenIndex];
        IReadOnlyList<int> indices = screen.Kind == "ask" ? [] : screen.Questions;
        // The dropdown of values points to its question; the components after it count without it.
        var pickers = Pickers(indices, projection.Mapping);
        if (pickers.Contains(position))
        {
            var chosen = indices[position - pickers.Count(at => at < position)];
            return new(projection.Mapping[chosen].FieldId, null, $"draft.fields[{chosen}]", culture);
        }
        position -= pickers.Count(at => at < position);
        // After a row's questions comes the block's own question, alone on the screen that asks before the first row.
        var block = screen.Kind == "ask" ? screen.Questions[0] : screen.Kind == "row" ? screen.Questions[0] - 1 : -1;
        if (position == indices.Count && block >= 0 && (screen.Kind == "ask" || WhatsAppFlowScreenBuilder.Asks(projection.Mapping[block].Rows!, screen.Row)))
            return new(projection.Mapping[block].FieldId, null, $"draft.fields[{block}]", culture);
        if (position == indices.Count && pointer.Property == "label" && pointer.ScreenIndex == screens.Count - 1)
            return new(null, null, "draft.submitLabel." + culture, culture);
        if (position >= indices.Count) return new(null, null, "channel", culture);
        var index = indices[position];
        var field = projection.Mapping[index];
        if (pointer.ChoiceIndex is { } option && option >= 0 && option < field.Choices.Count)
            return new(field.FieldId, field.Choices[option], $"draft.fields[{index}].options[{option}].label.{culture}", culture);
        return pointer.Property switch
        {
            "label" => new(field.FieldId, null, $"draft.fields[{index}].label.{culture}", culture),
            "min-uploaded-photos" or "min-uploaded-documents" => new(field.FieldId, null, $"draft.fields[{index}].files.minFiles", culture),
            "max-uploaded-photos" or "max-uploaded-documents" => new(field.FieldId, null, $"draft.fields[{index}].files.maxFiles", culture),
            "photo-source" => new(field.FieldId, null, $"draft.fields[{index}].files.photoSource", culture),
            "helper-text" or "description" => new(field.FieldId, null, $"draft.fields[{index}].help.{culture}", culture),
            _ => new(field.FieldId, null, $"draft.fields[{index}]", culture),
        };
    }
}
