using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.SystemQueries;
namespace ArquitecturaBase.Application.Channels;
internal sealed class UnavailableStructuredMissingFormRequestSink : IStructuredMissingFormRequestSink
{
    public Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request,CancellationToken cancellationToken)
    { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult<Result<StructuredMissingFormRequestCaptureResponse>>(SystemQueryErrors.NotFound); }
}
