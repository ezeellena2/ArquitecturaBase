using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Workers;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;
public sealed partial class FormDraftEndpointsTests
{
    // F2 (ARCA H11): an action that stopped its rule comes back to the worker until whoever loaded the response is told, once.
    [Fact]
    public async Task A_stopped_action_comes_back_to_the_worker_until_its_author_is_told_once()
    {
        using var actor=await OperatorAsync([BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RecordsCreate]);
        var action=new FormRuleAction(Guid.NewGuid(),"NotifyAuthor",null,new Dictionary<string,string>{{"es","Recibimos {número}"}},false);
        var rules=new[]{new FormRule(Guid.NewGuid(),"Received",[],[action])};
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
        var channel=new CapturingFormActionChannel();channel.Reachable.Add(author);
        await using var host=factory.WithWebHostBuilder(builder=>builder.ConfigureTestServices(services=>
        {
            services.RemoveAll<IStructuredRecordActionChannel>();services.AddSingleton<IStructuredRecordActionChannel>(channel);
        }));
        var runId=await factory.ExecuteDbContextAsync(async db=>
        {
            var run=await db.Set<BusinessFormActionRun>().SingleAsync(value=>value.RecordId==captured.RecordId,Ct);
            var now=factory.Clock.GetUtcNow().UtcDateTime;var fence=run.Reserve(now,TimeSpan.FromMinutes(2)).Value.Fence;
            Assert.True(run.RecordOutcome(fence,now,FormActionRunState.Failed,FormActionErrors.EffectUnconfirmed.Code,true).IsSuccess);
            await db.SaveChangesAsync(Ct);return run.Id;
        });
        async Task Poll()
        {
            using var worker=new FormActionBackgroundService(host.Services.GetRequiredService<IServiceScopeFactory>(),TimeProvider.System,
                Options.Create(new FormActionWorkerOptions{ProcessInBackground=false}),NullLogger<FormActionBackgroundService>.Instance);
            await worker.ProcessNextAsync(runId,Ct);
        }
        // The shared database keeps more than 100 due runs from other tests, and the worker's list fills with them before the
        // stalled ones (FormActionRunReader.ListDueIdsAsync): this run is checked against the same filter the list applies.
        Task<bool> NeedsNotice()=>factory.ExecuteDbContextAsync(db=>db.Set<BusinessFormActionRun>().AsNoTracking()
            .Where(BusinessFormActionRun.UnnoticedStall).AnyAsync(run=>run.Id==runId,Ct));
        Assert.True(await NeedsNotice());
        await Poll();
        var message=Assert.Single(channel.Messages);Assert.Equal(author,message.RecipientUserId);
        Assert.False(await NeedsNotice());
        await Poll();
        Assert.Single(channel.Messages);
        Assert.NotNull(await factory.ExecuteDbContextAsync(db=>db.Set<BusinessFormActionRun>().AsNoTracking().Where(run=>run.Id==runId).Select(run=>run.StallNoticedAtUtc).SingleAsync(Ct)));
    }
}
