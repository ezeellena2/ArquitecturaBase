using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using Microsoft.Extensions.Logging;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormActionChannel:IStructuredRecordActionChannel
{
    public string ChannelId=>"phone";public Exception? ReadException { get; set; }
    public Dictionary<Guid,Exception> ReadExceptions { get; }=[];
    public Dictionary<Guid,StructuredRecordActionResponse> Statuses { get; }=[];
    public List<Guid> Reads { get; }=[];public List<StructuredRecordActionMessage> Messages { get; }=[];public List<Guid> Retries { get; }=[];public List<long> ControlRevisions { get; }=[];
    public List<IReadOnlyList<StructuredRecordActionObservationRequest>> ObservationBatches { get; }=[];
    public Action? BeforeObservation { get; set; }
    public Task<IReadOnlyList<StructuredRecordActionObservationResponse>> ObserveAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken ct)
    {
        ObservationBatches.Add(requests.ToArray());BeforeObservation?.Invoke();
        return Task.FromResult<IReadOnlyList<StructuredRecordActionObservationResponse>>(requests.Where(request=>Statuses.ContainsKey(request.RecipientUserId))
            .Select(request=>new StructuredRecordActionObservationResponse(request.RunId,request.RecipientUserId,Statuses[request.RecipientUserId])).ToArray());
    }
    public Task<StructuredRecordActionResponse> GetAsync(Guid runId,Guid recipientUserId,CancellationToken cancellationToken)
    {
        Reads.Add(recipientUserId);if(ReadException is {} exception)throw exception;if(ReadExceptions.TryGetValue(recipientUserId,out var specific))throw specific;
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
            ?new(StructuredRecordActionState.Accepted,Review:review):new(StructuredRecordActionState.Failed,"WhatsApp.FormAction.ReviewedNotSent",true,Review:review));
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
