using System.Security.Cryptography;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormPdfActionServiceTests
{
    [Fact]
    public async Task Preparing_freezes_the_current_design_and_never_implicitly_saves_a_default()
    {
        var fixture=new FormPdfFixture(); var store=new MemoryPrivateFileStore();
        var service=new FormPdfActionService(fixture,store,fixture,fixture,NullLogger<FormPdfActionService>.Instance);
        var record=fixture.RecordSnapshot();
        var prepared=await service.PrepareAsync(Guid.NewGuid(),record,TestContext.Current.CancellationToken);
        Assert.True(prepared.IsSuccess); Assert.Null(prepared.Value.DesignId); Assert.Equal(0,prepared.Value.DesignRevision);
        Assert.Empty(fixture.Designs); Assert.Null(fixture.Rules.Definition.ActivePdfDesignId); Assert.Equal(0,fixture.Renders);
        Assert.Equal("Prueba",prepared.Value.AppName);
        var saved=await fixture.Service.SaveAsync(fixture.Rules.Definition.Id,new(0,prepared.Value.Design with {Name=new Dictionary<string,string>{["es"]="Copia"}}),TestContext.Current.CancellationToken);
        Assert.True(saved.IsSuccess);
        var fixedCopy=await service.PrepareAsync(Guid.NewGuid(),record,TestContext.Current.CancellationToken);
        Assert.Equal(new[]{"es"},fixedCopy.Value.Cultures);
        await fixture.Service.SaveAsync(fixture.Rules.Definition.Id,new(1,saved.Value.Design with {IncludePhotos=false}),TestContext.Current.CancellationToken);
        Assert.True(fixedCopy.Value.Design.IncludePhotos); Assert.Equal(1,fixedCopy.Value.DesignRevision);
    }
    [Fact]
    public async Task Reconciliation_reads_partial_copies_without_rendering_and_only_missing_copies_are_generated()
    {
        var fixture=new FormPdfFixture(); var store=new MemoryPrivateFileStore();
        var service=new FormPdfActionService(fixture,store,fixture,fixture,NullLogger<FormPdfActionService>.Instance);
        var snapshot=(await service.PrepareAsync(Guid.NewGuid(),fixture.RecordSnapshot(),TestContext.Current.CancellationToken)).Value;
        var original=new byte[]{8,9,10};
        await using(var bytes=new MemoryStream(original))await store.WriteAsync(FormPdfStorageKey.Create(snapshot.RunId,"es"),bytes,"application/pdf",TestContext.Current.CancellationToken);
        var consulted=await service.ExecuteAsync(snapshot,true,TestContext.Current.CancellationToken);
        Assert.Equal(FormPdfActionState.NotFound,consulted.Value.State); Assert.Single(consulted.Value.Artifacts); Assert.Equal(0,fixture.Renders);
        var completed=await service.ExecuteAsync(snapshot,false,TestContext.Current.CancellationToken);
        Assert.Equal(FormPdfActionState.Completed,completed.Value.State); Assert.Equal(2,completed.Value.Artifacts.Count); Assert.Equal(1,fixture.Renders);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)),completed.Value.Artifacts.Single(artifact=>artifact.Culture=="es").Sha256);
        var repeated=await service.ExecuteAsync(snapshot,false,TestContext.Current.CancellationToken);
        Assert.Equal(FormPdfActionState.Completed,repeated.Value.State); Assert.Equal(1,fixture.Renders);
        Assert.Equal(completed.Value.Artifacts.Select(artifact=>artifact.Sha256),repeated.Value.Artifacts.Select(artifact=>artifact.Sha256));
    }
    [Fact]
    public async Task Unconfigured_storage_is_a_known_failure_without_an_external_effect()
    {
        var fixture=new FormPdfFixture(); var store=new MemoryPrivateFileStore(false);
        var service=new FormPdfActionService(fixture,store,fixture,fixture,NullLogger<FormPdfActionService>.Instance);
        var snapshot=(await service.PrepareAsync(Guid.NewGuid(),fixture.RecordSnapshot(),TestContext.Current.CancellationToken)).Value;
        var result=await service.ExecuteAsync(snapshot,false,TestContext.Current.CancellationToken);
        Assert.Equal(FormPdfActionState.Failed,result.Value.State); Assert.Equal(0,fixture.Renders);
    }
}
