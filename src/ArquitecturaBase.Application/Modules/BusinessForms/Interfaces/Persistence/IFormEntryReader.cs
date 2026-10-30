using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>Reads to load a response in the app: the version, the options a person can choose and the saved outcome.</summary>
public interface IFormEntryReader
{
    /// <summary>A form that is not deleted, with the requested published version or, without one, the active one; null if the form does not exist.</summary>
    Task<FormEntryDefinitionRow?> FindDefinitionAsync(Guid definitionId, Guid? versionId, CancellationToken cancellationToken);

    /// <summary>Active options of a Manual catalog that pass the containment, searched by texts, details and code, in their order.</summary>
    Task<PagedResult<FormCatalogStoredItemRow>> ListItemsAsync(FormEntryItemsQuery query, CancellationToken cancellationToken);

    /// <summary>People of a Filters catalog for the person who loads, searched by the shown texts, in the catalog's order.</summary>
    Task<PagedResult<FormCatalogFilterOptionResponse>> ListPeopleAsync(Guid recipientUserId, FormCatalogFilterDefinition filters,
        FormEntryItemsQuery query, CancellationToken cancellationToken);

    /// <summary>When the response was submitted, its state and whether a rule already asked for its approval.</summary>
    Task<FormEntryOutcomeRow?> FindOutcomeAsync(Guid recordId, CancellationToken cancellationToken);

    /// <summary>Who loaded each of those receipts in the app and when; receipts from a channel are not returned.</summary>
    Task<IReadOnlyList<FormEntryOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken);
}
