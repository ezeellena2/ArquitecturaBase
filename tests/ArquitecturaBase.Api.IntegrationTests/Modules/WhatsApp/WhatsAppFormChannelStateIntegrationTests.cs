using System.Security.Cryptography;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Channel_state_batches_all_versions_and_origins_without_exposing_other_definitions()
    {
        using var employee = await OperatorAsync(factory, "formrecords.create");
        var userId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(userId, factory.Clock);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var definitionId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var emptyId = Guid.NewGuid();
        var sessions = new List<WhatsAppFlowSession>();
        try
        {
            sessions.Add(await StateSessionAsync(definitionId, contactId, userId, now.AddHours(3)));
            sessions.Add(await StateSessionAsync(definitionId, contactId, userId, now.AddHours(4), assistant: true));
            sessions.Add(await StateSessionAsync(definitionId, contactId, userId, now.AddHours(1)));
            sessions.Add(await StateSessionAsync(definitionId, contactId, userId, now.AddHours(8), completed: true));
            sessions.Add(await StateSessionAsync(otherId, contactId, userId, now.AddHours(10)));
            using var scope = factory.Services.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>();

            var rows = await source.ListAsync([definitionId, emptyId], now.AddHours(2), Ct);

            var row = Assert.Single(rows, item => item.DefinitionId == definitionId);
            Assert.Equal(now.AddHours(4), row.LastOpenSessionExpiresAtUtc);
            Assert.False(row.HasPendingReplies);
            Assert.DoesNotContain(rows, item => item.DefinitionId == otherId);
            Assert.All(rows.Where(item => item.DefinitionId == emptyId), item =>
            {
                Assert.Null(item.LastOpenSessionExpiresAtUtc);
                Assert.False(item.HasPendingReplies);
            });
            Assert.Empty(await source.ListAsync([], now, Ct));
        }
        finally { await RemoveStateSessionsAsync(sessions, []); }
    }

    [Theory]
    [InlineData("Linked", true)]
    [InlineData("UnlinkedTimely", true)]
    [InlineData("UnlinkedAtCreation", true)]
    [InlineData("UnlinkedBeforeCreation", false)]
    [InlineData("UnlinkedAtExpiry", false)]
    [InlineData("LinkedElsewhere", false)]
    [InlineData("OtherContact", false)]
    [InlineData("Processed", false)]
    [InlineData("NoPayload", false)]
    [InlineData("OtherKind", false)]
    [InlineData("CompletedSession", false)]
    public async Task A_timely_pending_reply_blocks_removal_even_after_its_session_expires(string scenario, bool pending)
    {
        using var employee = await OperatorAsync(factory, "formrecords.create");
        var userId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(userId, factory.Clock);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var definitionId = Guid.NewGuid();
        var sessions = new List<WhatsAppFlowSession>();
        var messages = new List<WhatsAppMessage>();
        try
        {
            var session = await StateSessionAsync(definitionId, contactId, userId, now.AddHours(1),
                completed: scenario == "CompletedSession");
            sessions.Add(session);
            var occurredAtUtc = scenario switch
            {
                "UnlinkedAtCreation" => session.CreatedAtUtc,
                "UnlinkedBeforeCreation" => session.CreatedAtUtc.AddSeconds(-1),
                "UnlinkedAtExpiry" => session.ExpiresAtUtc,
                _ => session.CreatedAtUtc.AddMinutes(30)
            };
            var messageContactId = scenario == "OtherContact" ? await CreateLinkedContactAsync(userId, factory.Clock) : contactId;
            var message = scenario == "OtherKind"
                ? WhatsAppMessage.Inbound(messageContactId, "wamid.state-" + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.Text, "Texto", null, occurredAtUtc)
                : WhatsAppMessage.Inbound(messageContactId, "wamid.state-" + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.FlowReply, null, null, occurredAtUtc, [1]);
            if (scenario == "Linked") message.AttachFlowSession(session.Id);
            if (scenario == "LinkedElsewhere")
            {
                var other = await StateSessionAsync(Guid.NewGuid(), contactId, userId, now.AddHours(1));
                sessions.Add(other);
                message.AttachFlowSession(other.Id);
            }
            if (scenario == "Processed") message.MarkProcessed(now.AddHours(2));
            messages.Add(message);
            await factory.ExecuteDbContextAsync(async db =>
            {
                db.Set<WhatsAppMessage>().Add(message);
                if (scenario == "NoPayload") db.Entry(message).Property(item => item.ProtectedFlowResponse).CurrentValue = null;
                await db.SaveChangesAsync(Ct);
            });
            using var scope = factory.Services.CreateScope();
            var source = scope.ServiceProvider.GetRequiredService<IStructuredFormChannelStateSource>();

            var row = (await source.ListAsync([definitionId], now.AddHours(2), Ct))
                .SingleOrDefault(item => item.DefinitionId == definitionId)
                ?? new StructuredFormChannelStateRow(definitionId, null, false);

            Assert.Null(row.LastOpenSessionExpiresAtUtc);
            Assert.Equal(pending, row.HasPendingReplies);
        }
        finally { await RemoveStateSessionsAsync(sessions, messages); }
    }

    private async Task<WhatsAppFlowSession> StateSessionAsync(Guid definitionId, Guid contactId, Guid userId,
        DateTime expiresAtUtc, bool assistant = false, bool completed = false)
    {
        var versionId = Guid.NewGuid();
        var operation = WhatsAppFlowOperation.Accept(Guid.NewGuid(), definitionId, versionId, userId);
        operation.SealManifest([1]);
        operation.Release(factory.Clock.GetUtcNow().UtcDateTime, completed: true, requiresReview: false);
        var culture = assistant ? "en" : "es";
        var binding = WhatsAppFlowBinding.Create(operation.Id, versionId, culture, "state-" + Guid.NewGuid().ToString("N"), [2]);
        var tokenHash = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var session = assistant
            ? WhatsAppFlowSession.CreateAssistant(contactId, 1, userId, definitionId, versionId, 1, binding.Id, culture,
                "{\"en\":\"State\"}", "STATE", tokenHash, new string('a', 64), expiresAtUtc)
            : WhatsAppFlowSession.Create(contactId, 1, userId, userId, definitionId, versionId, 1, binding.Id, culture,
                "{\"es\":\"Estado\"}", "STATE", tokenHash, new string('a', 64), expiresAtUtc);
        session.SealPayload([3]);
        if (completed) Assert.True(session.CompleteCapture("wamid.completed-" + session.Id.ToString("N"),
            WhatsAppFlowCaptureState.Completed, Guid.NewGuid(), "STATE-000001", null));
        await factory.ExecuteDbContextAsync(async db =>
        {
            db.Add(operation); db.Add(binding); db.Add(session);
            await db.SaveChangesAsync(Ct);
        });
        return session;
    }

    private Task RemoveStateSessionsAsync(List<WhatsAppFlowSession> sessions, List<WhatsAppMessage> messages) =>
        factory.ExecuteDbContextAsync(async db =>
        {
            var messageIds = messages.Select(message => message.Id).ToArray();
            var sessionIds = sessions.Select(session => session.Id).ToArray();
            var bindingIds = sessions.Select(session => session.BindingId).ToArray();
            var bindings = await db.Set<WhatsAppFlowBinding>().Where(binding => bindingIds.Contains(binding.Id)).ToArrayAsync(Ct);
            var operationIds = bindings.Select(binding => binding.OperationId).ToArray();
            db.RemoveRange(await db.Set<WhatsAppMessage>().Where(message => messageIds.Contains(message.Id)).ToArrayAsync(Ct));
            db.RemoveRange(await db.Set<WhatsAppFlowSession>().Where(session => sessionIds.Contains(session.Id)).ToArrayAsync(Ct));
            db.RemoveRange(bindings);
            db.RemoveRange(await db.Set<WhatsAppFlowOperation>().Where(operation => operationIds.Contains(operation.Id)).ToArrayAsync(Ct));
            await db.SaveChangesAsync(Ct);
        });
}
