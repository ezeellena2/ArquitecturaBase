using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>Validates and atomically records a structured form capture.</summary>
public interface IBusinessFormRecordCaptureService
{
    Task<StructuredRecordCaptureResponse> CaptureAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingReceiptAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    /// <summary>Records a response loaded in the app by its author. Its replay compares what the person sent, not the offer the server rebuilds.</summary>
    Task<StructuredRecordCaptureResponse> CaptureEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingEntryAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Records a correction loaded in the app (CW10-A): another record with the replaced response's number, author, reception
    /// and channel, through the same capture. <paramref name="request"/>'s actor is who corrects; the files that stay are kept.
    /// </summary>
    Task<StructuredRecordCaptureResponse> CaptureCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingCorrectionAsync(StructuredRecordCaptureRequest request, FormRecordCorrection correction,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records a received document a reviewer passes to its form (task 7): the response of whoever sent it, who must still be active, with
    /// the reviewer as the origin's operator, whose «Revisar documentos» replaces the sender's permission to load responses for this path
    /// only; the document is in the origin and is confirmed in the same commit. Its replay compares what the reviewer sent.
    /// </summary>
    Task<StructuredRecordCaptureResponse> CaptureDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);

    Task<StructuredRecordCaptureResponse?> FindExistingDocumentAsync(StructuredRecordCaptureRequest request, CancellationToken cancellationToken);
}
