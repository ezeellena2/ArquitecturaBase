using ArquitecturaBase.Application.Common.Logging;
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

internal sealed class WhatsAppFlowPublicationService(IWhatsAppFlowRepository repository, IWhatsAppFlowReader reader,
    IWhatsAppFlowProtector protector, IUnitOfWork unitOfWork, IWhatsAppFlowEnvironment environment,
    ILogger<WhatsAppFlowPublicationService> logger) : IWhatsAppFlowPublicationService
{
    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<StructuredFormPublicationResponse>>(logger, "AcceptWhatsAppFlowPublication", async () =>
    {
        var fingerprint = WhatsAppFlowPublicationPolicy.RequestFingerprint(request);
        var previous = await reader.FindAsync(request.OperationId, cancellationToken);
        if (previous is not null)
        {
            var manifest = protector.ReadManifest(previous.Id, previous.NeutralOperationId, previous.VersionId, previous.ProtectedManifest);
            return WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(manifest, request) ? WhatsAppFlowPublicationPolicy.Status(previous, manifest) : FlowErrors.PayloadMismatch;
        }
        if (request.OperationId == Guid.Empty || request.DefinitionId == Guid.Empty || request.VersionId == Guid.Empty
            || request.ActorUserId == Guid.Empty || request.RequiredPermissions.Count is < 1 or > 4
            || request.ProfileRevision != WhatsAppFlowCompiler.ProfileRevision
            || !request.Snapshot.TargetCultures.Contains(request.Snapshot.OriginalCulture, StringComparer.Ordinal))
            return FlowErrors.UnsupportedConfiguration;
        var compilation = WhatsAppFlowCompiler.Compile(request.Snapshot);
        if (compilation.Issues.Count != 0) return FlowErrors.UnsupportedConfiguration;
        if (compilation.Flows.Count != request.Cultures.Count || compilation.Flows.Any(flow =>
            request.Cultures.SingleOrDefault(culture => culture.Culture == flow.Culture)?.CompilationFingerprint != WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash)))
            return FlowErrors.PayloadMismatch;
        // Account verification is a read outside the write boundary, never part of the local check.
        var account = await environment.VerifyAccountAsync(cancellationToken);
        if (account.IsFailure)
        {
            if (account.Error.Code != FlowErrors.AccountMismatch.Code) return account.Error;
            return new StructuredFormPublicationResponse(request.OperationId, request.DefinitionId, request.VersionId,
                request.PublicationFingerprint, "Failed", FlowErrors.AccountMismatch.Code, [], null, false);
        }
        return await unitOfWork.ExecuteInTransactionAsync(ct => AcceptCoreAsync(request, fingerprint, account.Value, compilation, ct),
            CommitPolicy.OnSuccess, cancellationToken);
    });
    private async Task<Result<StructuredFormPublicationResponse>> AcceptCoreAsync(StructuredFormPublicationRequest request, string fingerprint,
        WhatsAppFlowAccountContext account, WhatsAppFlowCompilationResponse compilation, CancellationToken ct)
    {
        await repository.LockAsync(request.OperationId, ct);
        var previous = await repository.GetAsync(request.OperationId, ct);
        if (previous is not null)
        {
            var stored = protector.ReadManifest(previous.Id, previous.NeutralOperationId, previous.VersionId, previous.ProtectedManifest);
            if (!WhatsAppFlowPublicationPolicy.MatchesRequestFingerprint(stored, request)) return FlowErrors.PayloadMismatch;
            return new StructuredFormPublicationResponse(request.OperationId, previous.DefinitionId, previous.VersionId,
                stored.PublicationFingerprint, "Accepted", "Create", [], previous.NextCheckAtUtc, false);
        }
        var operation = WhatsAppFlowOperation.Accept(request.OperationId, request.DefinitionId, request.VersionId, request.ActorUserId);
        var manifest = new WhatsAppFlowStoredManifest(2, fingerprint, request.PublicationFingerprint, account, request.RequiredPermissions.ToArray());
        operation.SealManifest(protector.ProtectManifest(operation.Id, operation.NeutralOperationId, operation.VersionId, manifest));
        repository.Add(operation);
        foreach (var flow in compilation.Flows)
        {
            var binding = WhatsAppFlowBinding.Create(operation.Id, operation.VersionId, flow.Culture,
                "form_" + request.OperationId.ToString("N") + "_" + flow.Culture, []);
            var projection = new WhatsAppFlowProjection(1, flow.Json, flow.Hash, WhatsAppFlowCompiler.ProfileRevision,
                WhatsAppFlowCompiler.CodecVersion, flow.Culture, flow.Mapping.Select(field => new WhatsAppFlowProjectionField(
                    field.FieldId, field.Key, field.Type, field.Required, field.MaxLength, field.Choices, field.Files)).ToArray());
            binding.SealProjection(protector.ProtectProjection(operation.Id, binding.Id, operation.VersionId, binding.Culture, projection));
            repository.AddBinding(binding);
        }
        return new StructuredFormPublicationResponse(request.OperationId, operation.DefinitionId, operation.VersionId,
            request.PublicationFingerprint, "Accepted", "Create", [], null, false);
    }
}
