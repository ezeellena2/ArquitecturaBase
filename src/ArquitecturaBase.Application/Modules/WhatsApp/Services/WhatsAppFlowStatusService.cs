using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowStatusService(IWhatsAppFlowReader reader, IWhatsAppFlowProtector protector,
    IWhatsAppFlowProvider provider, IWhatsAppFlowEnvironment environment, TimeProvider timeProvider,
    ILogger<WhatsAppFlowStatusService> logger) : IWhatsAppFlowStatusService
{
    private static readonly TimeSpan PreviewDeadline = TimeSpan.FromSeconds(30);

    public Task<Result<StructuredFormPublicationResponse>> GetAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<StructuredFormPublicationResponse>>(logger, "GetWhatsAppFlowPublicationStatus",
            () => GetCoreAsync(query, cancellationToken));

    private async Task<Result<StructuredFormPublicationResponse>> GetCoreAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken)
    {
        var row = await reader.FindAsync(query.OperationId, cancellationToken);
        if (row is null || row.DefinitionId != query.DefinitionId || row.VersionId != query.VersionId) return FlowErrors.NotFound;
        var manifest = protector.ReadManifest(row.Id, row.NeutralOperationId, row.VersionId, row.ProtectedManifest);
        if (manifest.PublicationFingerprint != query.PublicationFingerprint) return FlowErrors.PayloadMismatch;
        var status = WhatsAppFlowPublicationPolicy.Status(row, manifest);
        if (status.State != "Published") return status;
        var account = await environment.VerifyAccountAsync(cancellationToken);
        if (account.IsFailure || !account.Value.HasSameIdentity(manifest.Account))
            return status with { State = "Uncertain", Stage = "Verify", RequiresReview = true };
        foreach (var binding in row.Bindings)
        {
            if (binding.MetaFlowId is null) return FlowErrors.StateConflict;
            var projection = protector.ReadProjection(row.Id, binding.Id, row.VersionId, binding.Culture, binding.ProtectedProjection);
            var verification = await provider.VerifyAsync(manifest.Account, binding.MetaFlowId, binding.TechnicalName, projection.Hash, cancellationToken);
            if (!verification.Published || !verification.AssetAndOwnerMatch || !verification.Validated || !verification.Healthy)
                return status with { State = "Uncertain", Stage = "Verify", RequiresReview = true };
        }
        return status;
    }
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<StructuredFormPreviewResponse>>(logger, "GetWhatsAppFlowPreview", async () =>
    {
        // The preview chains several Meta calls, each with its own cap; if Meta stops answering, the dialog gets an answer in 30 s.
        using var deadline = new CancellationTokenSource(PreviewDeadline, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            return await GetPreviewCoreAsync(query, linked.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return FlowErrors.PreviewUnavailable;
        }
    });

    private async Task<Result<StructuredFormPreviewResponse>> GetPreviewCoreAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken)
    {
        var checkedResult = await GetCoreAsync(new(query.OperationId, query.DefinitionId, query.VersionId, query.PublicationFingerprint), cancellationToken);
        if (checkedResult.IsFailure) return checkedResult.Error;
        if (checkedResult.Value.State != "Published") return FlowErrors.PreviewUnavailable;
        var row = (await reader.FindAsync(query.OperationId, cancellationToken))!;
        var binding = row.Bindings.SingleOrDefault(value => value.Culture == query.Culture);
        if (binding?.MetaFlowId is null) return FlowErrors.PreviewUnavailable;
        var manifest = protector.ReadManifest(row.Id, row.NeutralOperationId, row.VersionId, row.ProtectedManifest);
        // VO1-A (2026-11-06): it runs interactively from the Flow's first screen, with the sample options of its catalog lists.
        var projection = protector.ReadProjection(row.Id, binding.Id, row.VersionId, binding.Culture, binding.ProtectedProjection);
        if (WhatsAppFlowStartScreen.Of(projection) is not { } screen) return FlowErrors.PreviewUnavailable;
        var preview = await provider.GetPreviewAsync(manifest.Account, binding.MetaFlowId, new(screen, query.Catalogs ?? []), cancellationToken);
        if (preview.IsFailure) return preview.Error;
        return preview.Value.ExpiresAtUtc > timeProvider.GetUtcNow().UtcDateTime ? preview : FlowErrors.PreviewUnavailable;
    }
}
