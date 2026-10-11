using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Models.StructuredData;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed partial class FormPublicationDispatchService(IFormDraftRepository drafts, IFormPublicationRepository repository,
    IFormPublicationReader reader, IFormPublicationProtector protector, IUnitOfWork unitOfWork,
    IPermissionService permissions, TimeProvider timeProvider, ILogger<FormPublicationDispatchService> logger) : IFormPublicationDispatchService
{
    private static readonly string[] RequiredPermissions = [BusinessFormsPermissions.Read, BusinessFormsPermissions.Publish];
    public Task<Result<FormPublicationWorkResponse?>> ReserveAsync(Guid? operationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPublicationWorkResponse?>>(logger, "ReserveFormPublication", async () =>
    {
        var ids = operationId.HasValue ? new[] { operationId.Value } : await reader.ListDueIdsAsync(timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        foreach (var id in ids)
        {
            var result = await unitOfWork.ExecuteInTransactionAsync(ct => ReserveCoreAsync(id, ct), CommitPolicy.OnSuccess, cancellationToken);
            if (result.IsFailure || result.Value is not null || operationId.HasValue) return result;
        }
        return Result.Success<FormPublicationWorkResponse?>(null);
    });
    private async Task<Result<FormPublicationWorkResponse?>> ReserveCoreAsync(Guid id, CancellationToken ct)
    {
        if (id == Guid.Empty) return Result.Success<FormPublicationWorkResponse?>(null);
        await drafts.LockDefinitionsAsync(ct);
        var operation = await repository.GetByIdAsync(id, ct); if (operation is null) return Result.Success<FormPublicationWorkResponse?>(null);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var previousState = operation.State;
        var reserved = operation.Reserve(nowUtc, TimeSpan.FromMinutes(2)); if (reserved.IsFailure) return Result.Success<FormPublicationWorkResponse?>(null);
        var reconciliationOnly = false;
        if (!await AllowedAsync(operation.OperatorUserId, ct))
        {
            if (previousState == FormPublicationState.Accepted)
            {
                operation.RecordChannelResult(reserved.Value, nowUtc, "Failed", "AuthorizationChanged");
                return Result.Success<FormPublicationWorkResponse?>(null);
            }
            reconciliationOnly = true;
        }
        FormPublicationManifest manifest;
        FormVersionSchema schema;
        try
        {
            manifest = protector.ReadManifest(new(operation.Id, operation.OperatorUserId, operation.RequestId, operation.DefinitionId, operation.VersionId), operation.ProtectedManifest);
            var version = await repository.GetVersionAsync(operation.VersionId, ct) ?? throw new InvalidOperationException("The sealed publication version is missing.");
            schema = JsonSerializer.Deserialize<FormVersionSchema>(version.SchemaJson, JsonSerializerOptions.Web) ?? throw new InvalidOperationException("The sealed publication schema is invalid.");
            if (manifest.ContentHash != FormPublicationSnapshotPolicy.Hash(FormDraftPolicy.Serialize(schema))) throw new InvalidOperationException("The sealed publication schema does not match its manifest.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or CryptographicException or JsonException)
        {
            var deferred = operation.RecordChannelResult(reserved.Value, nowUtc, "Uncertain", "ManifestUnreadable");
            if (deferred.IsFailure) return Result.Failure<FormPublicationWorkResponse?>(deferred.Error);
            ManifestUnreadable(logger, operation.Id, exception.GetType().Name);
            return Result.Success<FormPublicationWorkResponse?>(null);
        }
        var channelRequests = CreateChannelRequests(operation, manifest, schema);
        return new FormPublicationWorkResponse(operation.Id, operation.DefinitionId, operation.VersionId, operation.OperatorUserId, reserved.Value,
            manifest, schema, channelRequests, reconciliationOnly);
    }
    private static FormPublicationChannelRequest[] CreateChannelRequests(BusinessFormPublicationOperation operation,
        FormPublicationManifest manifest, FormVersionSchema schema)
    {
        var targetCultures = manifest.Profiles.SelectMany(profile => profile.Cultures).Select(culture => culture.Culture)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var snapshot = new StructuredFormCheckRequest(schema.Format, manifest.OriginalCulture, manifest.Name, manifest.SubmitLabel,
            schema.Questions.Select(field => new StructuredFormQuestion(field.FieldId, field.Key, field.Type, field.Label, field.Help,
                field.Required, field.MaxLength, field.Options?.Select(option => new StructuredFormChoice(option.ChoiceId, option.Label)).ToArray())).ToArray(),
            targetCultures);
        return manifest.Profiles.Select(profile => new FormPublicationChannelRequest(profile.ChannelId,
            new StructuredFormPublicationRequest(operation.Id, operation.DefinitionId, operation.VersionId, operation.OperatorUserId,
                RequiredPermissions, manifest.PublicationFingerprint, profile.ProfileRevision,
                profile.Cultures.Select(culture => new StructuredFormPublicationCulture(culture.Culture, culture.CompilationFingerprint)).ToArray(),
                snapshot))).ToArray();
    }
    public Task<Result> CompleteAsync(FormPublicationWorkResponse work, IReadOnlyList<FormPublicationChannelProgress> progress, CancellationToken cancellationToken) =>
        OperationLog.RunAsync(logger, "CompleteFormPublication",
            () => unitOfWork.ExecuteInTransactionAsync(ct => CompleteCoreAsync(work, progress, ct), CommitPolicy.OnSuccess, cancellationToken));
    private async Task<Result> CompleteCoreAsync(FormPublicationWorkResponse work, IReadOnlyList<FormPublicationChannelProgress> progress, CancellationToken ct)
    {
        await drafts.LockDefinitionsAsync(ct);
        var operation = await repository.GetByIdAsync(work.OperationId, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (operation is { State: FormPublicationState.Published } && operation.Fence == work.Fence
            && operation.DefinitionId == work.DefinitionId && operation.VersionId == work.VersionId) return Result.Success();
        if (operation is null || operation.DefinitionId != work.DefinitionId || operation.VersionId != work.VersionId
            || operation.Fence != work.Fence || !operation.IsOpen || operation.LeaseExpiresAtUtc <= nowUtc || operation.LeaseExpiresAtUtc is null) return FormPublicationErrors.StateConflict;
        var manifest = protector.ReadManifest(new(operation.Id, operation.OperatorUserId, operation.RequestId, operation.DefinitionId, operation.VersionId), operation.ProtectedManifest);
        var responses = progress.Select(value => value.Response).ToArray();
        if (progress.Select(value => value.ChannelId).Distinct(StringComparer.Ordinal).Count() != manifest.Profiles.Count
            || progress.Any(value => !manifest.Profiles.Any(profile => profile.ChannelId == value.ChannelId))
            || responses.Length != manifest.Profiles.Count || responses.Any(response => response.OperationId != operation.Id || response.DefinitionId != operation.DefinitionId
            || response.VersionId != operation.VersionId || response.PublicationFingerprint != manifest.PublicationFingerprint)) return FormPublicationErrors.StateConflict;
        var authorized = await AllowedAsync(operation.OperatorUserId, ct);
        var knownNoEffect = responses.All(response => response.State == "Failed" && !response.RequiresReview);
        var state = !authorized ? knownNoEffect ? "Failed" : "Uncertain" : responses.Any(response => response.State == "Failed") ? "Failed"
            : responses.Any(response => response.State == "Uncertain" || response.RequiresReview) ? "Uncertain" : responses.All(response => response.State == "Published" && !response.RequiresReview) ? "Published" : "Publishing";
        var stage = state == "Published" ? "Activation" : responses.FirstOrDefault(response => response.State != "Published")?.Stage ?? "AuthorizationChanged";
        var recorded = operation.RecordChannelResult(work.Fence, nowUtc, state, stage); if (recorded.IsFailure) return recorded;
        if (state != "Published") return Result.Success();
        var definition = await drafts.GetAsync(operation.DefinitionId, ct) ?? throw new InvalidOperationException("The publication definition is missing.");
        var activated = definition.ActivateVersion(operation.BaseActiveVersionId, operation.VersionId);
        if (activated.IsFailure) return operation.DeferActivation(work.Fence, nowUtc);
        return operation.Complete(work.Fence, nowUtc);
    }
    private async Task<bool> AllowedAsync(Guid actor, CancellationToken ct) => await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)
        && await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Publish, ct);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publication operation {OperationId} was deferred because its sealed manifest is unreadable ({ExceptionType})")]
    private static partial void ManifestUnreadable(ILogger logger, Guid operationId, string exceptionType);
}
