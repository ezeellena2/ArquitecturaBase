using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal static class WhatsAppAssistantAcceptanceFixture
{
    public static WhatsAppAssistantAutomaticPolicy CreateAutomatic(WhatsAppAssistantSuggestionFixture host)=>
        new(Create(host),host.Reader,host.Context.Clock,host.Conversation,host.Messages,new WhatsAppAssistantTerminalFixture().Policy,new(host.Reader,host.Context.Clock),
            CreateReply(host));
    /// <summary>
    /// Task 9 of the documents: the circuit's policy, with the inbox's port the test gives (none answers Unavailable). The send it resolves
    /// is revalidated by the acceptance: give both the same sending policy, whose bindings keep their ids like the database's.
    /// </summary>
    public static WhatsAppAssistantDocumentPolicy CreateDocuments(WhatsAppAssistantSuggestionFixture host, IStructuredDocumentChannel? documents = null,
        WhatsAppFormSendPolicy? sending = null) =>
        new(documents ?? new ArquitecturaBase.Application.Channels.UnavailableStructuredDocumentChannel(), sending ?? CreateSending(host), new Environment(host.UnitOfWork),
            host.Assistant, host.Reader, host.Context.Clock, CreateSpreadsheets(host, documents, sending));

    /// <summary>VC1-A (task 29): the rows' circuit, with the rows' port the test gives (none answers Unavailable) and the inbox of the documents.</summary>
    public static WhatsAppAssistantSpreadsheetPolicy CreateSpreadsheets(WhatsAppAssistantSuggestionFixture host, IStructuredDocumentChannel? documents = null,
        WhatsAppFormSendPolicy? sending = null, IStructuredDocumentRowsChannel? rows = null) =>
        new(rows ?? new ArquitecturaBase.Application.Channels.UnavailableStructuredDocumentRowsChannel(),
            documents ?? new ArquitecturaBase.Application.Channels.UnavailableStructuredDocumentChannel(), sending ?? CreateSending(host), new Environment(host.UnitOfWork),
            host.Assistant, host.Reader, host.Context.Clock);

    /// <summary>VC1-A (task 29): the notice and the loads of a spreadsheet, accepted with the chat's own doubles.</summary>
    public static WhatsAppAssistantLoadAcceptancePolicy CreateLoads(WhatsAppAssistantSuggestionFixture host, WhatsAppFormSendPolicy? sending = null) =>
        new(sending ?? CreateSending(host), host.Sessions, host.Conversation, host.Requests, host.Reader, new(host.Protector, host.Context.Clock), host.Context.Clock);
    /// <summary>Derivar (AS4) with the chat's own doubles.</summary>
    public static WhatsAppAssistantHandoffPolicy CreateHandoffs(WhatsAppAssistantSuggestionFixture host) =>
        new(host.Conversation, new WhatsAppFormActionPolicy(host.Context.Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true),
            host.Context.Clock), host.Context.Clock);
    /// <summary>Casos del asistente: the replies of the agent, with no agent saved unless the test gives one.</summary>
    public static WhatsAppAssistantReplyPolicy CreateReply(WhatsAppAssistantSuggestionFixture host, IWhatsAppAssistantAgentReader? agents = null,
        IWhatsAppAssistantHistoryReader? history = null) =>
        new(agents ?? new FakeWhatsAppAssistantAgentReader(), history ?? new FakeWhatsAppAssistantHistoryReader(),
            new(host.Conversation, new WhatsAppFormActionPolicy(host.Context.Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true),
                host.Context.Clock), host.Context.Clock), host.Conversation, host.Requests, host.Messages, host.Context.Clock);
    public static WhatsAppAssistantAcceptancePolicy Create(WhatsAppAssistantSuggestionFixture host, WhatsAppFormSendPolicy? shared = null)
    {
        var sending = shared ?? CreateSending(host);
        return new(sending,host.Forms,host.Sessions,host.Conversation,new(host.Protector,host.Context.Clock),host.Context.Clock,host.Presentation,CreateHandoffs(host));
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
        public Result<WhatsAppFlowAccountContext> ConfiguredAccount() => Account;
        public Task<Result<WhatsAppFlowAccountContext>> VerifyAccountAsync(CancellationToken ct)
        {Assert.False(transaction.InTransaction);return Task.FromResult(Result.Success(Account));}
    }
    private sealed class Bindings(FakeAssistantForms forms) : IWhatsAppFlowSessionReader
    {
        public Task<IReadOnlyList<Guid>> ListContactsWithPendingLocationsAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>([]);
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
