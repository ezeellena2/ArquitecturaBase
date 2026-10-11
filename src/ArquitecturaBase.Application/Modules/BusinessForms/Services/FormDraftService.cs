using System.Globalization;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormDraftService(IFormDraftRepository repository, IFormDefinitionReader reader, IUnitOfWork unitOfWork,
    IRequestValidator validator, FormDraftAccessGuard accessGuard, IFormChangeProtector protector,
    TimeProvider timeProvider, ILogger<FormDraftService> logger) : IFormDraftService
{
    public Task<Result<FormDraftCommitResponse>> CreateAsync(SaveFormDraftRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDraftCommitResponse>>(logger, "CreateFormDraft", async () =>
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken); if (access.IsFailure) return access.Error;
        request = request with { Name = FormDraftPolicy.NormalizeName(request.Name)!, Prefix = request.Prefix?.Trim().ToUpperInvariant()! };
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.ExpectedRevision.HasValue) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "expectedRevision");
        request = FormDraftPolicy.Normalize(request);
        return await unitOfWork.ExecuteInTransactionAsync(ct => SaveAsync(null, request, access.Value, ct), CommitPolicy.OnSuccess, cancellationToken);
    });
    public Task<Result<FormDraftCommitResponse>> UpdateAsync(Guid id, SaveFormDraftRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDraftCommitResponse>>(logger, "UpdateFormDraft", async () =>
    {
        var access = await accessGuard.CheckAsync(true, cancellationToken); if (access.IsFailure) return access.Error;
        request = request with { Name = FormDraftPolicy.NormalizeName(request.Name)!, Prefix = request.Prefix?.Trim().ToUpperInvariant()! };
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (!request.ExpectedRevision.HasValue) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "expectedRevision");
        request = FormDraftPolicy.Normalize(request, supplyMissingSubmitLabel: false);
        return await unitOfWork.ExecuteInTransactionAsync(ct => SaveAsync(id, request, access.Value, ct), CommitPolicy.OnSuccess, cancellationToken);
    });
    private async Task<Result<FormDraftCommitResponse>> SaveAsync(Guid? id, SaveFormDraftRequest request, Guid actor, CancellationToken ct)
    {
        var kind = id.HasValue ? "Update" : "Create";
        var canonicalRequest = FormDraftPolicy.Serialize(new { kind, definitionId = id, request });
        await repository.LockRequestAsync(actor, request.RequestId, ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var previous = await repository.GetChangeAsync(actor, request.RequestId, ct);
        if (previous is not null)
        {
            if (previous.Kind != kind || (id.HasValue && previous.DefinitionId != id.Value)) return FormDefinitionErrors.RequestConflict;
            if (nowUtc >= previous.ResultExpiresAtUtc || previous.ProtectedResult is null) return FormDefinitionErrors.ChangeExpired;
            var stored = protector.Unprotect(Context(previous), previous.ProtectedResult);
            return protector.Matches(stored, canonicalRequest) ? stored.Result : FormDefinitionErrors.RequestConflict;
        }
        await repository.LockDefinitionsAsync(ct);
        var definition = id.HasValue ? await repository.GetAsync(id.Value, ct) : null;
        if (id.HasValue && definition is null) return FormDefinitionErrors.NotFound;
        if (definition is not null && definition.Revision != request.ExpectedRevision) return FormDraftErrors.RevisionConflict;
        if (definition is { PrefixLocked: true } && definition.Prefix != request.Prefix) return FormDefinitionErrors.PrefixLocked;
        var resolved = FormDraftPolicy.Resolve(request, definition); if (resolved.IsFailure) return resolved.Error;
        var creating = definition is null; definition ??= BusinessFormDefinition.Create();
        if (await repository.ExistsPrefixAsync(request.Prefix, definition.Id, ct)) return FieldErrors.On(FormDefinitionErrors.PrefixTaken, "prefix");
        var draft = resolved.Value.Draft;
        var updated = definition.SetDraft(request.ExpectedRevision ?? 1, request.Name[draft.Culture], FormDraftPolicy.Serialize(request.Name), request.Prefix, draft.Culture,
            FormDraftPolicy.Serialize(draft.SubmitLabel), FormDraftPolicy.Serialize(new FormQuestionSchema(1, draft.Fields)),
            FormDraftPolicy.Serialize(resolved.Value.Ledger), draft.Fields.Count, creating);
        if (updated.IsFailure) return updated.Error;
        var change = BusinessFormDraftChange.Create(actor, request.RequestId, definition.Id, kind, request.ExpectedRevision, definition.Revision, nowUtc.AddDays(30));
        var response = new FormDraftCommitResponse(request.RequestId, FormDraftPolicy.Definition(definition), resolved.Value.IdentityMap,
            new("/api/forms/draft-changes/" + request.RequestId, change.ResultExpiresAtUtc));
        var protectedResult = protector.Protect(Context(change), canonicalRequest, response);
        if (protectedResult.Length > 1048576) return FieldErrors.On(FormDefinitionErrors.ResultTooLarge, "draft");
        change.SetProtectedResult(protectedResult);
        if (creating) repository.Add(definition);
        repository.AddChange(change);
        await repository.UpdateExpiredResultsAsync(nowUtc, ct);
        return response;
    }
    public Task<Result<FormDefinitionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDefinitionDetailResponse>>(logger, "GetFormDefinition", async () =>
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
        var row = await reader.FindAsync(id, cancellationToken); if (row is null) return FormDefinitionErrors.NotFound;
        var canSave = await HasPermissionAsync(access.Value, BusinessFormsPermissions.Manage, cancellationToken);
        var canPublish = await HasPermissionAsync(access.Value, BusinessFormsPermissions.Publish, cancellationToken);
        var publication = await reader.FindPublicationAsync(id, canPublish ? access.Value : null, cancellationToken);
        if (publication.CurrentOperation is { RequestId: { } requestId } operation)
            publication = publication with { CurrentOperation = operation with { RecoveryPath = $"/api/forms/definitions/{id}/publications/{requestId}" } };
        return new FormDefinitionDetailResponse(FormDraftPolicy.Definition(row.Id, row.NameJson, row.Prefix, row.PrefixLocked, row.Revision, row.Culture, row.SubmitLabelJson, row.SchemaJson),
            new(canSave, canPublish, publication.ActiveVersion is not null), new(row.CreatedAtUtc, row.ModifiedAtUtc, row.LastChangedAtUtc, row.Author), publication);
    });
    public Task<Result<FormRecoveredChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormRecoveredChangeResponse>>(logger, "GetFormDraftChange", async () =>
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
        var change = await reader.FindChangeAsync(access.Value, requestId, cancellationToken);
        if (change is null) return FormDefinitionErrors.ChangeNotFound;
        if (timeProvider.GetUtcNow().UtcDateTime >= change.ResultExpiresAtUtc || change.ProtectedResult is null) return FormDefinitionErrors.ChangeExpired;
        var result = protector.Unprotect(change.Context, change.ProtectedResult).Result;
        return new FormRecoveredChangeResponse(requestId, change.Context.Kind, "Committed", change.CommittedAtUtc, change.CurrentRevision, result);
    });
    public Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<FormDefinitionListResponse>>>(logger, "ListFormDefinitions", async () =>
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await reader.ListAsync(request with { DisplayCulture = DisplayCulture() }, await HasPermissionAsync(access.Value, BusinessFormsPermissions.Manage, cancellationToken), cancellationToken);
    });
    public Task<Result<FormDefinitionFacetsResponse>> GetFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormDefinitionFacetsResponse>>(logger, "GetFormDefinitionFacets", async () =>
    {
        var access = await accessGuard.CheckAsync(false, cancellationToken); if (access.IsFailure) return access.Error;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var counts = await reader.CountFacetsAsync(request.Search, DisplayCulture(), cancellationToken); return new FormDefinitionFacetsResponse(counts.All, counts.Published, counts.Drafts);
    });
    private static string DisplayCulture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
    private Task<bool> HasPermissionAsync(Guid actor, string permission, CancellationToken ct) =>
        accessGuard.HasPermissionAsync(actor, permission, ct);
    private static FormChangeProtectionContext Context(BusinessFormDraftChange change) => new(change.Id, change.OperatorUserId,
        change.RequestId, change.DefinitionId, change.Kind, change.ResultFormat);
}
