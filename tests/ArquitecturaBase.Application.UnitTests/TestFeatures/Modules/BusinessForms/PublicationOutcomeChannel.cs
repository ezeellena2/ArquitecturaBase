using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class PublicationOutcomeChannel(string acceptanceState, bool failAcceptance, bool lookupMissing) : IStructuredFormChannel
{
    public string ChannelId => "outcome-test";
    public int AcceptCalls { get; private set; }
    public int LookupCalls { get; private set; }

    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken)
    {
        AcceptCalls++;
        return Task.FromResult<Result<StructuredFormPublicationResponse>>(failAcceptance
            ? FormPublicationErrors.ChannelUnavailable
            : new StructuredFormPublicationResponse(request.OperationId, request.DefinitionId, request.VersionId,
                request.PublicationFingerprint, acceptanceState, "Validation", [], null, false));
    }

    public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken)
    {
        LookupCalls++;
        return Task.FromResult<Result<StructuredFormPublicationResponse>>(lookupMissing
            ? FormPublicationErrors.NotFound
            : new StructuredFormPublicationResponse(query.OperationId, query.DefinitionId, query.VersionId,
                query.PublicationFingerprint, "Publishing", "Create", [], null, false));
    }

    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
}
