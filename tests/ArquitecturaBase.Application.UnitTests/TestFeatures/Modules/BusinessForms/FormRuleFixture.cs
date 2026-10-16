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
    public List<BusinessFormRuleSet> RuleSets { get; } = [];
    public Action? BeforeLock { get; set; }
    public Action? BeforeCurrentReturn { get; set; }
    public FormRuleService Service { get; }
    public FormRuleFixture()
    {
        Permissions.Permissions[User.UserId!.Value] = [BusinessFormsPermissions.RulesManage];
        Definition.SetDraft(1,"Visitas",FormDraftPolicy.Serialize(new Dictionary<string,string> { ["es"] = "Visitas", ["en"] = "Visits" }),"VISIT","es","{}",
            FormDraftPolicy.Serialize(new FormQuestionSchema(1,[new(FieldId,"visit","shortText",new Dictionary<string,string> { ["es"] = "Visita",["en"] = "Visit" },null,false,80,null)])),1,true);
        Service = new(this,this,this,new(User,Permissions),UnitOfWork,
            RequestValidators.For(new SaveFormRulesRequestValidator(),new ListFormRuleRevisionsRequestValidator()),new(this,this,this),NullLogger<FormRuleService>.Instance);
    }
    public Task LockDefinitionsAsync(CancellationToken ct) { BeforeLock?.Invoke(); return Task.CompletedTask; }
    Task<BusinessFormDefinition?> IFormDraftRepository.GetAsync(Guid id,CancellationToken ct) => Task.FromResult<BusinessFormDefinition?>(id == Definition.Id ? Definition : null);
    Task<BusinessFormRuleSet?> IFormRuleRepository.GetAsync(Guid id,CancellationToken ct) => Task.FromResult(RuleSets.SingleOrDefault(rule => rule.Id == id));
    public void Add(BusinessFormRuleSet ruleSet) => RuleSets.Add(ruleSet);
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid id,CancellationToken ct) => Task.FromResult<FormRuleDefinitionRow?>(id == Definition.Id ? new(id,Definition.NameJson,Definition.Culture,Definition.SchemaJson,[]) : null);
    public Task<FormRuleSetStoredRow?> FindCurrentAsync(Guid id,CancellationToken ct) { BeforeCurrentReturn?.Invoke(); return Task.FromResult(Map(RuleSets.SingleOrDefault(rule => rule.Id == Definition.ActiveRuleSetId))); }
    public Task<FormRuleSetStoredRow?> FindRevisionAsync(Guid id,long revision,CancellationToken ct) => Task.FromResult(Map(RuleSets.SingleOrDefault(rule => rule.DefinitionId == id && rule.Revision == revision)));
    public Task<PagedResult<FormRuleRevisionResponse>> ListRevisionsAsync(Guid id,ListFormRuleRevisionsRequest request,CancellationToken ct) => Task.FromResult(new PagedResult<FormRuleRevisionResponse>(RuleSets.Where(rule=>rule.DefinitionId==id).OrderByDescending(rule=>rule.Revision).Select(rule=>new FormRuleRevisionResponse(rule.Id,rule.Revision,rule.CreatedAtUtc,new(User.UserId!.Value,"Manager"),rule.HasApproval)).ToArray(),1,20,RuleSets.Count));
    public Task<IReadOnlyList<FormRuleRoleResponse>> ListRolesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<FormRuleRoleResponse>>([new(RoleId,"Supervisor")]);
    private FormRuleSetStoredRow? Map(BusinessFormRuleSet? rule) => rule is null ? null : new(rule.Id,rule.DefinitionId,rule.Revision,rule.OriginalCulture,rule.RulesJson,rule.CreatedAtUtc,new(User.UserId!.Value,"Manager"));
    public Task LockRequestAsync(Guid actor,Guid request,CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor,Guid request,CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsPrefixAsync(string prefix,Guid except,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormRetiredIdentities> FindRetiredIdentitiesAsync(Guid id,IReadOnlyCollection<Guid> fields,IReadOnlyCollection<Guid> choices,CancellationToken ct) => throw new NotSupportedException();
    public void AddRetiredIdentities(Guid id,FormRetiredIdentities identities) => throw new NotSupportedException();
    public void Add(BusinessFormDefinition definition) => throw new NotSupportedException();
    public void AddChange(BusinessFormDraftChange change) => throw new NotSupportedException();
    public Task UpdateExpiredResultsAsync(DateTime time,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request,bool canEdit,bool canRead,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id,CancellationToken ct) => Task.FromResult<FormCatalogStoredRow?>(null);
    public Task<int> CountUsageAsync(Guid id,CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id,ListFormCatalogItemsRequest request,CancellationToken ct) => throw new NotSupportedException();
    Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> IFormCatalogFilterReader.ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request,CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsActiveSampleUserAsync(Guid id,CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid actor,FormCatalogFilterDefinition filters,CancellationToken ct) => throw new NotSupportedException();
}
