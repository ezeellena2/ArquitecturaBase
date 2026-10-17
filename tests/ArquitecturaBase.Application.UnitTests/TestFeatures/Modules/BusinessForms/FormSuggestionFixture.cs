using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormSuggestionFixture : IFormSuggestionRepository,IFormSuggestionReader
{
    public FormRuleFixture Drafts { get; }=new();
    public BusinessFormDefinition Definition=>Drafts.Definition;
    public List<BusinessFormSuggestionSet> Sets { get; }=[];
    public FormSuggestionOptionsResponse? Options { get; set; }
    public Guid DestinationId { get; }=Guid.NewGuid();
    public Action? BeforeCurrentReturn { get; set; }
    public Action? BeforeOptionsReturn { get; set; }
    public Action? BeforeHistoryReturn { get; set; }
    public int RevisionReads { get; private set; }
    public FormSuggestionService Service { get; }
    public FormSuggestionFixture()
    {
        Drafts.Permissions.Permissions[Drafts.User.UserId!.Value]=[BusinessFormsPermissions.SuggestionsManage];
        var version=Guid.NewGuid();
        Definition.ActivateVersion(null,version);
        var questions=FormDraftPolicy.Read<FormQuestionSchema>(Definition.SchemaJson).Fields;
        Options=new(new(Definition.Id,version,new Dictionary<string,string>{["es"]="Visita"},"es",questions),
            [new(DestinationId,Guid.NewGuid(),new Dictionary<string,string>{["es"]="Reclamo"},"es",[])]);
        Service=new(Drafts,this,this,new(Drafts.User,Drafts.Permissions),Drafts.UnitOfWork,
            RequestValidators.For(new SaveFormSuggestionsRequestValidator(),new ListFormSuggestionRevisionsRequestValidator()),new(Drafts,Drafts),NullLogger<FormSuggestionService>.Instance);
    }
    public FormSuggestionInput Suggestion()=>new(Guid.NewGuid(),DestinationId,"Assistant","Cuando requiera un reclamo",true,[],[]);
    public Task<BusinessFormSuggestionSet?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult(Sets.SingleOrDefault(set=>set.Id==id));
    public void Add(BusinessFormSuggestionSet set)=>Sets.Add(set);
    public Task<bool> ExistsDefinitionAsync(Guid id,CancellationToken ct)=>Task.FromResult(id==Definition.Id);
    public Task<FormSuggestionOptionsResponse?> FindOptionsAsync(Guid id,CancellationToken ct){BeforeOptionsReturn?.Invoke();return Task.FromResult(id==Definition.Id?Options:null);}
    public Task<FormSuggestionSetStoredRow?> FindCurrentAsync(Guid id,CancellationToken ct){BeforeCurrentReturn?.Invoke();return Task.FromResult(Map(Sets.SingleOrDefault(set=>set.Id==Definition.ActiveSuggestionSetId&&set.DefinitionId==id)));}
    public Task<FormSuggestionSetStoredRow?> FindRevisionAsync(Guid id,long revision,CancellationToken ct){RevisionReads++;return Task.FromResult(Map(Sets.SingleOrDefault(set=>set.DefinitionId==id&&set.Revision==revision)));}
    public Task<PagedResult<FormSuggestionRevisionStoredRow>> ListRevisionsAsync(Guid id,ListFormSuggestionRevisionsRequest request,CancellationToken ct)
    {
        var rows=Sets.Where(set=>set.DefinitionId==id).ToArray();
        var ordered=request.Sort=="revision"?rows.OrderBy(set=>set.Revision):rows.OrderByDescending(set=>set.Revision);
        var result=ordered.Skip((request.Page-1)*request.PageSize).Take(request.PageSize).Select(set=>new FormSuggestionRevisionStoredRow(set.Id,set.Revision,set.CreatedAtUtc,
            new(Drafts.User.UserId!.Value,"Manager"),set.SuggestionsJson,rows.Where(before=>before.Revision<set.Revision).OrderByDescending(before=>before.Revision).Select(before=>before.SuggestionsJson).FirstOrDefault())).ToArray();
        BeforeHistoryReturn?.Invoke();
        return Task.FromResult(new PagedResult<FormSuggestionRevisionStoredRow>(result,request.Page,request.PageSize,rows.Length));
    }
    private FormSuggestionSetStoredRow? Map(BusinessFormSuggestionSet? set)=>set is null?null:new(set.Id,set.DefinitionId,set.SourceVersionId,set.Revision,set.OriginalCulture,set.SuggestionsJson,set.CreatedAtUtc,new(Drafts.User.UserId!.Value,"Manager"));
}
