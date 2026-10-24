using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using UserPermissions = ArquitecturaBase.Domain.Authorization.Permissions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormPersonDataFixture : IFormPersonDataRepository, IFormPersonDataReader, IFormCatalogRepository, IFormCatalogReader
{
    public FakeUnitOfWork Work { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public InMemoryUserAccounts Users { get; } = new();
    public Guid Actor { get; }
    public Guid Subject { get; }
    public List<BusinessFormPersonAttribute> Attributes { get; } = [];
    public List<BusinessFormPersonData> Data { get; } = [];
    public HashSet<Guid> DeletedUsers { get; } = [];
    public HashSet<Guid> UsedAttributes { get; } = [];
    public string UsageNameJson { get; set; } = "{\"es\":\"Formulario\"}";
    public string? SourceNameJson { get; set; }
    public List<string> Events { get; } = [];
    public BusinessFormCatalog Catalog { get; } = BusinessFormCatalog.CreateManual("es", Names(), Names());
    public BusinessFormCatalog OtherCatalog { get; } = BusinessFormCatalog.CreateManual("es", Names(), Names());
    public BusinessFormCatalogItem ActiveItem { get; }
    public BusinessFormCatalogItem InactiveItem { get; }
    public int RecipientReads { get; private set; }
    public int PeopleReads { get; private set; }
    public Action? OnLock { get; set; }
    public Action? OnRecipientRead { get; set; }
    public Dictionary<Guid, int> MissingCounts { get; } = [];
    public List<(Guid[] AttributeIds, string? Search)> MissingReads { get; } = [];
    public Action? OnMissingRead { get; set; }
    public FormPersonDataService Service { get; }

    public FormPersonDataFixture()
    {
        Actor = Users.AddUser("person-data-manager@example.invalid").Id;
        Subject = Users.AddUser("person-data-subject@example.invalid").Id;
        Current.UserId = Actor;
        Grant(BusinessFormsPermissions.PersonDataManage, UserPermissions.Users.Read, UserPermissions.Users.Manage);
        ActiveItem = Catalog.AddManualItem("CENTRAL", "Centro", "Central", null, null, 0).Value;
        InactiveItem = Catalog.AddManualItem("OLD", "Retirada", "Retired", null, null, 1).Value;
        Catalog.SetManualItemActive(InactiveItem, false);
        Service = new(this, this, this, this, Work, RequestValidators.For(
            new CreateFormPersonAttributeRequestValidator(), new UpdateFormPersonAttributeRequestValidator(),
            new UpdateFormPersonDataRequestValidator(), new ListFormPersonDataRequestValidator(), new ListFormPersonAttributeOptionsRequestValidator()),
            new(Current, Permissions, Users), NullLogger<FormPersonDataService>.Instance);
    }

    public void Grant(params string[] permissions) => Permissions.Permissions[Actor] = [.. permissions];
    public BusinessFormPersonAttribute AddAttribute(string key = "zone", bool catalog = true, bool active = true)
    {
        var value = BusinessFormPersonAttribute.Create(key, "es", Names(), catalog ? BusinessFormPersonAttributeType.Catalog : BusinessFormPersonAttributeType.Text,
            catalog ? Catalog.Id : null, Attributes.Count).Value;
        if (!active) value.Update(1, Names(), value.Type, value.SourceCatalogId, value.Position, false);
        Attributes.Add(value);
        return value;
    }

    public Task LockDefinitionsAsync(CancellationToken cancellationToken) => Lock("definitions");
    public Task LockAttributesAsync(CancellationToken cancellationToken) => Lock("attributes");
    public Task LockDataAsync(Guid userId, CancellationToken cancellationToken) => Lock("data:" + userId.ToString("N"));
    private Task Lock(string name)
    {
        Assert.True(Work.InTransaction);
        Events.Add(name);
        var hook = OnLock;
        OnLock = null;
        hook?.Invoke();
        return Task.CompletedTask;
    }
    public Task<BusinessFormPersonAttribute?> GetAttributeAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Attributes.SingleOrDefault(value => value.Id == id));
    public Task<BusinessFormPersonData?> GetDataAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Data.SingleOrDefault(value => value.UserId == userId));
    Task<List<BusinessFormPersonAttribute>> IFormPersonDataRepository.ListAttributesAsync(CancellationToken cancellationToken) => Task.FromResult(Attributes.ToList());
    public Task<List<BusinessFormPersonData>> ListDataWithValueAsync(Guid attributeId, CancellationToken cancellationToken)
    {
        Assert.True(Work.InTransaction);
        return Task.FromResult(DataWithValue(attributeId).ToList());
    }
    public Task<int> CountPeopleWithValueAsync(Guid attributeId, CancellationToken cancellationToken)
    {
        Assert.True(Work.InTransaction);
        return Task.FromResult(DataWithValue(attributeId).Count(value => !DeletedUsers.Contains(value.UserId)));
    }
    private IEnumerable<BusinessFormPersonData> DataWithValue(Guid attributeId) => Data
        .Where(value => JsonSerializer.Deserialize<Dictionary<string, string>>(value.ValuesJson)!.ContainsKey(attributeId.ToString("D"))).OrderBy(value => value.UserId);
    public void Add(BusinessFormPersonAttribute value) { Assert.True(Work.InTransaction); Attributes.Add(value); }
    public void Add(BusinessFormPersonData value) { Assert.True(Work.InTransaction); Data.Add(value); }

    public Task<IReadOnlyList<FormPersonAttributeStoredRow>> ListAttributesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FormPersonAttributeStoredRow>>(Attributes.Select(Row).ToArray());
    public Task<IReadOnlyList<FormPersonAttributeUseRow>> ListAttributeUsagesAsync(IReadOnlyCollection<Guid> attributeIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormPersonAttributeUseRow>>(attributeIds.Where(UsedAttributes.Contains)
            .Select(id => new FormPersonAttributeUseRow(id, Guid.CreateVersion7(), UsageNameJson)).ToArray());
    public Task<FormPersonRecipientDataRow> FindRecipientDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        RecipientReads++;
        var data = Data.SingleOrDefault(value => value.UserId == userId);
        var row = new FormPersonRecipientDataRow(userId, data?.Revision, data?.ValuesJson ?? "{}", Attributes.Select(Row).ToArray(),
            new Dictionary<Guid, FormPersonOptionTextRow> { [ActiveItem.Id] = new(ActiveItem.CatalogId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "Central" : "Centro") });
        OnRecipientRead?.Invoke();
        return Task.FromResult(row);
    }
    public Task<PagedResult<FormPersonDataTableRow>> ListPeopleAsync(ListFormPersonDataRequest request, CancellationToken cancellationToken)
    {
        PeopleReads++;
        return Task.FromResult(new PagedResult<FormPersonDataTableRow>([], request.Page, request.PageSize, 0));
    }
    public Task<FormPersonMissingCounts> CountMissingByAttributeAsync(IReadOnlyCollection<Guid> attributeIds, string? search, CancellationToken cancellationToken)
    {
        MissingReads.Add((attributeIds.ToArray(), search));
        OnMissingRead?.Invoke();
        return Task.FromResult(new FormPersonMissingCounts(attributeIds.ToDictionary(id => id, id => MissingCounts.GetValueOrDefault(id))));
    }
    public Task<PagedResult<FormPersonAttributeOptionRow>> ListOptionsAsync(Guid sourceCatalogId, ListFormPersonAttributeOptionsRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<FormPersonAttributeOptionRow>([new(ActiveItem.Id, "Centro")], request.Page, request.PageSize, 1));

    Task IFormCatalogRepository.LockAsync(Guid id, CancellationToken cancellationToken) => Lock("catalog:" + id.ToString("N"));
    Task<BusinessFormCatalog?> IFormCatalogRepository.GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Source(id));
    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(id == Catalog.Id ? new List<BusinessFormCatalogItem> { ActiveItem, InactiveItem } : []);
    public void Add(BusinessFormCatalog value) => throw new NotSupportedException();
    public void AddItems(IEnumerable<BusinessFormCatalogItem> values) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FormCatalogStoredRow?>(Source(id) is not { } source ? null : new(
        source.Id, source.Key, source.NameJson, source.DescriptionJson, source.OriginalCulture, source.Kind.ToString(), source.Revision, source.IsActive,
        source.CreatedAtUtc, source.CreatedBy, source.ModifiedAtUtc, source.ModifiedBy, id == Catalog.Id ? 2 : 0));
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>([Item(ActiveItem), Item(InactiveItem)]);
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken cancellationToken, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken cancellationToken) => Task.FromResult(false);

    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid catalogId, ListFormCatalogItemsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

    private BusinessFormCatalog? Source(Guid id) => id == Catalog.Id ? Catalog : id == OtherCatalog.Id ? OtherCatalog : null;
    private FormPersonAttributeStoredRow Row(BusinessFormPersonAttribute value) => new(value.Id, value.Key, value.NameJson,
        value.OriginalCulture, value.Type.ToString(), value.SourceCatalogId, value.Position, value.IsActive, value.Revision, value.SourceCatalogId is null ? null : SourceNameJson ?? Catalog.NameJson);
    private static FormCatalogStoredItemRow Item(BusinessFormCatalogItem value) => new(value.Id, value.CatalogId, value.Code,
        value.TextEs, value.TextEn, value.DetailEs, value.DetailEn, value.Position, value.IsActive, value.Value);
    private static Dictionary<string, string> Names() => new() { ["es"] = "Zona", ["en"] = "Zone" };
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<FormPersonMissingRecipientsRow> FindRecipientsWithoutDataAsync(IReadOnlyCollection<Guid> attributeIds,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
