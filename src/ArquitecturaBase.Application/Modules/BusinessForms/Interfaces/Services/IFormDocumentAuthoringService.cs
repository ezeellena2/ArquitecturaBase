using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

/// <summary>
/// «Armar formulario con la IA» (DOC6): an interpretation of the channel <c>forms-documents</c>, asked by a reviewer who designs forms for one
/// unclassified document or for a group of similar ones, with their readings as the conversation's document, waited for like a dictation
/// (one can leave and come back); its proposal is read back as it is.
/// </summary>
public interface IFormDocumentAuthoringService
{
    Task<Result<FormDocumentProposalResponse>> RequestAsync(RequestFormDocumentProposalRequest request, CancellationToken cancellationToken);
    Task<Result<FormDocumentProposalResponse>> GetAsync(Guid requestId, CancellationToken cancellationToken);
}
