using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Validates and atomically records a structured form capture.</summary>
public interface IBusinessFormRecordCaptureService
{
    Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    /// <summary>Records a response loaded in the app by its author. Its replay compares what the person sent, not the offer the server rebuilds.</summary>
    Task<StructuredRecordCaptureResponse> CaptureEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);
}
