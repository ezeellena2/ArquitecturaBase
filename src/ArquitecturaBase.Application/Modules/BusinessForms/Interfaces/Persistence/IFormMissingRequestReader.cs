using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormMissingRequestReader
{
    Task<IReadOnlyList<FormMissingTopicRow>> ListAssistantTopicsAsync(CancellationToken cancellationToken);
    /// <summary>O55: the active forms completed only in the app, with their sealed names: what a request names is not missing.</summary>
    Task<IReadOnlyList<FormAppOnlyRow>> ListAppOnlyFormsAsync(CancellationToken cancellationToken);
    Task<MissingFormTopicsPageResponse> FindPageAsync(ListMissingFormTopicsRequest request, CancellationToken cancellationToken);
    Task<MissingFormTopicResponse?> FindAsync(Guid topicId, CancellationToken cancellationToken);
}
