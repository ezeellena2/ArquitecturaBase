using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
internal sealed class StubStructuredFormChannel(string channelId) : IStructuredFormChannel
{
    public Task<Result<StructuredFormPublicationResponse>> AcceptAsync(StructuredFormPublicationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<StructuredFormPublicationResponse>> GetPublicationAsync(StructuredFormPublicationQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<StructuredFormPreviewResponse>> GetPreviewAsync(StructuredFormPreviewQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

    public string ChannelId => channelId;
    public Task<Result<StructuredFormCheckResponse>> CheckAsync(StructuredFormCheckRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The composition test channel must not execute.");
}