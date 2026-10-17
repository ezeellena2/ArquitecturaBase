using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppFormActionHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string PrivateBody = "La visita VIS-000001 del cliente ficticio fue aprobada.";
    private DateTime Now => MetaWebhook.TruncatedToSeconds(factory.Clock.GetUtcNow().UtcDateTime);

    [Fact]
    public async Task Chat_actions_keep_system_origin_and_hide_values_after_record_read_is_revoked()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, "formrecords.read"], Ct);
        await scenario.InTransactionAsync(services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            foreach (var kind in new[] { WhatsAppMessageKind.Text, WhatsAppMessageKind.Document })
            {
                var message = WhatsAppMessage.Outbound(recipient.ContactId, MetaWebhook.UniqueWaMessageId(), kind, PrivateBody, Now.AddSeconds((int)kind));
                message.AttributeFormAction(); db.Set<WhatsAppMessage>().Add(message);
            }
            return Task.FromResult(true);
        }, Ct);
        var request = new StructuredRecordActionMessage(Guid.NewGuid(), recipient.UserId, PrivateBody,
            new("private/http-action.pdf", "Visita.pdf", "application/pdf", 80, new string('a', 64)));
        Assert.Equal(StructuredRecordActionState.Pending, (await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(request, Ct))).State);
        using var client = scenario.Host.CreateClient();
        var route = $"/api/whatsapp/chats/{recipient.ContactId}/messages";
        using var thread = await client.SendAsync(HttpMethod.Get, route, userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var permitted = await thread.ReadJsonAsync();
        Assert.True(permitted.GetProperty("canReadFormRecords").GetBoolean());
        Assert.All(permitted.GetProperty("messages").EnumerateArray(), message => AssertAction(message, PrivateBody));
        var pending = Assert.Single(permitted.GetProperty("outgoing").EnumerateArray());
        AssertAction(pending, PrivateBody);
        Assert.Equal("Document", pending.GetProperty("kind").GetString());
        await WhatsAppFormActionContractCapture.WriteAsync(thread, "thread-form-actions", Ct);
        var profileName = await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>()
            .Where(contact => contact.Id == recipient.ContactId).Select(contact => contact.ProfileName).SingleAsync(Ct));
        using var list = await client.SendAsync(HttpMethod.Get, "/api/whatsapp/chats?search=" + Uri.EscapeDataString(profileName!), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.DoesNotContain(PrivateBody, await list.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
        await WhatsAppFormActionContractCapture.WriteAsync(list, "chats-form-actions", Ct);
        await scenario.InTransactionAsync(async services =>
        {
            var role = "restricted-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(role, null, [WhatsAppPermissions.Read], Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(actor, [role], Ct); return true;
        }, Ct);
        using var restricted = await client.SendAsync(HttpMethod.Get, route, userId: actor.ToString());
        Assert.Equal(HttpStatusCode.OK, restricted.StatusCode);
        var body = await restricted.ReadJsonAsync();
        Assert.False(body.GetProperty("canReadFormRecords").GetBoolean());
        Assert.All(body.GetProperty("messages").EnumerateArray(), message => AssertAction(message, null));
        AssertAction(Assert.Single(body.GetProperty("outgoing").EnumerateArray()), null);
        await WhatsAppFormActionContractCapture.WriteAsync(restricted, "thread-form-actions-restricted", Ct);
        using var owned = await client.SendAsync(HttpMethod.Get, $"/api/whatsapp/chats/{recipient.ContactId}/outgoing/{request.RunId}", userId: actor.ToString());
        Assert.Equal(HttpStatusCode.NotFound, owned.StatusCode);
    }

    [Fact]
    public async Task An_uncertain_system_action_does_not_block_manual_attention_or_mode_changes()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct);
        var actor = await scenario.OperatorAsync([WhatsAppPermissions.Read, WhatsAppPermissions.Send, WhatsAppPermissions.Manage], Ct);
        await scenario.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == recipient.ContactId, Ct);
            var outgoing = WhatsAppOutgoingMessage.CreateFormAction(recipient.UserId, contact.Id, Guid.NewGuid(), PrivateBody, Now,
                contactLinkRevision: contact.LinkRevision, recipientWaId: contact.WaId);
            var reservation = outgoing.ReserveFormAction(Now); outgoing.MarkFormActionMessageEffect(reservation, Now); outgoing.MarkUncertain();
            db.Set<WhatsAppOutgoingMessage>().Add(outgoing); return true;
        }, Ct);
        using var client = scenario.Host.CreateClient();
        foreach (var (mode, revision) in new[] { ("Bot", 0), ("Manual", 1) })
        {
            using var changed = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{recipient.ContactId}/mode",
                JsonContent.Create(new { mode, expectedRevision = revision }), userId: actor.ToString());
            Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        }
        using var sent = await client.SendAsync(HttpMethod.Post, $"/api/whatsapp/chats/{recipient.ContactId}/messages",
            JsonContent.Create(new { requestId = Guid.NewGuid(), body = "Respuesta manual" }), userId: actor.ToString());
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode);
    }

    [Fact]
    public async Task A_retained_failure_without_text_does_not_offer_an_impossible_retry()
    {
        await using var scenario = await WhatsAppFormActionScenario.CreateAsync(factory, Ct);
        var recipient = await scenario.RecipientAsync(Now, Ct); var request = new StructuredRecordActionMessage(Guid.NewGuid(), recipient.UserId, PrivateBody);
        await scenario.ScopeAsync(services => Channel(services).EnqueueAsync(request, Ct));
        await scenario.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var outgoing = await db.Set<WhatsAppOutgoingMessage>().SingleAsync(Ct); outgoing.Fail(WhatsAppFormActionErrors.SendRejected.Code);
            await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "WhatsAppOutgoingMessages" SET "Body" = NULL WHERE "Id" = {outgoing.Id}""", Ct); return true;
        }, Ct);
        Assert.False((await scenario.ScopeAsync(services => Channel(services).GetAsync(request.RunId, recipient.UserId, Ct))).CanRetry);
        var retried = await scenario.ScopeAsync(services => Channel(services).RetryAsync(request.RunId, recipient.UserId, 2, Ct));
        Assert.False(retried.CanRetry); Assert.Equal(StructuredRecordActionState.Failed, retried.State);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Human_review_is_durable_and_a_late_provider_acceptance_still_wins_without_another_send(bool wasSent)
    {
        await using var scenario=await WhatsAppFormActionScenario.CreateAsync(factory,Ct);var recipient=await scenario.RecipientAsync(Now,Ct);
        var runId=Guid.NewGuid();var reviewer=Guid.NewGuid();
        var created=await scenario.InTransactionAsync(async services=>
        {
            var db=services.GetRequiredService<ApplicationDbContext>();var contact=await db.Set<WhatsAppContact>().SingleAsync(item=>item.Id==recipient.ContactId,Ct);
            var outgoing=WhatsAppOutgoingMessage.CreateFormAction(recipient.UserId,contact.Id,runId,PrivateBody,Now,contactLinkRevision:contact.LinkRevision,recipientWaId:contact.WaId);
            var reservation=outgoing.ReserveFormAction(Now);outgoing.MarkFormActionMessageEffect(reservation,Now);outgoing.MarkUncertain();db.Set<WhatsAppOutgoingMessage>().Add(outgoing);
            return(outgoing.Id,Reservation:reservation);
        },Ct);
        var evidence=new StructuredRecordActionReview(1,wasSent,reviewer,Now.AddMinutes(1));
        var response=await scenario.ScopeAsync(services=>Channel(services).ReviewAsync(runId,recipient.UserId,evidence,Ct));
        Assert.Equal(wasSent?StructuredRecordActionState.Accepted:StructuredRecordActionState.Failed,response.State);Assert.Equal(!wasSent,response.CanRetry);
        await scenario.ScopeAsync(async services=>
        {
            var outgoing=await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking().SingleAsync(Ct);
            Assert.Equal(wasSent,outgoing.DeliveryReviewWasSent);Assert.Equal(reviewer,outgoing.DeliveryReviewedByUserId);Assert.Equal(evidence.ReviewedAtUtc,outgoing.DeliveryReviewedAtUtc);Assert.Null(outgoing.WaMessageId);
        });
        var late=await scenario.ScopeAsync(services=>services.GetRequiredService<IWhatsAppChatDispatchService>().CompleteAsync(recipient.ContactId,created.Id,
            "wamid.late-"+Guid.NewGuid().ToString("N"),false,Ct,created.Reservation));Assert.True(late.IsSuccess);
        var accepted=await scenario.ScopeAsync(services=>Channel(services).GetAsync(runId,recipient.UserId,Ct));Assert.Equal(StructuredRecordActionState.Accepted,accepted.State);Assert.False(accepted.CanRetry);
        Assert.Empty(scenario.Cloud.Messages);Assert.Equal(0,scenario.Media.Calls);
        await scenario.ScopeAsync(async services=>Assert.Equal(1,await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppMessage>().CountAsync(Ct)));
    }
    private static IStructuredRecordActionChannel Channel(IServiceProvider services) => services.GetServices<IStructuredRecordActionChannel>().Single(channel => channel.ChannelId == "phone");
    private static void AssertAction(JsonElement item, string? expectedBody)
    {
        Assert.Equal("FormAction", item.GetProperty("origin").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("operatorUserId").ValueKind);
        Assert.Equal(expectedBody, item.GetProperty("body").GetString());
        Assert.DoesNotContain("private/", item.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("storageKey", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediaId", item.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
