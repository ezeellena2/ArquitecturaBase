using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormPersonDataReader
{
    Task<IReadOnlyList<FormPersonAttributeStoredRow>> ListAttributesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FormPersonAttributeUseRow>> ListAttributeUsagesAsync(IReadOnlyCollection<Guid> attributeIds, CancellationToken cancellationToken);
    Task<FormPersonRecipientDataRow> FindRecipientDataAsync(Guid userId, CancellationToken cancellationToken);
    Task<FormPersonMissingRecipientsRow> FindRecipientsWithoutDataAsync(IReadOnlyCollection<Guid> attributeIds, CancellationToken cancellationToken);
    Task<PagedResult<FormPersonDataTableRow>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken cancellationToken);
    Task<PagedResult<FormPersonAttributeOptionRow>> ListOptionsAsync(Guid sourceCatalogId, ListFormPersonAttributeOptionsRequest request, CancellationToken cancellationToken);
}
