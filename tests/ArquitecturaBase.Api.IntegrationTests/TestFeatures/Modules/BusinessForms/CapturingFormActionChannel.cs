using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal sealed class CapturingFormActionChannel:IStructuredRecordActionChannel
{
    private readonly object _gate=new();private readonly Dictionary<(Guid,Guid),StructuredRecordActionResponse> _states=[];
    public string ChannelId=>"phone";public List<StructuredRecordActionMessage> Messages { get; }=[];
    public Task<StructuredRecordActionResponse> GetAsync(Guid runId,Guid userId,CancellationToken ct)
    {lock(_gate)return Task.FromResult(_states.GetValueOrDefault((runId,userId))??new(StructuredRecordActionState.NotFound));}
    public Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage message,CancellationToken ct)
    {
        lock(_gate)
        {
            if(_states.TryGetValue((message.RunId,message.RecipientUserId),out var prior))return Task.FromResult(prior);
            Messages.Add(message);return Task.FromResult(_states[(message.RunId,message.RecipientUserId)]=new(StructuredRecordActionState.Accepted));
        }
    }
    public List<StructuredRecordActionReview> Reviews { get; }=[];
    public void SetState(Guid runId,Guid userId,StructuredRecordActionResponse status){lock(_gate)_states[(runId,userId)]=status;}
    public Task<StructuredRecordActionResponse> ReviewAsync(Guid runId,Guid userId,StructuredRecordActionReview review,CancellationToken ct)
    {
        lock(_gate)
        {
            Reviews.Add(review);return Task.FromResult(_states[(runId,userId)]=review.WasSent
                ?new(StructuredRecordActionState.Accepted,Review:review):new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.ReviewedNotSent",true,Review:review));
        }
    }
    public Task<StructuredRecordActionResponse> RetryAsync(Guid runId,Guid userId,long controlRevision,CancellationToken ct)=>GetAsync(runId,userId,ct);
}
