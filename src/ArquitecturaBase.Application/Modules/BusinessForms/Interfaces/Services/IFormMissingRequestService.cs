using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormMissingRequestService
{
    Task<Result<MissingFormTopicsPageResponse>> ListAsync(ListMissingFormTopicsRequest request, CancellationToken cancellationToken);
    Task<Result<MissingFormTopicResponse>> DiscardAsync(Guid topicId, DiscardMissingFormTopicRequest request, CancellationToken cancellationToken);
}
