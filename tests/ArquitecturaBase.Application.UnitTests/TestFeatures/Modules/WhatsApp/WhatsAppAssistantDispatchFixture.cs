using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantDispatchFixture
{
    public WhatsAppAssistantContextFixture Context { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public InMemoryWhatsAppMessageRepository Messages { get; }
    public FakeWhatsAppAssistantReader Reader { get; } = new();
    public Conversations Repository { get; }
    public WhatsAppOutgoingMessage Outgoing { get; }
    public WhatsAppChatDispatchService Service { get; }
    public WhatsAppOutgoingSendGuard Guard { get; }
    public FakeAssistantForms Forms { get; } = new();
    public DateTime Now => Context.Clock.GetUtcNow().UtcDateTime;

    private WhatsAppAssistantDispatchFixture()
    {
        Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        Outgoing = WhatsAppOutgoingMessage.CreateAssistantText(Context.Subject.Id, Context.Contact.Id, Guid.NewGuid(),
            "Recibimos el pedido", Now, Context.Contact.LinkRevision, Context.Contact.WaId!, Context.Contact.ModeRevision);
        Repository = new(Context.Contact, Outgoing, UnitOfWork);
        Messages = new(new LockLog { InTransaction = () => UnitOfWork.InTransaction });
        Guard=CreateGuard(Context,Reader,Forms);
        Service = new(Repository, Messages, UnitOfWork, Context.Clock,
            Guard,
            NullLogger<WhatsAppChatDispatchService>.Instance);
    }

    public static WhatsAppOutgoingSendGuard CreateGuard(WhatsAppAssistantContextFixture context, FakeWhatsAppAssistantReader? reader = null, FakeAssistantForms? forms = null) =>
        new(new FakePhoneNumberParser(), new(context.Users,context.Permissions),
            new(context.Users,new FakePhoneNumberParser(),new FakeWhatsAppAvailability(true,true),context.Clock),context.Policy,
            reader ?? new(),new Sessions(),forms ?? new FakeAssistantForms(),context.Clock);

    private sealed class Sessions : IWhatsAppFlowSessionReader
    {
        public Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId,CancellationToken ct) => Task.FromResult<WhatsAppFormDispatchRow?>(null);
        public Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId,Guid versionId,string culture,CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId,string tokenHash,CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit,CancellationToken ct) => throw new NotSupportedException();
    }

    public static async Task<WhatsAppAssistantDispatchFixture> CreateAsync()
    {
        var host = new WhatsAppAssistantDispatchFixture();
        var captured = await host.Context.Policy.CaptureAsync(host.Context.Contact,null,TestContext.Current.CancellationToken);
        Assert.True(captured.IsSuccess);
        host.Reader.Reference = new(Guid.NewGuid(),host.Context.Contact.Id,Guid.NewGuid(),host.Context.Subject.Id,host.Context.Subject.Id,
            WhatsAppAssistantRequestKind.Automatic,host.Context.Contact.ModeRevision,host.Context.Contact.LinkRevision,
            JsonSerializer.Serialize(captured.Value.Context,JsonSerializerOptions.Web),host.Now.AddDays(90),WhatsAppAssistantRequestState.Sent,
            Guid.NewGuid(),null,host.Outgoing.Id);
        return host;
    }

    public sealed class Conversations(WhatsAppContact contact, WhatsAppOutgoingMessage outgoing, FakeUnitOfWork transaction) : IWhatsAppConversationRepository
    {
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id,CancellationToken ct)
        { Assert.True(transaction.InTransaction); return Task.FromResult<WhatsAppContact?>(id==contact.Id?contact:null); }
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessage?>(outgoing.State==WhatsAppOutgoingMessageState.Pending?outgoing:null);
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id,CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessage?>(id==outgoing.Id?outgoing:null);
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff,CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppOutgoingMessage>>(outgoing.State==WhatsAppOutgoingMessageState.Sending&&outgoing.StartedAtUtc<=cutoff?[outgoing]:[]);
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid id,Guid actor,CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid id,Guid actor,Guid request,CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessage?>(id==contact.Id&&outgoing.RequestId==request&&outgoing.OperatorUserId==actor?outgoing:null);
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid id,CancellationToken ct) => Task.FromResult(false);
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid id,CancellationToken ct) => Task.FromResult(outgoing.State==WhatsAppOutgoingMessageState.Sending&&outgoing.MessageEffectStartedAtUtc.HasValue);
        public void Add(WhatsAppReadState row) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage row) => throw new NotSupportedException();
    }
}
