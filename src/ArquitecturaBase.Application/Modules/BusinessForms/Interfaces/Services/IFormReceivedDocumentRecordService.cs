using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// Passing a received document to its form (decision 15): the common capture records the response as the person who sent it, with the
/// reviewer as the origin's operator and the document attached and in the origin, idempotent by the request's UUID; the document is
/// confirmed with its record in the same commit.
/// </summary>
public interface IFormReceivedDocumentRecordService
{
    Task<Result<FormReceivedDocumentRecordResponse>> PassAsync(Guid documentId, PassFormReceivedDocumentRequest request, CancellationToken cancellationToken);
}
