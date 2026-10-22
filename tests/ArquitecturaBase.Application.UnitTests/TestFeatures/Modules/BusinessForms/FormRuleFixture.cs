using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormRuleFixture : IFormDraftRepository, IFormRuleRepository, IFormRuleReader, IFormCatalogReader, IFormCatalogFilterReader
{
    public FakeCurrentUser User { get; } = new() { UserId = Guid.NewGuid() };
    public FakePermissionService Permissions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public BusinessFormDefinition Definition { get; } = BusinessFormDefinition.Create();
    public Guid FieldId { get; } = Guid.NewGuid();
    public Guid RoleId { get; } = Guid.NewGuid();
    public bool RoleAvailable { get; set; } = true;
    public Action? BeforeRolesReturn { get; set; }
    public List<BusinessFormRuleSet> RuleSets { get; } = [];
    public Action? BeforeLock { get; set; }
    public Action? BeforeCurrentReturn { get; set; }
    public Action? BeforeHistoryReturn { get; set; }
    public int RevisionReads { get; private set; }
    public Dictionary<Guid,FormCatalogStoredRow> Catalogs { get; } = [];
    public Dictionary<Guid,IReadOnlyList<FormCatalogItemResponse>> ManualItems { get; } = [];
    public FormCatalogFilterPreviewResponse FilterPreview { get; set; } = new([],0,false);
    public Guid? PreviewActor { get; private set; }
    public int ManualPageReads { get; private set; }
    public int ManualAllItemReads { get; private set; }
    public FormRuleService Service { get; }
    public FormRuleFixture(string originalCulture = "es")
    {
        Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.RulesManage];
        Definition.SetDraft(1,originalCulture == "es" ? "Visitas" : "Visits",FormDraftPolicy.Serialize(new Dictionary<string,string> { ["es"] = "Visitas", ["en"] = "Visits" }),"VISIT",originalCulture,"{}",
            FormDraftPolicy.Serialize(new FormQuestionSchema(1,[new(FieldId,"visit","shortText",new Dictionary<string,string> { ["es"] = "Visita",["en"] = "Visit" },null,false,80,null)])),1,true);
        Service = new(this,this,this,new(User,Permissions),UnitOfWork,
            RequestValidators.For(new SaveFormRulesRequestValidator(),new ListFormRuleRevisionsRequestValidator()),new(this,new(this,this)),NullLogger<FormRuleService>.Instance);
    }
    public Task LockDefinitionsAsync(CancellationToken ct) { BeforeLock?.Invoke(); return Task.CompletedTask; }
    Task<BusinessFormDefinition?> IFormDraftRepository.GetAsync(Guid id,CancellationToken ct) => Task.FromResult<BusinessFormDefinition?>(id == Definition.Id ? Definition : null);
    Task<BusinessFormRuleSet?> IFormRuleRepository.GetAsync(Guid id,CancellationToken ct) => Task.FromResult(RuleSets.SingleOrDefault(rule => rule.Id == id));
    public void Add(BusinessFormRuleSet ruleSet) => RuleSets.Add(ruleSet);
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid id,CancellationToken ct) => Task.FromResult<FormRuleDefinitionRow?>(id == Definition.Id ? new(id,Definition.NameJson,Definition.Culture,Definition.SchemaJson,[]) : null);
    public Task<FormRuleSetStoredRow?> FindCurrentAsync(Guid id,CancellationToken ct) { BeforeCurrentReturn?.Invoke(); return Task.FromResult(Map(RuleSets.SingleOrDefault(rule => rule.Id == Definition.ActiveRuleSetId))); }
    public Task<FormRuleSetStoredRow?> FindRevisionAsync(Guid id,long revision,CancellationToken ct)
    { RevisionReads++; return Task.FromResult(Map(RuleSets.SingleOrDefault(rule => rule.DefinitionId == id && rule.Revision == revision))); }
    public Task<PagedResult<FormRuleRevisionStoredRow>> ListRevisionsAsync(Guid id,ListFormRuleRevisionsRequest request,CancellationToken ct)
    {
        var revisions = RuleSets.Where(rule => rule.DefinitionId == id).ToArray();
        var ordered = request.Sort == "revision" ? revisions.OrderBy(rule => rule.Revision) : revisions.OrderByDescending(rule => rule.Revision);
        var rows = ordered.Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Select(rule => new FormRuleRevisionStoredRow(rule.Id, rule.Revision, rule.CreatedAtUtc, new(User.UserId!.Value,"Manager"), rule.HasApproval,
                rule.RulesJson, revisions.Where(previous => previous.Revision < rule.Revision).OrderByDescending(previous => previous.Revision)
                    .Select(previous => previous.RulesJson).FirstOrDefault())).ToArray();
        BeforeHistoryReturn?.Invoke();
        return Task.FromResult(new PagedResult<FormRuleRevisionStoredRow>(rows, request.Page, request.PageSize, revisions.Length));
    }
    public Task<IReadOnlyList<FormRuleRoleResponse>> ListRolesAsync(CancellationToken ct)
    {
        BeforeRolesReturn?.Invoke();
        return Task.FromResult<IReadOnlyList<FormRuleRoleResponse>>(RoleAvailable ? [new(RoleId,"Supervisor")] : []);
    }
    private FormRuleSetStoredRow? Map(BusinessFormRuleSet? rule) => rule is null ? null : new(rule.Id,rule.DefinitionId,rule.Revision,rule.OriginalCulture,rule.RulesJson,rule.CreatedAtUtc,new(User.UserId!.Value,"Manager"));
    public Task LockRequestAsync(Guid actor,Guid request,CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor,Guid request,CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsPrefixAsync(string prefix,Guid except,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormRetiredIdentities> FindRetiredIdentitiesAsync(Guid id,IReadOnlyCollection<Guid> fields,IReadOnlyCollection<Guid> choices,CancellationToken ct) => throw new NotSupportedException();
    public void AddRetiredIdentities(Guid id,FormRetiredIdentities identities) => throw new NotSupportedException();
    public void Remove(BusinessFormDefinition definition) => throw new NotSupportedException();
    public void Add(BusinessFormDefinition definition) => throw new NotSupportedException();
    public void AddChange(BusinessFormDraftChange change) => throw new NotSupportedException();
    public Task UpdateExpiredResultsAsync(DateTime time,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request,bool canEdit,bool canRead,CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct)
    {
        ManualAllItemReads++;
        var items = ManualItems.GetValueOrDefault(id) ?? [];
        return Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(items.OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new FormCatalogStoredItemRow(item.Id, id, item.Code, item.Text["es"]!, item.Text.GetValueOrDefault("en"),
                item.Detail.GetValueOrDefault("es"), item.Detail.GetValueOrDefault("en"), item.Position, item.IsActive)).ToArray());
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id,CancellationToken ct) => Task.FromResult(Catalogs.GetValueOrDefault(id));
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id,CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id,ListFormCatalogItemsRequest request,CancellationToken ct)
    {
        ManualPageReads++;
        var items = ManualItems.GetValueOrDefault(id) ?? [];
        return Task.FromResult(new PagedResult<FormCatalogItemResponse>(items.Skip((request.Page-1)*request.PageSize).Take(request.PageSize).ToArray(),request.Page,request.PageSize,items.Count));
    }
    Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> IFormCatalogFilterReader.ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request,CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsActiveSampleUserAsync(Guid id,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid actor,FormCatalogFilterDefinition filters,CancellationToken ct) { PreviewActor = actor; return Task.FromResult(FilterPreview); }
}
