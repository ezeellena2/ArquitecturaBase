using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FormPdfFixture : IFormPdfDesignRepository, IFormPdfDesignReader, IFormPdfRenderer, IAppName
{
    public FormRuleFixture Rules { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026,10,16,12,0,0,TimeSpan.Zero));
    public List<BusinessFormPdfDesign> Designs { get; } = [];
    public Action? BeforeCurrentReturn { get; set; }
    public int Renders { get; private set; }
    public FakeFormResponseReader Records { get; } = new();
    public FormActionRecordSnapshot RecordSnapshot()
    {
        var definition=Rules.Definition;
        var schema=new FormVersionSchema(1,FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson).Fields);
        var row=new FormRecordStoredRow(Guid.NewGuid(),Guid.NewGuid(),1,definition.Id,definition.Prefix,Guid.NewGuid(),1,definition.NameJson,FormDraftPolicy.Serialize(schema),"[]");
        var response = new FormResponseStoredRow(row,Clock.GetUtcNow().UtcDateTime,new(Rules.User.UserId!.Value,"Author"));
        Records.ExportRows = Records.ExportRows.Concat([response]).ToArray();
        return new(response,definition.Culture,new(Guid.NewGuid(),Guid.NewGuid(),"Approver",Clock.GetUtcNow().UtcDateTime,"Approved",null));
    }
    public FormPdfRenderRequest? Rendered { get; private set; }
    public string? RenderCulture { get; private set; }
    public string Value => "Prueba";
    public FormPdfDesignService Service { get; }
    public FormPdfFixture() => Service = new(Rules,this,this,new(Rules.User,Rules.Permissions),Rules.UnitOfWork,
        RequestValidators.For(new SaveFormPdfDesignRequestValidator(),new PreviewFormPdfDesignRequestValidator()),new(this,this,Clock),NullLogger<FormPdfDesignService>.Instance);
    public Task<BusinessFormPdfDesign?> GetAsync(Guid id,CancellationToken ct) => Task.FromResult(Designs.SingleOrDefault(design=>design.Id==id));
    public void Add(BusinessFormPdfDesign design) => Designs.Add(design);
    public Task<FormRuleDefinitionRow?> FindDefinitionAsync(Guid id,CancellationToken ct) => Rules.FindDefinitionAsync(id,ct);
    public Task<FormPdfDesignStoredRow?> FindCurrentAsync(Guid id,CancellationToken ct) { BeforeCurrentReturn?.Invoke(); return Task.FromResult(Map(Designs.SingleOrDefault(design=>design.Id==Rules.Definition.ActivePdfDesignId))); }
    public Task<FormPdfDesignStoredRow?> FindRevisionAsync(Guid id,long revision,CancellationToken ct) => Task.FromResult(Map(Designs.SingleOrDefault(design=>design.DefinitionId==id&&design.Revision==revision)));
    public Task<FormPdfDocumentRow?> FindDocumentAsync(Guid recordId,Guid documentId,CancellationToken ct) => Task.FromResult<FormPdfDocumentRow?>(null);
    public Task<byte[]> RenderAsync(FormPdfRenderRequest request,string culture,bool preview,CancellationToken ct) { Renders++; Rendered=request; RenderCulture=culture; return Task.FromResult(new byte[]{1,2,3}); }
    private FormPdfDesignStoredRow? Map(BusinessFormPdfDesign? design) => design is null?null:new(design.Id,design.DefinitionId,design.Revision,design.OriginalCulture,design.DesignJson,design.CreatedAtUtc,new(Rules.User.UserId!.Value,"Manager"));
}
