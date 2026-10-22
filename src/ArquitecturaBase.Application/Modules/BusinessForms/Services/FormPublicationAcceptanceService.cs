using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPublicationAcceptanceService(IFormDraftRepository draftRepository, IFormPublicationRepository publicationRepository,
    FormPublicationAcceptanceGuard requestGuard,
    IFormPublicationProtector protector, IUnitOfWork unitOfWork, TimeProvider timeProvider, FormPublicationChannelVerifier channelVerifier,
    ILogger<FormPublicationAcceptanceService> logger) : IFormPublicationAcceptanceService
{
    public Task<Result<FormPublicationReceiptResponse>> AcceptAsync(Guid id, AcceptFormPublicationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<FormPublicationReceiptResponse>>(logger, "AcceptFormPublication", async () =>
    {
        var validated = await requestGuard.CheckAsync(request, cancellationToken);
        if (validated.IsFailure) return validated.Error;
        return await unitOfWork.ExecuteInTransactionAsync(ct => AcceptCoreAsync(id, validated.Value.Actor, request.CheckToken,
            validated.Value.Draft, ct), CommitPolicy.OnSuccess, cancellationToken);
    });

    private async Task<Result<FormPublicationReceiptResponse>> AcceptCoreAsync(Guid id, Guid actor, string token, SaveFormDraftRequest input, CancellationToken ct)
    {
        var access = await requestGuard.CheckAccessAsync(ct); if (access.IsFailure) return access.Error;
        await draftRepository.LockRequestAsync(actor, input.RequestId, ct);
        var attemptFingerprint = FormPublicationSnapshotPolicy.Hash(FormDraftPolicy.Serialize(new { definitionId = id, input }));
        var previous = await publicationRepository.GetOperationAsync(actor, input.RequestId, ct);
        if (previous is not null)
        {
            if (previous.DefinitionId != id) return FormPublicationErrors.PayloadMismatch;
            var manifest = protector.ReadManifest(Context(previous), previous.ProtectedManifest);
            return manifest.AttemptFingerprint == attemptFingerprint ? Receipt(previous) : FormPublicationErrors.PayloadMismatch;
        }
        var ticket = string.IsNullOrEmpty(token) || token.Length > 8192 ? null : protector.ReadTicket(token);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (ticket is null || ticket.Actor != actor || ticket.DefinitionId != id || ticket.DraftRevision != input.ExpectedRevision
            || ticket.ExpiresAtUtc <= nowUtc) return FormPublicationErrors.CheckExpired;
        await draftRepository.LockDefinitionsAsync(ct);
        var definition = await draftRepository.GetAsync(id, ct);
        if (definition is null) return FormDefinitionErrors.NotFound;
        if (definition.Revision != input.ExpectedRevision) return FormDraftErrors.RevisionConflict;
        var saved = FormDraftPolicy.Definition(definition);
        if (!FormPublicationSnapshotPolicy.Matches(input, saved)) return FormPublicationErrors.DraftMustBeSaved;
        if (FormPublicationPolicy.MissingTranslations(saved).Count != 0) return FormPublicationErrors.MissingTranslation;
        if (await publicationRepository.ExistsOpenOperationAsync(id, ct)) return FormPublicationErrors.InProgress;
        var verified = await channelVerifier.CheckAsync(saved, ct); if (verified.IsFailure) return verified.Error;
        var profiles = verified.Value;
        var publicationFingerprint = FormPublicationSnapshotPolicy.Fingerprint(saved, profiles);
        if (ticket.PublicationFingerprint != publicationFingerprint || ticket.ExpiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime)
            return FormPublicationErrors.CheckExpired;
        var fields = await publicationRepository.ListFieldsAsync(id, ct);
        foreach (var field in saved.Draft.Fields)
        {
            var existing = fields.SingleOrDefault(value => value.Id == field.FieldId);
            if (existing is not null && (existing.Type != field.Type || existing.StableKey != field.Key)) return FormPublicationErrors.StateConflict;
            if (existing is null) publicationRepository.AddField(BusinessFormField.Register(field.FieldId, id, field.Key, field.Type));
        }
        var schema = new FormVersionSchema(1, saved.Draft.Fields);
        var contentHash = FormPublicationSnapshotPolicy.Hash(FormDraftPolicy.Serialize(schema));
        var number = checked((long)await publicationRepository.CountVersionsAsync(id, ct) + 1);
        var version = BusinessFormVersion.Create(id, number, contentHash);
        version.SealSchema(FormDraftPolicy.Serialize(schema));
        version.SealPresentation(FormDraftPolicy.Serialize(saved.Name), FormDraftPolicy.Serialize(saved.Draft.SubmitLabel), saved.Draft.Culture, schema.Questions.Count);
        version.SealPublishedCultures(profiles.SelectMany(profile => profile.Cultures).Select(culture => culture.Culture)
            .Distinct(StringComparer.Ordinal).ToArray());
        var operation = BusinessFormPublicationOperation.Accept(id, version.Id, actor, input.RequestId, saved.Revision,
            number, definition.ActiveVersionId, [], nowUtc);
        operation.SealManifest(protector.ProtectManifest(Context(operation), new(1, attemptFingerprint, publicationFingerprint,
            id, version.Id, saved.Revision, contentHash, saved.Name, saved.Prefix, saved.Draft.Culture, saved.Draft.SubmitLabel, profiles, saved.Draft.Fields.Any(field => field.Files is not null) ? channelVerifier.MaxFileSizeMiB : null)));
        definition.LockPrefix();
        publicationRepository.AddVersion(version); publicationRepository.AddOperation(operation);
        return Receipt(operation);
    }

    private static FormPublicationProtectionContext Context(BusinessFormPublicationOperation operation) =>
        new(operation.Id, operation.OperatorUserId, operation.RequestId, operation.DefinitionId, operation.VersionId);
    private static FormPublicationReceiptResponse Receipt(BusinessFormPublicationOperation operation) => new(operation.RequestId,
        operation.Id, operation.DefinitionId, operation.VersionId, operation.VersionNumber, operation.DraftRevision,
        "Accepted", operation.AcceptedAtUtc);
}
