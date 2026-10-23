using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogProposalFixture : IFormCatalogRepository, IFormCatalogFilterReader, IFormCatalogReader
{
    public CatalogAssistantFixture Assistant { get; } = new();
    public FakeUnitOfWork Uow { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero));
    public List<BusinessFormCatalog> Catalogs { get; } = [];
    public List<Guid> Locks { get; } = [];
    public Action? WhileLocking { get; set; }
    public int PreviewReads { get; private set; }
    public bool SampleExists { get; set; } = true;
    public FormCatalogFilterPreviewResponse Preview { get; set; } = new([new(Guid.NewGuid(), "Fictitious supervisor", "Supervisores")], 1, false);
    public IReadOnlyList<FormCatalogFilterChoiceResponse> Roles { get; set; } = [new(CatalogAssistantFixture.RoleId.ToString(), "Supervisores")];
    public FormCatalogProposalService Service { get; }
    public FormCatalogProposalFixture()
    {
        Current.UserId = Assistant.Actor;
        Service = new(this, this, Assistant.Users, Assistant.Permissions, Current, Uow, Time, this);
    }
    public StructuredCatalogProposalAcceptanceRequest Request(string culture = "es") => new(
        Assistant.Context with { ChannelId = "whatsapp-assistant", SubjectUserId = Assistant.Actor, Culture = culture },
        new(Assistant.TurnId, Guid.NewGuid(), new("forms.catalog-filters.propose", 1), CatalogAssistantFixture.Proposal)
        { ObservedReads = [Assistant.Observed()] },
        new("fictitious-profile", 3, "fictitious-instructions", 7, Time.GetUtcNow().UtcDateTime.AddMinutes(-2), "whatsapp-assistant"));
    public BusinessFormCatalog Pending(string culture = "es")
    {
        var request = Request(culture);
        var payload = (FormCatalogProposalPayload)request.Proposal.Payload;
        var evidence = new FormCatalogProposalEvidence(culture, Assistant.Actor, request.Provenance.OccurredAtUtc,
            request.Proposal.TurnId, request.Proposal.Capability.Id, request.Proposal.Capability.Version, request.Provenance, payload, 1);
        var catalog = BusinessFormCatalog.CreateProposedFilters(culture, new Dictionary<string,string> { [culture] = payload.Name },
            new Dictionary<string,string> { [culture] = payload.Description }, JsonSerializer.Serialize(payload.Filters, JsonSerializerOptions.Web),
            request.Proposal.Id, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web));
        Catalogs.Add(catalog);
        return catalog;
    }
    public ApproveFormCatalogProposalRequest Approval(BusinessFormCatalog catalog, string culture = "es") => new(catalog.Revision,
        new Dictionary<string,string?> { [culture] = "Reviewed supervisors" },
        new Dictionary<string,string?> { [culture] = "Reviewed active supervisors" }, CatalogAssistantFixture.Proposal.Filters, Assistant.Actor);
    public Task LockAsync(Guid id, CancellationToken ct)
    {
        TransactionGuard.Require(() => Uow.InTransaction);
        Locks.Add(id); WhileLocking?.Invoke(); WhileLocking = null;
        return Task.CompletedTask;
    }
    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Catalogs.SingleOrDefault(catalog => catalog.Id == id));
    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid id, CancellationToken ct) => Task.FromResult(Catalogs.SingleOrDefault(catalog => catalog.ProposalId == id));
    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public void Add(BusinessFormCatalog catalog) { TransactionGuard.Require(() => Uow.InTransaction); Catalogs.Add(catalog); }
    public void AddItems(IEnumerable<BusinessFormCatalogItem> items) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken ct) => Task.FromResult(Roles);
    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsActiveSampleUserAsync(Guid id, CancellationToken ct) => Task.FromResult(SampleExists);
    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid sample, FormCatalogFilterDefinition filters, CancellationToken ct)
    { PreviewReads++; return Task.FromResult(Preview); }
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        var catalog = Catalogs.SingleOrDefault(value => value.Id == id);
        return Task.FromResult(catalog is null ? null : new FormCatalogStoredRow(catalog.Id, catalog.Key, catalog.NameJson,
            catalog.DescriptionJson, catalog.Kind.ToString(), catalog.Revision, catalog.IsActive, catalog.CreatedAtUtc,
            catalog.CreatedBy, catalog.ModifiedAtUtc, catalog.ModifiedBy, 0, catalog.FiltersJson, catalog.ProposalEvidenceJson,
            catalog.ProposalState, catalog.ProposalDecidedByUserId, catalog.ProposalDecidedAtUtc));
    }
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => Task.FromResult(0);
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool edit, bool read, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();

    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> FindFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken) => throw new NotSupportedException();
}
