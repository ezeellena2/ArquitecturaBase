using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

// O49: a reply of the person that Meta could not deliver comes only as a failed status with 131052 of a message that is not ours, with the
// person's number or BSUID. The open send of the person whose form asks for a file gets its form again, once, with the message that says
// so, from the same sender and in the same send: the retry of the same webhook, or another loss while that resend waits, sends nothing
// more, and a loss after it went out sends the form again. A form without a file, one sent after the loss, or another failure, sends
// nothing; the receipt stays.
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppLostReplyIntegrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reply_meta_could_not_deliver_sends_the_open_form_that_asks_for_a_file_again_once(bool byBsuid)
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, fileQuestion: true);
        var contact = await SentAsync(host);
        var lost = await LostAsync(host, contact.ContactId, 131052, byBsuid);

        await PostAsync(host, lost);
        await PostAsync(host, lost);
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        await worker.ProcessNextAsync(Ct);

        Assert.Equal(2, host.Cloud.Messages.Count);
        var resend = Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages[1]);
        Assert.Equal("No me llegó tu respuesta al formulario «Registrar visita». Completalo de nuevo, por favor.", resend.Body);
        var session = await SessionAsync(host, contact.ContactId);
        var outgoing = await OutgoingAsync(host, contact.ContactId);
        Assert.Equal([(WhatsAppOutgoingMessageKind.Flow, false, WhatsAppOutgoingMessageState.Accepted),
                (WhatsAppOutgoingMessageKind.Flow, true, WhatsAppOutgoingMessageState.Accepted)],
            outgoing.Select(item => (item.Kind, item.IsResend, item.State)));
        Assert.All(outgoing, item => Assert.Equal((WhatsAppMessageOrigin.Assistant, (Guid?)session.Id), (item.Origin, item.FlowSessionId)));
        Assert.Equal(WhatsAppFlowCaptureState.Pending, session.CaptureState);
    }

    [Fact]
    public async Task Another_lost_reply_sends_the_form_again_only_once_its_resend_went_out()
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, fileQuestion: true);
        var contact = await SentAsync(host);
        var worker = host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();

        await PostAsync(host, await LostAsync(host, contact.ContactId, 131052));
        await PostAsync(host, await LostAsync(host, contact.ContactId, 131052));
        Assert.Equal(2, (await OutgoingAsync(host, contact.ContactId)).Count);
        await worker.ProcessNextAsync(Ct);
        await PostAsync(host, await LostAsync(host, contact.ContactId, 131052));
        await worker.ProcessNextAsync(Ct);

        Assert.Equal(3, host.Cloud.Messages.Count);
        Assert.Equal([(false, WhatsAppOutgoingMessageState.Accepted), (true, WhatsAppOutgoingMessageState.Accepted), (true, WhatsAppOutgoingMessageState.Accepted)],
            (await OutgoingAsync(host, contact.ContactId)).Select(item => (item.IsResend, item.State)));
    }

    [Theory]
    [InlineData(false, 131052, false)]
    [InlineData(true, 131026, false)]
    [InlineData(true, 131052, true)]
    public async Task A_form_without_a_file_or_sent_after_the_loss_or_another_failure_sends_nothing(bool fileQuestion, int errorCode, bool beforeTheSend)
    {
        await using var host = await WhatsAppAssistantScenario.CreateAsync(factory, Ct, fileQuestion: fileQuestion);
        var contact = await SentAsync(host);

        await PostAsync(host, await LostAsync(host, contact.ContactId, errorCode, beforeTheSend: beforeTheSend));

        Assert.Single(await OutgoingAsync(host, contact.ContactId));
        Assert.Equal(1, await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppDeliveryReceipt>()
            .CountAsync(receipt => receipt.Status == WhatsAppMessageStatus.Failed && receipt.ErrorCode == errorCode, Ct)));
    }

    // The assistant accepts its own proposal: its Flow goes out first, as the assistant's.
    private static async Task<WhatsAppAssistantContact> SentAsync(WhatsAppAssistantScenario host)
    {
        var contact = await host.ContactAsync(Ct, automatic: true);
        var work = await host.ScopeAsync(async services => (await services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId, Ct)).Value);
        await host.CompleteOutcomeAsync(contact.ContactId, host.Outcome(contact, Assert.IsType<WhatsAppAssistantWorkResponse>(work), "ready"), Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(host.Cloud.Messages));
        return contact;
    }

    // Meta's status of the reply it could not deliver, some time after the Flow arrived (or, for a form that did not exist yet, a minute before
    // it was sent): an id of its own, which is none of ours, and the number of the person or only its BSUID, next to a number nobody has.
    private static async Task<byte[]> LostAsync(WhatsAppAssistantScenario host, Guid contactId, int errorCode, bool byBsuid = false,
        bool beforeTheSend = false)
    {
        if (!beforeTheSend) host.Clock.Advance(TimeSpan.FromSeconds(30));
        var person = await host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().AsNoTracking()
            .Where(item => item.Id == contactId).Select(item => new { item.WaId, item.UserIdentifier }).SingleAsync(Ct));
        var atUtc = host.Clock.GetUtcNow().UtcDateTime - (beforeTheSend ? TimeSpan.FromMinutes(1) : TimeSpan.Zero);
        return MetaWebhook.Build(statuses:
            [MetaWebhook.Status(MetaWebhook.UniqueWaMessageId(), "failed", atUtc, byBsuid ? null : person.WaId, errorCode, byBsuid ? person.UserIdentifier : null)]);
    }

    private static async Task PostAsync(WhatsAppAssistantScenario host, byte[] body)
    {
        using var client = host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/whatsapp") { Content = new ByteArrayContent(body) };
        request.Headers.Add("X-Hub-Signature-256", MetaWebhook.Sign(body));
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<WhatsAppFlowSession> SessionAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .SingleAsync(item => item.ContactId == contactId, Ct));

    private static Task<List<WhatsAppOutgoingMessage>> OutgoingAsync(WhatsAppAssistantScenario host, Guid contactId) =>
        host.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item => item.ContactId == contactId).OrderBy(item => item.RequestedAtUtc).ThenBy(item => item.Id).ToListAsync(Ct));
}
