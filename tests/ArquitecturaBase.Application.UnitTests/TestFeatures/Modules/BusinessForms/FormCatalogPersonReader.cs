using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogPersonReader : IFormPersonDataReader
{
    public List<FormPersonAttributeStoredRow> Attributes { get; } = [];
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
    public Dictionary<Guid, string> OptionTexts { get; } = [];
    public int RecipientReads { get; private set; }
    public FormPersonMissingRecipientsRow Missing { get; set; } = new(0, []);
    public List<Guid[]> MissingReads { get; } = [];
    public Action? OnRead { get; set; }
    public Task<IReadOnlyList<FormPersonAttributeStoredRow>> ListAttributesAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormPersonAttributeStoredRow>>(Attributes.ToArray());
    public Task<FormPersonRecipientDataRow> FindRecipientDataAsync(Guid userId, CancellationToken ct)
    {
        RecipientReads++;
        var result = new FormPersonRecipientDataRow(userId, 1, FormDraftPolicy.Serialize(Values), Attributes.ToArray(), OptionTexts);
        OnRead?.Invoke();
        return Task.FromResult(result);
    }
    public Task<FormPersonMissingRecipientsRow> FindRecipientsWithoutDataAsync(IReadOnlyCollection<Guid> attributeIds, CancellationToken ct)
    {
        MissingReads.Add(attributeIds.Order().ToArray());
        return Task.FromResult(Missing);
    }
    public Task<IReadOnlyList<FormPersonAttributeUseRow>> ListAttributeUsagesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormPersonDataTableRow>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormPersonAttributeOptionRow>> ListOptionsAsync(Guid catalogId, ListFormPersonAttributeOptionsRequest request, CancellationToken ct) => throw new NotSupportedException();
}
