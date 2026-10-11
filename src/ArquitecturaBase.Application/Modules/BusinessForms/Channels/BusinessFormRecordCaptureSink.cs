using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Channels;

/// <summary>Adapts the optional BusinessForms service to the core's structured-record channel.</summary>
internal sealed class BusinessFormRecordCaptureSink(IBusinessFormRecordCaptureService capture) : IStructuredRecordSink
{
    public Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        capture.CaptureAsync(request, cancellationToken);

    public Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken) =>
        capture.FindExistingReceiptAsync(request, cancellationToken);
}
