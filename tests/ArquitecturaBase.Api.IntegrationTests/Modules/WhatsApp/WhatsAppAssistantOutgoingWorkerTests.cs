using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppAssistantOutgoingWorkerTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Fact]
    public async Task The_assistant_marks_the_effect_after_the_lease_and_before_the_provider()
    {
        await using var host=new WhatsAppAssistantWorkerFixture();
        await host.Worker.ProcessNextAsync(Ct);
        Assert.Equal(new[]{"reserve","lease","mark","post","complete"},host.Events);
    }
    // H9: an assistant send that Meta refuses keeps why, one stable code per kind of refusal, and the count of what it ended in has it.
    [Fact]
    public async Task A_refused_assistant_send_keeps_why_it_did_not_go_out()
    {
        using var outcomes=new MetricCollector<long>(null,WhatsAppMetrics.MeterName,"whatsapp.outgoing.outcomes");
        await using var host=new WhatsAppAssistantWorkerFixture{NextResult=WhatsAppSendResult.Failed(WhatsAppSendFailure.Undeliverable,131026)};
        await host.Worker.ProcessNextAsync(Ct);
        Assert.Equal((false,"WhatsApp.Send.Undeliverable"),(host.CompletedUncertain,host.CompletedFailureCode));
        Assert.Contains(outcomes.GetMeasurementSnapshot(),measurement=>measurement.Tags["outcome"] is "failed"&&measurement.Tags["origin"] is "Assistant");
    }
    // H9: an assistant send that did not go out after its retries keeps that it could not reach WhatsApp.
    [Fact]
    public async Task An_assistant_send_that_spent_its_retries_keeps_that_whatsapp_was_unavailable()
    {
        await using var host=new WhatsAppAssistantWorkerFixture{NextResult=WhatsAppSendResult.Failed(WhatsAppSendFailure.Transient)};
        host.Operation=host.Operation with{DispatchRetries=WhatsAppDispatchPacing.MaxRetries};
        await host.Worker.ProcessNextAsync(Ct);
        Assert.Equal("WhatsApp.Send.Unavailable",host.CompletedFailureCode);
        Assert.DoesNotContain("retry",host.Events);
    }
    [Fact]
    public async Task A_rejected_revalidation_never_invokes_the_provider()
    {
        await using var host=new WhatsAppAssistantWorkerFixture{AllowPost=false};
        await host.Worker.ProcessNextAsync(Ct);
        Assert.Null(host.SentMessage);Assert.DoesNotContain("post",host.Events);
    }
    [Fact]
    public async Task A_known_message_id_is_confirmed_with_a_live_token_after_shutdown()
    {
        await using var host=new WhatsAppAssistantWorkerFixture{CancelAfterProvider=true};
        await host.Worker.ProcessNextAsync(host.Shutdown.Token);
        Assert.Equal("wamid.assistant-known",host.CompletedId);Assert.True(host.CompletedWithLiveToken);
    }
    [Fact]
    public async Task Clarification_options_use_real_reply_buttons_with_captured_ids()
    {
        await using var host=new WhatsAppAssistantWorkerFixture();
        host.Operation=host.Operation with{Kind=WhatsAppOutgoingMessageKind.QuickReplies,QuickReplies=[new("opaque-choice-one","Visita",3),new("opaque-choice-two","Mantenimiento",4)]};
        await host.Worker.ProcessNextAsync(Ct);
        var sent=Assert.IsType<WhatsAppReplyButtonsMessage>(host.SentMessage);
        Assert.Equal(new[]{"opaque-choice-one","opaque-choice-two"},sent.Buttons.Select(button=>button.Id));
        Assert.Equal(new[]{"Visita","Mantenimiento"},sent.Buttons.Select(button=>button.Title));
    }
    [Fact]
    public async Task Flow_preparation_revalidates_after_waiting_for_the_recipient_lease()
    {
        await using var host=new WhatsAppAssistantWorkerFixture();
        host.Operation=host.Operation with{Kind=WhatsAppOutgoingMessageKind.Flow,FlowSessionId=Guid.NewGuid()};
        await host.Worker.ProcessNextAsync(Ct);
        Assert.Equal(new[]{"reserve","lease","prepare","mark","post","complete"},host.Events);
    }
    [Fact]
    public async Task An_uncertain_result_is_recorded_and_the_same_work_is_never_repeated()
    {
        await using var host=new WhatsAppAssistantWorkerFixture{Uncertain=true};
        await host.Worker.ProcessNextAsync(Ct);await host.Worker.ProcessNextAsync(Ct);
        Assert.True(host.CompletedUncertain);Assert.Null(host.CompletedId);Assert.Single(host.Events,value=>value=="post");
    }
}
