using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormMissingRequestReader
{
    Task<IReadOnlyList<FormMissingTopicMetadata>> ListAssistantTopicsAsync(CancellationToken cancellationToken);
    Task<MissingFormTopicsPageResponse> FindPageAsync(ListMissingFormTopicsRequest request, CancellationToken cancellationToken);
    Task<MissingFormTopicResponse?> FindAsync(Guid topicId, CancellationToken cancellationToken);
}
