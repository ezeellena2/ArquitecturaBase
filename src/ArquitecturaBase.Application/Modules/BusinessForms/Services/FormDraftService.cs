using System.Globalization;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormDraftService(IFormDraftRepository repository, IFormDefinitionReader reader, IUnitOfWork unitOfWork,
    IRequestValidator validator, ICurrentUser currentUser, IPermissionService permissions, IFormChangeProtector protector,
    TimeProvider timeProvider) : IFormDraftService
{
    public async Task<Result<FormDraftCommitResponse>> CreateAsync(SaveFormDraftRequest request, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(true, cancellationToken); if (access is not null) return access;
        request = request with { Name = FormDraftPolicy.NormalizeName(request.Name)!, Prefix = request.Prefix?.Trim().ToUpperInvariant()! };
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (request.ExpectedRevision.HasValue) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "expectedRevision");
        request = FormDraftPolicy.Normalize(request);
        return await unitOfWork.ExecuteInTransactionAsync(ct => SaveAsync(null, request, ct), CommitPolicy.OnSuccess, cancellationToken);
    }
    public async Task<Result<FormDraftCommitResponse>> UpdateAsync(Guid id, SaveFormDraftRequest request, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(true, cancellationToken); if (access is not null) return access;
        request = request with { Name = FormDraftPolicy.NormalizeName(request.Name)!, Prefix = request.Prefix?.Trim().ToUpperInvariant()! };
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        if (!request.ExpectedRevision.HasValue) return FieldErrors.On(FormDefinitionErrors.ContentInvalid, "expectedRevision");
        request = FormDraftPolicy.Normalize(request, supplyMissingSubmitLabel: false);
        return await unitOfWork.ExecuteInTransactionAsync(ct => SaveAsync(id, request, ct), CommitPolicy.OnSuccess, cancellationToken);
    }
    private async Task<Result<FormDraftCommitResponse>> SaveAsync(Guid? id, SaveFormDraftRequest request, CancellationToken ct)
    {
        var actor = currentUser.UserId!.Value; var kind = id.HasValue ? "Update" : "Create";
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
    public async Task<Result<FormDefinitionDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(false, cancellationToken); if (access is not null) return access;
        var row = await reader.FindAsync(id, cancellationToken); if (row is null) return FormDefinitionErrors.NotFound;
        var canSave = await permissions.HasPermissionAsync(currentUser.UserId!.Value, BusinessFormsPermissions.Manage, cancellationToken);
        var canPublish = await permissions.HasPermissionAsync(currentUser.UserId.Value, BusinessFormsPermissions.Publish, cancellationToken);
        var publication = await reader.FindPublicationAsync(id, canPublish ? currentUser.UserId.Value : null, cancellationToken);
        if (publication.CurrentOperation is { RequestId: { } requestId } operation)
            publication = publication with { CurrentOperation = operation with { RecoveryPath = $"/api/forms/definitions/{id}/publications/{requestId}" } };
        return new FormDefinitionDetailResponse(FormDraftPolicy.Definition(row.Id, row.NameJson, row.Prefix, row.PrefixLocked, row.Revision, row.Culture, row.SubmitLabelJson, row.SchemaJson),
            new(canSave, canPublish, publication.ActiveVersion is not null), new(row.CreatedAtUtc, row.ModifiedAtUtc, row.LastChangedAtUtc, row.Author), publication);
    }
    public async Task<Result<FormRecoveredChangeResponse>> GetChangeAsync(Guid requestId, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(false, cancellationToken); if (access is not null) return access;
        var change = await reader.FindChangeAsync(currentUser.UserId!.Value, requestId, cancellationToken);
        if (change is null) return FormDefinitionErrors.ChangeNotFound;
        if (timeProvider.GetUtcNow().UtcDateTime >= change.ResultExpiresAtUtc || change.ProtectedResult is null) return FormDefinitionErrors.ChangeExpired;
        var result = protector.Unprotect(change.Context, change.ProtectedResult).Result;
        return new FormRecoveredChangeResponse(requestId, change.Context.Kind, "Committed", change.CommittedAtUtc, change.CurrentRevision, result);
    }
    public async Task<Result<PagedResult<FormDefinitionListResponse>>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(false, cancellationToken); if (access is not null) return access;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        return await reader.ListAsync(request with { DisplayCulture = DisplayCulture() }, await permissions.HasPermissionAsync(currentUser.UserId!.Value, BusinessFormsPermissions.Manage, cancellationToken), cancellationToken);
    }
    public async Task<Result<FormDefinitionFacetsResponse>> GetFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken)
    {
        var access = await CheckAccessAsync(false, cancellationToken); if (access is not null) return access;
        if (await validator.ValidateAsync(request, cancellationToken) is { } invalid) return invalid;
        var counts = await reader.CountFacetsAsync(request.Search, DisplayCulture(), cancellationToken); return new FormDefinitionFacetsResponse(counts.All, counts.Published, counts.Drafts);
    }
    private static string DisplayCulture() => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
    private async Task<Error?> CheckAccessAsync(bool writing, CancellationToken ct)
    {
        if (currentUser.UserId is not { } actor) return FormDefinitionErrors.Unauthorized;
        if (!await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Read, ct)
            || (writing && !await permissions.HasPermissionAsync(actor, BusinessFormsPermissions.Manage, ct))) return FormDefinitionErrors.Forbidden;
        return null;
    }
    private static FormChangeProtectionContext Context(BusinessFormDraftChange change) => new(change.Id, change.OperatorUserId,
        change.RequestId, change.DefinitionId, change.Kind, change.ResultFormat);
}
