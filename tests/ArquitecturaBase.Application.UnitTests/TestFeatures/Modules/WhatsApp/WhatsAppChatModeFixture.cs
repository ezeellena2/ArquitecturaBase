using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppChatModeFixture
{
    public static readonly DateTime Now = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
    public ModeRepository Repository { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public InMemoryUserAccounts Users { get; } = new();
    public FakeCurrentUser CurrentUser { get; }
    public ModeAssistant Assistant { get; } = new();
    public WhatsAppChatModeService Service { get; }
    public Guid Actor { get; }
    public WhatsAppChatModeFixture(string[] permissions, bool active = true)
    {
        Actor = Users.AddUser("mode-operator@example.test", isActive: active).Id;
        CurrentUser = new() { UserId = Actor };
        Permissions.Permissions[Actor] = permissions;
        Service = new(Repository, RequestValidators.For(new ChangeWhatsAppModeRequestValidator()), CurrentUser,
            UnitOfWork, Permissions, Users, Assistant, NullLogger<WhatsAppChatModeService>.Instance);
    }
    public sealed class ModeAssistant : IAssistantOperations
    {
        public string State { get; set; } = "available";
        public int Reads { get; private set; }
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct)
        { Reads++; return Task.FromResult(new AssistantAvailabilityResponse(State)); }
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) => throw new NotSupportedException();
    }
    public sealed class ModeRepository : IWhatsAppConversationRepository
    {
        public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493415550000", null, "Fictitious mode contact", Now);
        public bool Missing { get; set; }
        public int Locks { get; private set; }
        public bool StartedAssistantMessage { get; set; }
        public List<WhatsAppOutgoingMessage> Outgoing { get; } = [];
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id, CancellationToken ct)
        { Locks++; return Task.FromResult<WhatsAppContact?>(!Missing && id == Contact.Id ? Contact : null); }
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid id, CancellationToken ct) => Task.FromResult(Outgoing.Any(item => item.State is WhatsAppOutgoingMessageState.Pending or WhatsAppOutgoingMessageState.Sending or WhatsAppOutgoingMessageState.Uncertain));
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid contact, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contact, Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken ct) => Task.FromResult(StartedAssistantMessage);
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff, CancellationToken ct) => throw new NotSupportedException();
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing) => Outgoing.Add(outgoing);
    }
}
