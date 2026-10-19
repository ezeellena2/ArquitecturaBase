using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal static class WhatsAppAssistantAcceptanceFixture
{
    public static WhatsAppAssistantAutomaticPolicy CreateAutomatic(WhatsAppAssistantSuggestionFixture host)=>
        new(Create(host),host.Reader,host.Context.Clock,host.Conversation,host.Messages,new WhatsAppAssistantTerminalFixture().Policy,new(host.Reader,host.Context.Clock));
    public static WhatsAppAssistantAcceptancePolicy Create(WhatsAppAssistantSuggestionFixture host)
    {
        var sending = CreateSending(host);
        return new(sending,host.Forms,host.Sessions,host.Conversation,new(host.Protector,host.Context.Clock),host.Context.Clock,host.Presentation);
    }
    public static WhatsAppFormSendPolicy CreateSending(WhatsAppAssistantSuggestionFixture host) =>
        new(host.Context.Recipients, host.Forms, new Bindings(host.Forms), new Projection(host.Forms),
            new Environment(host.UnitOfWork), host.Context.Permissions, new FakePhoneNumberParser(), host.Context.Clock);
    public static WhatsAppFormMessageDispatchService CreateDispatch(WhatsAppAssistantSuggestionFixture host, WhatsAppFormDispatchRow row)
    {
        var sessions = new Bindings(host.Forms) { Dispatch = row };
        return new(sessions,new(host.Protector,host.Context.Clock),new(host.Context.Recipients,host.Forms,new Environment(host.UnitOfWork),
            host.Context.Permissions,new FakePhoneNumberParser(),new Projection(host.Forms),host.Context.Users,host.Context.Clock),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppFormMessageDispatchService>.Instance);
    }
    public static WhatsAppFormDispatchRow DispatchRow(WhatsAppAssistantSuggestionFixture host)
    {
        var outgoing=Assert.Single(host.Conversation.Outgoing);var session=Assert.Single(host.Sessions.Rows);
        return new(outgoing.Id,session.Id,session.TokenHash,session.ContactId,host.Context.Contact.WaId,session.UserId,session.OperatorUserId,
            session.DefinitionId,session.VersionId,session.BindingId,session.Culture,session.ProtectedPayload,session.ExpiresAtUtc,
            ArquitecturaBase.Domain.Modules.WhatsApp.WhatsAppOutgoingMessageState.Sending,outgoing.Body,
            host.Context.Contact.Mode,host.Context.Contact.ModeRevision,host.Context.Contact.LastInboundAtUtc)
        {Origin=session.Origin,ProtectedAssistantContent=session.ProtectedAssistantContent,AssistantContentExpiresAtUtc=session.AssistantContentExpiresAtUtc,
            BindingOperationId=Guid.NewGuid(),ProtectedProjection=[1]};
    }
    private static WhatsAppFlowAccountContext Account => new("1","2","v25.0",new string('a',64));
    private sealed class Environment(FakeUnitOfWork transaction) : IWhatsAppFlowEnvironment
    {
        public bool IsConfigured => true;
        public TimeSpan SessionLifetime => TimeSpan.FromHours(24);
        public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken ct)
        {Assert.False(transaction.InTransaction);return Task.FromResult(Result.Success(Account));}
    }
    private sealed class Bindings(FakeAssistantForms forms) : IWhatsAppFlowSessionReader
    {
        private readonly Guid _binding=Guid.NewGuid(),_operation=Guid.NewGuid(),_neutral=Guid.NewGuid();
        public Task<WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId,Guid versionId,string culture,CancellationToken ct)=>
            Task.FromResult<WhatsAppFormBindingRow?>(forms.Prefill.IsSuccess && forms.Prefill.Value.Culture==culture
                ?new(_binding,_operation,_neutral,definitionId,versionId,culture,"visit","123",[1],[2]):null);
        public WhatsAppFormDispatchRow? Dispatch {get;init;}
        public Task<WhatsAppFormDispatchRow?> FindDispatchAsync(Guid outgoingId,CancellationToken ct)=>Task.FromResult(Dispatch);
        public Task<WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid contactId,string tokenHash,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Projection(FakeAssistantForms forms) : IWhatsAppFlowProtector
    {
        public byte[] ProtectManifest(Guid operationId,Guid neutralId,Guid versionId,WhatsAppFlowStoredManifest manifest)=>throw new NotSupportedException();
        public byte[] ProtectProjection(Guid operationId,Guid bindingId,Guid versionId,string culture,WhatsAppFlowProjection projection)=>throw new NotSupportedException();
        public WhatsAppFlowStoredManifest ReadManifest(Guid operationId,Guid neutralId,Guid versionId,byte[] manifest)=>new(1,new string('b',64),new string('c',64),Account,[]);
        public WhatsAppFlowProjection ReadProjection(Guid operationId,Guid bindingId,Guid versionId,string culture,byte[] projection)=>
            new(1,JsonSerializer.Serialize(new{screens=new[]{new{id="FORM"}}}),new string('d',64),"flow-static-7.3-r8",1,culture,
                forms.Prefill.Value.Fields.Select(field=>new WhatsAppFlowProjectionField(field.FieldId,field.Key,field.Type,false,100,[])).ToArray());
    }
}
