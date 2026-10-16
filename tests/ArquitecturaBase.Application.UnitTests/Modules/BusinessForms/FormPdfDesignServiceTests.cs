using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPdfDesignServiceTests
{
    [Fact]
    public async Task Saving_a_design_creates_its_own_revision_without_publishing_or_changing_the_draft()
    {
        var fixture=new FormPdfFixture(); var definition=fixture.Rules.Definition;
        var initial=await fixture.Service.GetCurrentAsync(definition.Id,TestContext.Current.CancellationToken);
        Assert.True(initial.IsSuccess); Assert.Equal(0,initial.Value.Revision); Assert.Null(initial.Value.Id);
        var original=definition.Revision;
        var saved=await fixture.Service.SaveAsync(definition.Id,new(0,initial.Value.Design),TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess); Assert.Equal(1,saved.Value.Revision); Assert.Equal(saved.Value.Id,definition.ActivePdfDesignId);
        Assert.Equal(original,definition.Revision); Assert.Null(definition.ActiveVersionId); Assert.Single(fixture.Designs);
        var old=fixture.Designs[0].DesignJson;
        var changed=await fixture.Service.SaveAsync(definition.Id,new(1,saved.Value.Design with {IncludePhotos=false}),TestContext.Current.CancellationToken);
        Assert.True(changed.IsSuccess); Assert.Equal(2,changed.Value.Revision); Assert.Equal(old,fixture.Designs[0].DesignJson);
        var stale=await fixture.Service.SaveAsync(definition.Id,new(1,saved.Value.Design),TestContext.Current.CancellationToken);
        Assert.Equal(FormPdfErrors.RevisionConflict,stale.Error); Assert.Equal(2,fixture.Designs.Count);
    }
    [Fact]
    public async Task Revocation_at_the_write_lock_prevents_a_design_and_revocation_at_read_discards_it()
    {
        var fixture=new FormPdfFixture(); var definition=fixture.Rules.Definition;
        var initial=await fixture.Service.GetCurrentAsync(definition.Id,TestContext.Current.CancellationToken);
        fixture.Rules.BeforeLock=()=>fixture.Rules.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await fixture.Service.SaveAsync(definition.Id,new(0,initial.Value.Design),TestContext.Current.CancellationToken)).Error);
        Assert.Empty(fixture.Designs);
        fixture.Rules.BeforeLock=null;
        fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RulesManage];
        fixture.BeforeCurrentReturn=()=>fixture.Rules.Permissions.Permissions.Clear();
        Assert.Equal(FormDefinitionErrors.Forbidden,(await fixture.Service.GetCurrentAsync(definition.Id,TestContext.Current.CancellationToken)).Error);
    }
    [Fact]
    public async Task Preview_uses_example_data_and_falls_back_completely_without_saving_or_issuing()
    {
        var fixture=new FormPdfFixture(); var definition=fixture.Rules.Definition;
        var design=new FormPdfDesign(new Dictionary<string,string>{["es"]="Visita"},[fixture.Rules.FieldId],true,null,true);
        var result=await fixture.Service.PreviewAsync(definition.Id,new(design,"en"),TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess); await result.Value.Content.DisposeAsync();
        Assert.Equal("es",result.Value.Culture); Assert.Equal("es",fixture.RenderCulture);
        Assert.Equal("Prueba",fixture.Rendered!.AppName); Assert.Equal(0,fixture.Rendered.DesignRevision);
        Assert.Empty(fixture.Designs); Assert.Equal(0,fixture.Rules.UnitOfWork.Transactions);
        Assert.Null(definition.ActivePdfDesignId);
    }
}
