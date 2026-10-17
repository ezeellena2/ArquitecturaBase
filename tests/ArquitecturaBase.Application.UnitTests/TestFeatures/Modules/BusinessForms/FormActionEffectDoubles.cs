using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormActionChannel:IStructuredRecordActionChannel
{
    public string ChannelId=>"phone";public Exception? ReadException { get; set; }
    public Dictionary<Guid,StructuredRecordActionResponse> Statuses { get; }=[];
    public List<Guid> Reads { get; }=[];public List<StructuredRecordActionMessage> Messages { get; }=[];public List<Guid> Retries { get; }=[];public List<long> ControlRevisions { get; }=[];
    public Task<StructuredRecordActionResponse> GetAsync(Guid runId,Guid recipientUserId,CancellationToken cancellationToken)
    {
        Reads.Add(recipientUserId);if(ReadException is {} exception)throw exception;
        return Task.FromResult(Statuses.GetValueOrDefault(recipientUserId)??new(StructuredRecordActionState.NotFound));
    }
    public Task<StructuredRecordActionResponse> EnqueueAsync(StructuredRecordActionMessage message,CancellationToken cancellationToken)
    {
        Messages.Add(message);return Task.FromResult(Statuses[message.RecipientUserId]=new(StructuredRecordActionState.Accepted));
    }
    public List<(Guid RecipientUserId,StructuredRecordActionReview Review)> Reviews { get; }=[];
    public Task<StructuredRecordActionResponse> ReviewAsync(Guid runId,Guid recipientUserId,StructuredRecordActionReview review,CancellationToken ct)
    {
        Reviews.Add((recipientUserId,review));return Task.FromResult(Statuses[recipientUserId]=review.WasSent
            ?new(StructuredRecordActionState.Accepted):new(StructuredRecordActionState.Failed,"reviewedNotSent",true));
    }
    public Task<StructuredRecordActionResponse> RetryAsync(Guid runId,Guid recipientUserId,long controlRevision,CancellationToken cancellationToken)
    {
        ControlRevisions.Add(controlRevision);Retries.Add(recipientUserId);return Task.FromResult(Statuses[recipientUserId]=new(StructuredRecordActionState.Accepted));
    }
}
internal sealed class FormActionCaptureLogger<T>:ILogger<T>
{
    public List<string> Entries { get; }=[];
    public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;
    public bool IsEnabled(LogLevel logLevel)=>true;
    public void Log<TState>(LogLevel logLevel,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter)=>Entries.Add(formatter(state,exception));
}
