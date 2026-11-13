using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// Changing the form of a received document (decision 15): an interpretation of the channel <c>forms-documents</c> extracts it again for
/// the form the reviewer chose, from the reading it keeps, and is waited for like a dictation; the result is applied when it is read.
/// </summary>
public interface IFormReceivedDocumentExtractionService
{
    Task<Result<FormReceivedDocumentExtractionResponse>> RequestAsync(Guid documentId, ChangeFormReceivedDocumentFormRequest request,
        CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentExtractionResponse>> GetAsync(Guid documentId, Guid requestId, CancellationToken cancellationToken);
    /// <summary>
    /// DOC6: the server reads an unclassified document again for the form built from it, once published, with the context of whoever asked
    /// for that form; a document that changed meanwhile is left as it is.
    /// </summary>
    Task<Result<FormReceivedDocumentExtractionResponse>> RereadAsync(FormReceivedDocumentRereadRequest request, CancellationToken cancellationToken);
    /// <summary>DOC6: the server applies the extraction a document waits for, once it finished, as a reviewer's read would.</summary>
    Task<Result<FormReceivedDocumentExtractionResponse>> ApplyAsync(Guid documentId, CancellationToken cancellationToken);
}
