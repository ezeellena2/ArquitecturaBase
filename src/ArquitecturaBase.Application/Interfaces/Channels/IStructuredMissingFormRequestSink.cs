using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Interfaces.Channels;
public interface IStructuredMissingFormRequestSink
{
    Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request, CancellationToken cancellationToken);
}
