using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «Documentos recibidos» (DOC5-A) for whoever sees them (<c>formdocuments.read</c>): the inbox with its filters, search and counts,
/// the menu's counter, a document with its reading, proposal and history, and its file; whoever reviews them
/// (<c>formdocuments.review</c>) discards and reopens them with the expected revision and searches the sender's options of a choice.
/// </summary>
public interface IFormReceivedDocumentService
{
    Task<Result<FormReceivedDocumentsPageResponse>> ListAsync(ListFormReceivedDocumentsRequest request, CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentCountsResponse>> CountAsync(CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentDetailResponse>> GetAsync(Guid documentId, CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentFileResponse>> OpenAsync(Guid documentId, CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentOptionsResponse>> SearchOptionsAsync(Guid documentId, SearchFormReceivedDocumentOptionsRequest request,
        CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentDetailResponse>> DiscardAsync(Guid documentId, ReviewFormReceivedDocumentRequest request, CancellationToken cancellationToken);
    Task<Result<FormReceivedDocumentDetailResponse>> ReopenAsync(Guid documentId, ReviewFormReceivedDocumentRequest request, CancellationToken cancellationToken);
}
