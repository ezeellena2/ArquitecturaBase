using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantWorkerFixture : IAsyncDisposable
{
    public List<string> Events { get; } = [];
    public bool AllowPost { get; set; } = true;
    public bool CancelAfterProvider { get; set; }
    public bool Uncertain { get; set; }
    public bool CompletedWithLiveToken { get; private set; }
    public string? CompletedId { get; private set; }
    public bool CompletedUncertain { get; private set; }
    public WhatsAppOutboundMessage? SentMessage { get; private set; }
    public CancellationTokenSource Shutdown { get; } = new();
    public WhatsAppDispatchResponse Operation { get; set; } = new(Guid.NewGuid(),Guid.NewGuid(),PhoneNumber.Create("+5493515550101").Value,
        "Recibimos el pedido",Origin:WhatsAppMessageOrigin.Assistant,DispatchReservationId:Guid.NewGuid());
    private readonly ServiceProvider _services;
    public WhatsAppOutgoingBackgroundService Worker { get; }
    private bool _reserved;
    public WhatsAppAssistantWorkerFixture()
    {
        _services=new ServiceCollection().AddSingleton<IWhatsAppChatDispatchService>(new Dispatch(this))
            .AddSingleton<IWhatsAppCloudClient>(new Cloud(this)).AddSingleton<IWhatsAppFormMessageDispatchService>(new Forms(this)).BuildServiceProvider();
        Worker=new(_services.GetRequiredService<IServiceScopeFactory>(),new Coordinator(this),Options.Create(new WhatsAppOptions()),
            new FakeTimeProvider(new DateTimeOffset(2026,10,17,15,0,0,TimeSpan.Zero)),new WhatsAppHealth(),NullLogger<WhatsAppOutgoingBackgroundService>.Instance);
    }
    public async ValueTask DisposeAsync(){Shutdown.Dispose();Worker.Dispose();await _services.DisposeAsync();}
    private sealed class Dispatch(WhatsAppAssistantWorkerFixture host):IWhatsAppChatDispatchService
    {
        public Task<Result<WhatsAppDispatchResponse?>> ReserveAsync(CancellationToken ct)
        {host.Events.Add("reserve");var next=host._reserved?null:host.Operation;host._reserved=true;return Task.FromResult(Result.Success(next));}
        public Task<Result> RecoverExpiredAsync(CancellationToken ct)=>Task.FromResult(Result.Success());
        public Task<Result<bool>> MarkAssistantMessageEffectAsync(Guid contact,Guid operation,Guid reservation,CancellationToken ct)
        {Assert.Equal(host.Operation.DispatchReservationId,reservation);host.Events.Add("mark");return Task.FromResult(Result.Success(host.AllowPost));}
        public Task<Result<bool>> MarkFormActionMessageEffectAsync(Guid contact,Guid operation,Guid reservation,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Result<bool>> RecordFormActionMediaAsync(Guid operation,Guid reservation,string media,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Result> CompleteAsync(Guid contact,Guid operation,string? id,bool uncertain,CancellationToken ct,Guid? reservationId=null,string? failureCode=null)
        {host.Events.Add("complete");host.CompletedWithLiveToken=!ct.IsCancellationRequested;host.CompletedId=id;host.CompletedUncertain=uncertain;return Task.FromResult(Result.Success());}
    }
    private sealed class Coordinator(WhatsAppAssistantWorkerFixture host):IWhatsAppDispatchCoordinator
    {
        public Task<IWhatsAppDispatchLease> AcquireAsync(PhoneNumber recipient,CancellationToken ct)
        {host.Events.Add("lease");return Task.FromResult<IWhatsAppDispatchLease>(new Lease());}
        private sealed class Lease:IWhatsAppDispatchLease
        {public CancellationToken LostOwnership=>CancellationToken.None;public ValueTask DisposeAsync()=>ValueTask.CompletedTask;}
    }
    private sealed class Cloud(WhatsAppAssistantWorkerFixture host):IWhatsAppCloudClient
    {
        public Task<WhatsAppSendResult> SendAsync(WhatsAppOutboundMessage message,CancellationToken ct)
        {host.Events.Add("post");host.SentMessage=message;if(host.CancelAfterProvider)host.Shutdown.Cancel();return Task.FromResult(host.Uncertain?WhatsAppSendResult.Uncertain():WhatsAppSendResult.Sent("wamid.assistant-known"));}
    }
    private sealed class Forms(WhatsAppAssistantWorkerFixture host):IWhatsAppFormMessageDispatchService
    {
        public Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(Guid outgoingId,CancellationToken ct)
        {host.Events.Add("prepare");return Task.FromResult(Result.Success(new WhatsAppFlowMessageResponse(host.Operation.To,host.Operation.Body,"Abrir formulario","123","private-token","FORM",new("1","2","v25.0",new string('a',64)))));}
    }
}
