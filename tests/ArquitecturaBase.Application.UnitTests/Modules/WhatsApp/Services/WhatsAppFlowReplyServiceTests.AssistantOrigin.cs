using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed partial class WhatsAppFlowReplyServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_live_assisted_reply_forwards_the_sealed_origin_and_keeps_the_recipient_as_capture_actor(bool automatic)
    {
        var host = new Fixture(assisted: true, automatic: automatic);
        var reply = host.AddReply("wamid.assisted", host.ValidAnswers("changed"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState);
        var request = Assert.IsType<StructuredRecordCaptureRequest>(host.Sink.Request);
        Assert.Same(host.SessionProtector.Content!.Origin, request.Origin);
        Assert.Equal(host.Session.UserId, request.ActorUserId);
        Assert.Equal(automatic ? StructuredRecordSenderKind.Assistant : StructuredRecordSenderKind.Manual, request.Origin!.SenderKind);
        Assert.Equal(host.Session.OperatorUserId, request.Origin.OperatorUserId);
    }

    [Fact]
    public async Task A_legacy_manual_reply_remains_without_an_inferred_assistant_origin()
    {
        var host = new Fixture(); host.AddReply("wamid.legacy-origin", host.ValidAnswers("legacy"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(host.Sink.Request); Assert.Null(host.Sink.Request.Origin); Assert.Equal(0, host.SessionProtector.AssistantReadCount);
    }

    [Theory]
    [InlineData("format")]
    [InlineData("operator")]
    [InlineData("origin")]
    [InlineData("row-operator")]
    [InlineData("row-origin")]
    public async Task A_crossed_reply_header_is_not_used_to_create_a_record(string mismatch)
    {
        var host = new Fixture(assisted: true); var payload = host.SessionProtector.Payload;
        host.SessionProtector.Payload = mismatch switch
        {
            "format" => payload with { Format = 2 },
            "operator" => payload with { OperatorUserId = Guid.NewGuid() },
            "origin" => payload with { Origin = WhatsAppMessageOrigin.Assistant },
            _ => payload
        };
        if (mismatch == "row-operator") host.Sessions.Row = host.Sessions.Row with { OperatorUserId = Guid.NewGuid() };
        if (mismatch == "row-origin") host.Sessions.Row = host.Sessions.Row with { Origin = WhatsAppMessageOrigin.Assistant };
        var reply = host.AddReply("wamid.crossed-header", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
    }

    [Theory]
    [InlineData("sender")]
    [InlineData("operator")]
    [InlineData("purpose")]
    [InlineData("format")]
    public async Task A_crossed_private_origin_is_rejected_before_capture(string mismatch)
    {
        var host = new Fixture(assisted: true); var content = host.SessionProtector.Content!;
        host.SessionProtector.Content = mismatch switch
        {
            "sender" => content with { Origin = content.Origin with { SenderKind = StructuredRecordSenderKind.Assistant } },
            "operator" => content with { Origin = content.Origin with { OperatorUserId = Guid.NewGuid() } },
            "format" => content with { Format = 2 },
            _ => content
        };
        host.SessionProtector.FailAssistantRead = mismatch == "purpose";
        var reply = host.AddReply("wamid.crossed-private", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Expired_or_purged_private_content_is_never_read_or_used_for_a_new_capture(bool purged)
    {
        var host = new Fixture(assisted: true); var reply = host.AddReply("wamid.expired-private", host.ValidAnswers("answer"));
        host.Clock.Advance(TimeSpan.FromDays(90));
        if (purged) Assert.True(host.Session.ClearAssistantContent(host.Clock.GetUtcNow().UtcDateTime));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
        Assert.Equal(0, host.Sink.ReceiptProbeCount); Assert.Equal(0, host.SessionProtector.AssistantReadCount);
    }

    [Fact]
    public async Task A_new_message_after_private_purge_does_not_relabel_the_original_completed_session()
    {
        var host = new Fixture(assisted: true); var first = host.AddReply("wamid.before-purge", host.ValidAnswers("original"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        var originalId = host.Session.CompletedRecordId; var originalNumber = host.Session.CompletedNumber;
        host.Sessions.Row = host.Sessions.Row with { CaptureState = host.Session.CaptureState };
        host.Clock.Advance(TimeSpan.FromDays(90)); Assert.True(host.Session.ClearAssistantContent(host.Clock.GetUtcNow().UtcDateTime));
        var later = host.AddReply("wamid.after-purge", host.ValidAnswers("another answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, first.FlowCaptureState); Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, later.FlowCaptureState);
        Assert.Null(later.FlowSessionId); Assert.Equal(1, host.Sink.CaptureCount); Assert.Equal(0, host.Sink.ReceiptProbeCount);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, host.Session.CaptureState); Assert.Equal(originalId, host.Session.CompletedRecordId); Assert.Equal(originalNumber, host.Session.CompletedNumber);
    }

    [Fact]
    public async Task A_processed_message_keeps_its_original_receipt_when_private_content_expires()
    {
        var host = new Fixture(assisted: true); var reply = host.AddReply("wamid.same-after-expiry", host.ValidAnswers("original"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken); var id = reply.FlowRecordId;
        host.Clock.Advance(TimeSpan.FromDays(90)); Assert.True(host.Session.ClearAssistantContent(host.Clock.GetUtcNow().UtcDateTime));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, host.Sink.CaptureCount); Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState); Assert.Equal(id, reply.FlowRecordId);
    }

    [Fact]
    public async Task A_valid_private_origin_is_preserved_when_recovering_an_exact_existing_receipt()
    {
        var host = new Fixture(assisted: true); host.Sessions.IsCurrentActiveRecipient = false;
        host.Sink.ExistingReceipt = new("Completed", Guid.NewGuid(), 24, null);
        var reply = host.AddReply("wamid.origin-receipt", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Completed, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
        Assert.Equal(1, host.Sink.ReceiptProbeCount); Assert.Same(host.SessionProtector.Content!.Origin, host.Sink.ReceiptRequest!.Origin);
    }

    [Fact]
    public async Task An_assistant_session_without_its_private_origin_is_not_treated_as_a_legacy_manual_session()
    {
        var host = new Fixture(automatic: true); var reply = host.AddReply("wamid.missing-origin", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_projection_from_another_culture_is_not_a_valid_reply_mapping()
    {
        var host = new Fixture(); host.ProjectionProtector.Projection = host.Projection with { Culture = "en" };
        var reply = host.AddReply("wamid.crossed-projection", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
    }

    [Fact]
    public async Task A_missing_operator_does_not_turn_a_conversation_header_into_an_automatic_session()
    {
        var host = new Fixture(); host.Sessions.Row = host.Sessions.Row with { OperatorUserId = null };
        host.SessionProtector.Payload = host.SessionProtector.Payload with { OperatorUserId = null };
        var reply = host.AddReply("wamid.null-operator", host.ValidAnswers("answer"));
        await host.Service.ProcessPendingAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WhatsAppFlowCaptureState.Unrecognized, reply.FlowCaptureState); Assert.Equal(0, host.Sink.CaptureCount);
    }
}
