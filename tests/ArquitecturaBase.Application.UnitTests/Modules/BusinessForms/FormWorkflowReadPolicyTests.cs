using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
public sealed class FormWorkflowReadPolicyTests
{
    [Fact]
    public async Task Completed_runs_observe_all_their_recipients_in_one_local_batch_without_effects()
    {
        var (fixture,recordId,runs)=await SeedAsync(FormActionRunState.Completed,StructuredRecordActionState.Accepted,count:2,role:true);
        var channel=new FakeFormActionChannel();var readAt=fixture.Clock.GetUtcNow().UtcDateTime;
        foreach(var person in fixture.Recipients.People.Keys)channel.Statuses[person]=new(StructuredRecordActionState.Accepted,Receipt:new(null,readAt));
        var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);Assert.True(response.IsSuccess);
        Assert.Equal(4,Assert.Single(channel.ObservationBatches).Count);Assert.Equal(4,channel.ObservationBatches[0].Distinct().Count());
        Assert.All(response.Value.Actions.SelectMany(action=>action.Recipients),recipient=>{Assert.Equal(readAt,recipient.ReadAtUtc);Assert.Null(recipient.DeliveredAtUtc);});
        Assert.All(runs,run=>Assert.Equal(FormActionRunState.Completed,run.State));AssertNoEffects(fixture,channel);
    }
    [Fact]
    public async Task An_observed_acceptance_removes_review_without_mutating_the_uncertain_run()
    {
        var (fixture,recordId,runs)=await SeedAsync(FormActionRunState.Uncertain,StructuredRecordActionState.Uncertain);
        var run=Assert.Single(runs);var original=(run.State,run.Fence,run.UpdatedAtUtc,run.OutcomeJson);var user=fixture.Recipients.People.Keys.Single();
        var channel=new FakeFormActionChannel();channel.Statuses[user]=new(StructuredRecordActionState.Accepted);
        var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);var action=Assert.Single(response.Value.Actions);var recipient=Assert.Single(action.Recipients);
        Assert.Equal("Accepted",recipient.State);Assert.False(recipient.CanReview);Assert.Equal("Uncertain",action.State);Assert.False(action.CanRetry);
        Assert.Equal(original,(run.State,run.Fence,run.UpdatedAtUtc,run.OutcomeJson));AssertNoEffects(fixture,channel);
    }
    [Fact]
    public async Task A_late_acceptance_keeps_the_human_no_send_review_and_adds_only_real_receipt_dates()
    {
        var (fixture,recordId,runs)=await SeedAsync(FormActionRunState.Completed,StructuredRecordActionState.Failed,role:true,review:true);
        var before=(await fixture.Runs.FindWorkflowAsync(recordId,CancellationToken.None))!;var channel=new FakeFormActionChannel();
        var delivered=fixture.Clock.GetUtcNow().UtcDateTime;var read=delivered.AddMinutes(1);
        foreach(var user in fixture.Recipients.People.Keys)channel.Statuses[user]=new(StructuredRecordActionState.Accepted,Receipt:new(delivered,read));
        var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);var action=Assert.Single(response.Value.Actions);
        Assert.Equal("Completed",action.State);Assert.False(action.CanRetry);
        foreach(var recipient in action.Recipients)
        {
            Assert.Equal("Accepted",recipient.State);Assert.False(recipient.CanReview);Assert.Equal(delivered,recipient.DeliveredAtUtc);Assert.Equal(read,recipient.ReadAtUtc);
            Assert.Equal(before.Actions[0].Recipients.Single(item=>item.User.UserId==recipient.User.UserId).Review,recipient.Review);Assert.False(recipient.Review!.WasSent);
        }
        Assert.All(runs,run=>Assert.Equal(FormActionRunState.Completed,run.State));AssertNoEffects(fixture,channel);
    }
    [Fact]
    public async Task A_late_acceptance_hides_an_obsolete_author_retry_without_changing_the_failed_run()
    {
        var (fixture,recordId,runs)=await SeedAsync(FormActionRunState.Failed,StructuredRecordActionState.Failed,review:true,canRetry:true);
        var before=(await fixture.Runs.FindWorkflowAsync(recordId,CancellationToken.None))!;
        Assert.True(Assert.Single(before.Actions).CanRetry);
        var run=Assert.Single(runs);var saved=(run.State,run.Fence,run.UpdatedAtUtc,run.OutcomeJson,run.CanRetry);
        var channel=new FakeFormActionChannel();channel.Statuses[fixture.Recipients.People.Keys.Single()]=new(StructuredRecordActionState.Accepted);
        var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);
        var action=Assert.Single(response.Value.Actions);
        Assert.Equal("Failed",action.State);Assert.Equal("Accepted",Assert.Single(action.Recipients).State);Assert.False(action.CanRetry);
        Assert.Equal(saved,(run.State,run.Fence,run.UpdatedAtUtc,run.OutcomeJson,run.CanRetry));AssertNoEffects(fixture,channel);
    }
    [Fact]
    public async Task Permission_revoked_during_local_observation_discards_the_whole_workflow()
    {
        var (fixture,recordId,_)=await SeedAsync(FormActionRunState.Completed,StructuredRecordActionState.Accepted);
        var channel=new FakeFormActionChannel{BeforeObservation=()=>fixture.Rules.Permissions.Permissions.Clear()};
        var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);
        Assert.Equal(FormDefinitionErrors.Forbidden,response.Error);Assert.Single(channel.ObservationBatches);AssertNoEffects(fixture,channel);
    }
    [Fact]
    public async Task No_read_permission_prevents_observations_and_missing_evidence_preserves_the_saved_result()
    {
        var (fixture,recordId,_)=await SeedAsync(FormActionRunState.Uncertain,StructuredRecordActionState.Uncertain);var channel=new FakeFormActionChannel();
        fixture.Rules.Permissions.Permissions.Clear();Assert.Equal(FormDefinitionErrors.Forbidden,(await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None)).Error);Assert.Empty(channel.ObservationBatches);
        Allow(fixture);var response=await Policy(fixture,channel).ReadAsync(recordId,CancellationToken.None);
        Assert.Equal("Uncertain",Assert.Single(Assert.Single(response.Value.Actions).Recipients).State);Assert.Single(channel.ObservationBatches);
        AssertNoEffects(fixture,channel);
    }
    [Fact]
    public void Repeated_channel_identifiers_are_configuration_errors()
    {
        var fixture=new FormActionFixture();Assert.Throws<InvalidOperationException>(()=>new FormWorkflowReadPolicy(fixture.Runs,new(fixture.Rules.User,fixture.Rules.Permissions),fixture.Rules.Permissions,[new FakeFormActionChannel(),new FakeFormActionChannel()]));
    }
    private static FormWorkflowReadPolicy Policy(FormActionFixture fixture,FakeFormActionChannel channel)=>new(fixture.Runs,new(fixture.Rules.User,fixture.Rules.Permissions),fixture.Rules.Permissions,[channel]);
    private static void Allow(FormActionFixture fixture)=>fixture.Rules.Permissions.Permissions[fixture.Rules.User.UserId!.Value]=[BusinessFormsPermissions.RecordsRead,BusinessFormsPermissions.RulesManage];
    private static void AssertNoEffects(FormActionFixture fixture,FakeFormActionChannel channel)
    {Assert.Empty(channel.Reads);Assert.Empty(channel.Messages);Assert.Empty(channel.Retries);Assert.Empty(channel.Reviews);Assert.Equal(0,fixture.Rules.UnitOfWork.Transactions);}
    private static async Task<(FormActionFixture Fixture,Guid RecordId,IReadOnlyList<BusinessFormActionRun> Runs)> SeedAsync(FormActionRunState state,StructuredRecordActionState recipientState,int count=1,bool role=false,bool review=false,bool canRetry=false)
    {
        var fixture=new FormActionFixture();var rules=Enumerable.Range(0,count).Select(_=>new FormRule(Guid.NewGuid(),"Received",[],[FormActionFixture.Action(role?"NotifyRole":"NotifyAuthor") with{RoleId=role?fixture.Rules.RoleId:null}])).ToArray();
        var captured=fixture.Capture(rules);if(role){var user=Guid.NewGuid();fixture.Recipients.People[user]=new(user,"es","Another employee");}Allow(fixture);
        await fixture.Recorder.RecordAsync(captured.Record,captured.Snapshot,"Received",CancellationToken.None);var now=fixture.Clock.GetUtcNow().UtcDateTime;
        foreach(var run in fixture.Runs.Runs)
        {
            var reservation=run.Reserve(now,TimeSpan.FromMinutes(2)).Value;
            var recipients=fixture.Recipients.People.Values.Select(person=>new FormActionRecipientOutcome(person.UserId,person.DisplayName,"phone",new(recipientState,Review:review?new(1,false,fixture.Rules.User.UserId!.Value,now):null))).ToArray();
            Assert.True(run.RecordOutcome(reservation.Fence,now,state,null,canRetry,FormDraftPolicy.Serialize(new FormActionOutcomeResponse(state,Recipients:recipients))).IsSuccess);
        }
        return(fixture,captured.Record.Id,fixture.Runs.Runs.ToArray());
    }
}
