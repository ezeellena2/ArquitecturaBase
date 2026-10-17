using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppChatQueryActionPrivacyTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    private const string PrivateBody = "Resultado confidencial de la visita";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Action_values_are_never_in_the_chat_list_preview()
    {
        var host = new Host();
        var result = await host.Service.ListAsync(new(), Guid.NewGuid(), Ct);
        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(PrivateBody, Assert.Single(result.Value.Page.Items).Preview, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Losing_record_read_hides_sent_and_pending_action_values_after_reload()
    {
        var host = new Host(); var actor = Guid.NewGuid();
        var permitted = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), actor, Ct);
        Assert.Equal(PrivateBody, Assert.Single(permitted.Value.Messages).Body);
        Assert.Equal(PrivateBody, Assert.Single(permitted.Value.Outgoing).Body);
        host.Source.Allowed = false;
        var restricted = await host.Service.GetThreadAsync(new(host.Reader.Contact.Id), actor, Ct);
        Assert.False(restricted.Value.CanReadFormRecords);
        Assert.Null(Assert.Single(restricted.Value.Messages).Body);
        Assert.Null(Assert.Single(restricted.Value.Outgoing).Body);
        Assert.Equal(WhatsAppMessageOrigin.FormAction, Assert.Single(restricted.Value.Messages).Origin);
        Assert.Equal(WhatsAppMessageOrigin.FormAction, Assert.Single(restricted.Value.Outgoing).Origin);
    }

    private sealed class Host
    {
        public Reader Reader { get; } = new(); public Source Source { get; } = new();
        public AssistantOperations Operations { get; } = new();
        public WhatsAppChatQueryService Service { get; }
        public Microsoft.Extensions.Time.Testing.FakeTimeProvider Clock { get; } = new(new DateTimeOffset(Now));
        public Host() => Service = new(Reader, new Forms(), Source, new FakePhoneNumberParser(), Operations, new(new(90),Clock), NullLogger<WhatsAppChatQueryService>.Instance);
    }
    private sealed class Reader : IWhatsAppChatReader
    {
        public bool Missing { get; set; }
        public WhatsAppMessageRow? Audio { get; set; }
        public bool AudioPreview { get; set; }
        public WhatsAppChatRow Contact { get; } = new() { Id = Guid.NewGuid(), ProfileName = "Ana", PhoneNumber = "5493413654813", LastInboundAtUtc = Now, Preview = PrivateBody, PreviewIsFormAction = true };
        public Task<PagedResult<WhatsAppChatRow>> ListChatsAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken ct) => Task.FromResult(new PagedResult<WhatsAppChatRow>([AudioPreview ? Contact with {PreviewIsFormAction=false,PreviewIsAudio=true}:Contact], 1, 25, 1));
        public Task<WhatsAppChatCounts> CountChatsAsync(ListWhatsAppChatsRequest request, Guid actor, CancellationToken ct) => Task.FromResult(new WhatsAppChatCounts(1, 0));
        public Task<WhatsAppThreadRow?> FindThreadAsync(ListWhatsAppMessagesRequest request, Guid actor, CancellationToken ct) => Task.FromResult<WhatsAppThreadRow?>(Missing ? null : new(Contact,
            [Audio ?? new(Guid.NewGuid(), WhatsAppMessageDirection.Outbound, WhatsAppMessageKind.Text, PrivateBody, Now, 0, null, null, null, Origin: WhatsAppMessageOrigin.FormAction)],
            [new(Guid.NewGuid(), Guid.NewGuid(), null, PrivateBody, Now, WhatsAppOutgoingMessageState.Pending, null, Origin: WhatsAppMessageOrigin.FormAction)], null, null));
        public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contact, Guid request, Guid actor, CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessageRow?>(null);
    }
    private sealed class Source : IStructuredRecordSource
    {
        public bool Allowed { get; set; } = true;
        public Task<StructuredRecordReadResult> ReadAsync(Guid actor, string culture, IReadOnlyCollection<Guid> receipts, CancellationToken ct) => Task.FromResult(new StructuredRecordReadResult(Allowed, new Dictionary<Guid, StructuredRecordReadReply>()));
    }
    private sealed class Forms : IWhatsAppFormService
    {
        public Task<WhatsAppFormContactResponse?> FindContactAsync(Guid contact, CancellationToken ct) => Task.FromResult<WhatsAppFormContactResponse?>(new(null, null));
        public Task<Result<WhatsAppFormPreparationResponse>> PrepareAsync(Guid contact, Guid definition, Guid version, CancellationToken ct) => throw new NotSupportedException();
        public Task<Result<WhatsAppFormsResponse>> ListAsync(ListWhatsAppFormsRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class AssistantOperations : IAssistantOperations
    {
        public string State { get; set; } = "available";
        public int AvailabilityReads { get; private set; }
        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); AvailabilityReads++; return Task.FromResult(new AssistantAvailabilityResponse(State)); }
        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("A chat GET must never submit an assistant operation.");
        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken ct) =>
            throw new InvalidOperationException("Availability must not recover private assistant content.");
    }

}
