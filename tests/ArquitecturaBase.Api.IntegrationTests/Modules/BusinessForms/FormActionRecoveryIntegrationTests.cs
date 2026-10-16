using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    [Fact]
    public async Task Two_hosts_reconcile_an_effect_after_a_crash_without_resending_and_then_release_the_dependency()
    {
        using var actor=await OperatorAsync([BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RecordsCreate]);
        var firstAction=new FormRuleAction(Guid.NewGuid(),"NotifyAuthor",null,new Dictionary<string,string>{{"es","Recibimos {número}"}},false);
        var secondAction=firstAction with{Id=Guid.NewGuid()};var rules=new[]{new FormRule(Guid.NewGuid(),"Received",[],[firstAction,secondAction])};
        var seeded=await SeedSheetAsync(actor,0);var author=Guid.Parse(actor.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var versionId=await factory.ExecuteDbContextAsync(async db=>
        {
            var definition=await db.Set<BusinessFormDefinition>().SingleAsync(value=>value.Id==seeded.Id,Ct);
            var version=await db.Set<BusinessFormVersion>().SingleAsync(value=>value.Id==definition.ActiveVersionId,Ct);
            var ruleSet=BusinessFormRuleSet.Create(definition.Id,1,"es",System.Text.Json.JsonSerializer.Serialize(rules,SerializerOptions),false,version.SchemaJson);
            db.Set<BusinessFormRuleSet>().Add(ruleSet);definition.SetActiveRuleSetId(ruleSet.Id);await db.SaveChangesAsync(Ct);return version.Id;
        });
        var request=new StructuredRecordCaptureRequest(Guid.NewGuid(),seeded.Id,versionId,author,factory.Clock.GetUtcNow().UtcDateTime,new Dictionary<string,string>());
        var captured=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(request,Ct));
        Assert.Equal("Completed",captured.State);
        var replay=await factory.ExecuteScopeAsync(services=>services.GetRequiredService<IBusinessFormRecordCaptureService>().CaptureAsync(request,Ct));
        Assert.Equal(captured.RecordId,replay.RecordId);
        var runs=await factory.ExecuteDbContextAsync(db=>db.Set<BusinessFormActionRun>().AsNoTracking().Where(run=>run.RecordId==captured.RecordId).OrderBy(run=>run.Order).Select(run=>run.Id).ToArrayAsync(Ct));
        Assert.Equal(2,runs.Length);var clock=new FakeTimeProvider(factory.Clock.GetUtcNow());var channel=new CapturingFormActionChannel();
        void Configure(IServiceCollection services)
        {
            services.RemoveAll<TimeProvider>();services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IStructuredRecordActionChannel>();services.AddSingleton<IStructuredRecordActionChannel>(channel);
            services.Configure<FormActionWorkerOptions>(options=>options.ProcessInBackground=false);
        }
        await using var first=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(Configure));
        await using var second=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(Configure));
        var start=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<FormActionWorkResponse?> ReserveAtStart(IServiceProvider services)
        {
            await start.Task;return await InActionScopeAsync(services,async scope=>(await scope.GetRequiredService<IFormActionDispatchService>().ReserveAsync(runs[0],Ct)).Value);
        }
        var attempts=new[]{ReserveAtStart(first.Services),ReserveAtStart(second.Services)};start.SetResult();var reserved=await Task.WhenAll(attempts);
        var work=Assert.Single(reserved,value=>value is not null)!;
        var blocked=await InActionScopeAsync(second.Services,async scope=>(await scope.GetRequiredService<IFormActionDispatchService>().ReserveAsync(runs[1],Ct)).Value);
        Assert.Null(blocked);
        var outcome=await InActionScopeAsync(first.Services,scope=>scope.GetRequiredService<IFormActionEffectService>().ExecuteAsync(work,Ct));
        Assert.Equal(FormActionRunState.Completed,outcome.State);
        clock.Advance(TimeSpan.FromMinutes(3));
        var recovery=await InActionScopeAsync(second.Services,async scope=>(await scope.GetRequiredService<IFormActionDispatchService>().ReserveAsync(runs[0],Ct)).Value);
        Assert.NotNull(recovery);Assert.True(recovery.ReconciliationOnly);
        var stale=await InActionScopeAsync(first.Services,scope=>scope.GetRequiredService<IFormActionDispatchService>().CompleteAsync(work,outcome,Ct));
        Assert.Equal(FormActionErrors.StateConflict,stale.Error);
        var observed=await InActionScopeAsync(second.Services,scope=>scope.GetRequiredService<IFormActionEffectService>().ExecuteAsync(recovery,Ct));
        Assert.True((await InActionScopeAsync(second.Services,scope=>scope.GetRequiredService<IFormActionDispatchService>().CompleteAsync(recovery,observed,Ct))).IsSuccess);
        Assert.Single(channel.Messages,message=>message.RunId==runs[0]);
        var next=await InActionScopeAsync(second.Services,async scope=>(await scope.GetRequiredService<IFormActionDispatchService>().ReserveAsync(runs[1],Ct)).Value);
        Assert.NotNull(next);
        var nextOutcome=await InActionScopeAsync(second.Services,scope=>scope.GetRequiredService<IFormActionEffectService>().ExecuteAsync(next,Ct));
        Assert.True((await InActionScopeAsync(second.Services,scope=>scope.GetRequiredService<IFormActionDispatchService>().CompleteAsync(next,nextOutcome,Ct))).IsSuccess);
        Assert.Equal(2,channel.Messages.Count);
    }
    private static async Task<T> InActionScopeAsync<T>(IServiceProvider services,Func<IServiceProvider,Task<T>> action)
    {await using var scope=services.CreateAsyncScope();return await action(scope.ServiceProvider);}
}
