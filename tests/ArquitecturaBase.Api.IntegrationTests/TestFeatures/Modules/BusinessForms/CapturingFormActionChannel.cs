using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;
internal sealed class CapturingFormActionChannel:IStructuredRecordActionChannel
{
    private readonly object _gate=new();private readonly Dictionary<(Guid,Guid),StructuredRecordActionStatus> _states=[];
    public string ChannelId=>"phone";public List<StructuredRecordActionMessage> Messages { get; }=[];
    public Task<StructuredRecordActionStatus> GetAsync(Guid runId,Guid userId,CancellationToken ct)
    {lock(_gate)return Task.FromResult(_states.GetValueOrDefault((runId,userId))??new(StructuredRecordActionState.NotFound));}
    public Task<StructuredRecordActionStatus> EnqueueAsync(StructuredRecordActionMessage message,CancellationToken ct)
    {
        lock(_gate)
        {
            if(_states.TryGetValue((message.RunId,message.RecipientUserId),out var prior))return Task.FromResult(prior);
            Messages.Add(message);return Task.FromResult(_states[(message.RunId,message.RecipientUserId)]=new(StructuredRecordActionState.Accepted));
        }
    }
    public Task<StructuredRecordActionStatus> RetryAsync(Guid runId,Guid userId,CancellationToken ct)=>GetAsync(runId,userId,ct);
}
