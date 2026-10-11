using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppChatServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Manual_reply_is_persisted_once_and_a_repeated_request_returns_the_same_operation()
    {
        var host = new Host();
        host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var request = new SendWhatsAppTextRequest(host.Repository.Contact.Id, Guid.NewGuid(), "Hola");
        var first = await host.Service.SendTextAsync(request, Ct);
        host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Bot);
        var repeated = await host.Service.SendTextAsync(request, Ct);
        Assert.True(first.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value.Id, repeated.Value.Id);
        Assert.Single(host.Repository.Outgoing);
        Assert.Equal(2, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Bot_mode_rejects_manual_reply_without_storing_a_send()
    {
        var host = new Host();
        host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Bot);
        var result = await host.Service.SendTextAsync(new(host.Repository.Contact.Id, Guid.NewGuid(), "Hola"), Ct);
        Assert.Equal(WhatsAppChatErrors.ManualRequired, result.Error);
        Assert.Empty(host.Repository.Outgoing);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Invalid_text_is_rejected_before_a_transaction()
    {
        var host = new Host();
        var result = await host.Service.SendTextAsync(new(host.Repository.Contact.Id, Guid.NewGuid(), " "), Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Reusing_a_request_id_for_different_content_is_a_conflict()
    {
        var host = new Host();
        host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        var requestId = Guid.NewGuid();
        await host.Service.SendTextAsync(new(host.Repository.Contact.Id, requestId, "Hola"), Ct);
        var result = await host.Service.SendTextAsync(new(host.Repository.Contact.Id, requestId, "Otro"), Ct);
        Assert.Equal(WhatsAppChatErrors.RequestConflict, result.Error);
        Assert.Single(host.Repository.Outgoing);
    }

    private sealed class Host
    {
        public Repository Repository { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public WhatsAppChatService Service { get; }
        public Host()
        {
            Service = new(new UnusedChatQueries(), Repository,
                RequestValidators.For(new SendWhatsAppTextRequestValidator()),
                new FakeCurrentUser { UserId = Guid.NewGuid() }, UnitOfWork,
                new FakeTimeProvider(Now), new FakePhoneNumberParser(), NullLogger<WhatsAppChatService>.Instance);
        }
    }

    private sealed class Repository : IWhatsAppConversationRepository
    {
        public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493413654813", null, "Ana", Now.UtcDateTime);
        public List<WhatsAppOutgoingMessage> Outgoing { get; } = [];
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult<WhatsAppContact?>(id == Contact.Id ? Contact : null);
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contact, Guid actor, Guid request, CancellationToken ct) =>
            Task.FromResult(Outgoing.FirstOrDefault(item => item.ContactId == contact && item.OperatorUserId == actor && item.RequestId == request));
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid contact, CancellationToken ct) => Task.FromResult(Outgoing.Count > 0);
        public void Add(WhatsAppOutgoingMessage outgoing) => Outgoing.Add(outgoing);
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid contact, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class UnusedChatQueries : IWhatsAppChatQueryService
    {
        public Task<Result<WhatsAppChatsResponse>> ListAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<WhatsAppThreadResponse>> GetThreadAsync(ListWhatsAppMessagesRequest request, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<WhatsAppOutgoingMessageResponse>> FindOutgoingAsync(Guid contact, Guid request, Guid actor, CancellationToken ct) => throw new NotSupportedException();
    }
}
