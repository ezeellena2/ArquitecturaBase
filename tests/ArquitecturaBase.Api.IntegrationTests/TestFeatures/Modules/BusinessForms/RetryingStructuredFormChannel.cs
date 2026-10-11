using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class RetryingStructuredFormChannel : IStructuredFormChannel
{
    private int _acceptCalls;

    public string ChannelId => "retry-test";
    public int AcceptCalls => _acceptCalls;
    public int FailuresBeforeAccept { get; set; } = 1;

    public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken) =>
        Task.FromResult<Result<StructuredFormCheckResponse>>(new StructuredFormCheckResponse("retry-profile", "LocallyValidated", "Pending",
            [new("en", "en-fingerprint", request.Questions.Count), new("es", "es-fingerprint", request.Questions.Count)], [], true));

    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _acceptCalls) <= FailuresBeforeAccept)
            return Task.FromResult<Result<StructuredFormPublicationResponse>>(FormPublicationErrors.ChannelUnavailable);
        return Task.FromResult<Result<StructuredFormPublicationResponse>>(Response(request.OperationId, request.DefinitionId,
            request.VersionId, request.PublicationFingerprint, "Accepted"));
    }

    public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<Result<StructuredFormPublicationResponse>>(Response(query.OperationId, query.DefinitionId,
            query.VersionId, query.PublicationFingerprint, "Published"));

    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    private static StructuredFormPublicationResponse Response(Guid operationId, Guid definitionId, Guid versionId,
        string publicationFingerprint, string state) =>
        new(operationId, definitionId, versionId, publicationFingerprint, state, "Verify", [], null, false);
}
