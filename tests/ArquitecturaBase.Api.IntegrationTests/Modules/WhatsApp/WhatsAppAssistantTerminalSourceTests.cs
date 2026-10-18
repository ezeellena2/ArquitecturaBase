using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppAssistantTerminalSourceTests(ApiFactory factory)
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_processed_capture_is_not_scanned_again_when_an_equivalent_unmarked_receipt_arrives(bool automatic)
    {
        await using var host=await WhatsAppAssistantScenario.CreateAsync(factory,Ct);
        var contact=await host.ContactAsync(Ct,automatic:automatic);
        var now=host.Clock.GetUtcNow().UtcDateTime;
        var recordId=Guid.NewGuid();
        var original=Capture(contact.ContactId,recordId,2,now);
        original.MarkAssistantProcessed(now);
        var duplicate=Capture(contact.ContactId,recordId,3,now);
        await host.InTransactionAsync(services=>
        {
            services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().AddRange(original,duplicate);
            return Task.FromResult(true);
        },Ct);
        var result=await host.ScopeAsync(async services=>
        {
            var reader=services.GetRequiredService<IWhatsAppAssistantReader>();
            return (Pending:await reader.ListPendingContactsAsync([],100,now,Ct),
                Source:await reader.FindLatestSourceAsync(contact.ContactId,Ct));
        });
        Assert.DoesNotContain(contact.ContactId,result.Pending);
        Assert.NotNull(result.Source);Assert.Equal(original.Id,result.Source.Id);
        Assert.Equal(now,result.Source.AssistantProcessedAtUtc);
        Assert.Null(duplicate.AssistantProcessedAtUtc);

        var next=Capture(contact.ContactId,Guid.NewGuid(),4,now);
        await host.InTransactionAsync(services=>
        {
            services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().Add(next);
            return Task.FromResult(true);
        },Ct);
        var pending=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantReader>().ListPendingContactsAsync([],100,now,Ct));
        Assert.Contains(contact.ContactId,pending);
    }

    private static WhatsAppMessage Capture(Guid contactId,Guid recordId,long sequence,DateTime nowUtc)
    {
        var message=WhatsAppMessage.Inbound(contactId,"wamid.terminal-capture-"+Guid.NewGuid().ToString("N"),WhatsAppMessageKind.FlowReply,null,null,nowUtc,[1]);
        message.Classify(WhatsAppMessagePurpose.Business);message.SetInboundSequence(sequence);
        Assert.True(message.SetFlowReplyCapture(WhatsAppFlowCaptureState.Completed,recordId,"VIS-000024",null));message.MarkProcessed(nowUtc);
        return message;
    }
}
