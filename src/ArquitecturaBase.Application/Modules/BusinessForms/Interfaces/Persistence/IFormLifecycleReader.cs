using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormLifecycleReader
{
    Task<IReadOnlyList<FormLifecycleStoredRow>> ListFactsAsync(IReadOnlyCollection<Guid> definitionIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormIncomingSuggestionResponse>> ListIncomingSuggestionsAsync(Guid definitionId, CancellationToken cancellationToken);
}
