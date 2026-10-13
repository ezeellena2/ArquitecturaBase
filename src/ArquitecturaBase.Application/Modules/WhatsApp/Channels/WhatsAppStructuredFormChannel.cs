using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Channels;

internal sealed class WhatsAppStructuredFormChannel(IWhatsAppFlowEnvironment environment, IWhatsAppFlowPublicationService publication, IWhatsAppFlowStatusService status) : IStructuredFormChannel
{
    public async Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken)
    {
        var accepted = await publication.AcceptAsync(request, cancellationToken);
        // These local rejections cannot be fixed by polling the provider.
        if (accepted.IsFailure && (accepted.Error == FlowErrors.UnsupportedConfiguration || accepted.Error == FlowErrors.PayloadMismatch))
            return new StructuredFormPublicationResponse(request.OperationId, request.DefinitionId, request.VersionId,
                request.PublicationFingerprint, "Failed", accepted.Error.Code, [], null, false);
        return accepted;
    }
    public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) => status.GetAsync(query, cancellationToken);
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => status.GetPreviewAsync(query, cancellationToken);
    public string ChannelId => "phone";
    public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var compilation = WhatsAppFlowCompiler.Compile(request);
        return Task.FromResult<Result<StructuredFormCheckResponse>>(new StructuredFormCheckResponse(
            WhatsAppFlowCompiler.ProfileRevision, compilation.Issues.Count == 0 ? "LocallyValidated" : "Blocked", "Pending",
            compilation.Flows.Select(flow => new StructuredFormCultureResponse(flow.Culture, WhatsAppFlowPublicationPolicy.CompilationFingerprint(flow.Hash), flow.ComponentCount)).ToArray(), compilation.Issues, environment.IsConfigured));
    }
}
