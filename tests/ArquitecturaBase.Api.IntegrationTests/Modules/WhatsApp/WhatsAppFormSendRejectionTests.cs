using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("Window", 409, WhatsAppChatErrors.WindowClosedCode)]
    [InlineData("Bot", 409, WhatsAppChatErrors.ManualRequiredCode)]
    [InlineData("Revision", 409, WhatsAppChatErrors.RevisionConflictCode)]
    [InlineData("Pending", 409, WhatsAppChatErrors.SendPendingCode)]
    [InlineData("Request", 409, WhatsAppChatErrors.RequestConflictCode)]
    [InlineData("Empty", 400, "Validation.Failed")]
    [InlineData("Length", 400, "Validation.Failed")]
    [InlineData("Invisible", 400, "Validation.Failed")]
    [InlineData("EmployeePermission", 200, "Failed")]
    public async Task Rejected_form_sends_do_not_create_another_session_or_call_the_provider(string reason, int status, string code)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        await using var host = FormsHost(new CapturingFlowProvider(clock), cloud, source, clock);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, versionId, 1, "FORM", snapshot.Name, 1, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new(operationId, definitionId, versionId, authorId,
            ["formtemplates.publish"], "rejection-fixture", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot), Ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        if (reason == "Bot")
            await factory.ExecuteDbContextAsync(async db =>
            {
                (await db.Set<WhatsAppContact>().SingleAsync(contact => contact.Id == contactId, Ct)).ChangeMode(WhatsAppConversationMode.Bot);
                await db.SaveChangesAsync(Ct);
            });
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        var requestId = Guid.NewGuid();
        var request = new JsonObject
        {
            ["requestId"] = requestId.ToString(), ["definitionId"] = definitionId.ToString(), ["versionId"] = versionId.ToString(),
            ["expectedModeRevision"] = reason == "Bot" || reason == "Revision" ? 1 : 0,
            ["expectedRecipientContextRevision"] = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString(),
            ["message"] = "Rejection fixture",
        };
        var existing = reason is "Pending" or "Request" or "EmployeePermission";
        try
        {
            if (existing)
            {
                using var first = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct);
                Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            }
            switch (reason)
            {
                case "Window": clock.Advance(TimeSpan.FromHours(25)); break;
                case "Pending": request["requestId"] = Guid.NewGuid().ToString(); break;
                case "Request": request["message"] = "Different message"; break;
                case "Empty": request["message"] = ""; break;
                case "Length": request["message"] = new string('x', 1025); break;
                case "Invisible": request["message"] = "\u200B"; break;
                case "EmployeePermission":
                    await factory.InTransactionAsync(services => services.GetRequiredService<IUserRepository>().SetRolesAsync(employeeId, [], Ct));
                    await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
                    break;
            }
            using var outcome = reason == "EmployeePermission"
                ? await author.GetAsync($"/api/whatsapp/chats/{contactId}/outgoing/{requestId}", Ct)
                : await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct);
            Assert.Equal((HttpStatusCode)status, outcome.StatusCode);
            var result = await outcome.ReadJsonAsync();
            Assert.Equal(code, result.GetProperty(reason == "EmployeePermission" ? "state" : "code").GetString());
            Assert.Equal(existing ? 1 : 0, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>().CountAsync(session => session.ContactId == contactId, Ct)));
            Assert.Equal(existing ? 1 : 0, await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppOutgoingMessage>().CountAsync(message => message.ContactId == contactId, Ct)));
            Assert.Empty(cloud.Messages);
        }
        finally
        {
            if (existing) await RemoveOutgoingAndSessionAsync(contactId, authorId, requestId);
        }
    }

    private Task RemoveOutgoingAndSessionAsync(Guid contactId, Guid operatorUserId, Guid requestId) =>
        factory.InTransactionAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var outgoing = await db.Set<WhatsAppOutgoingMessage>().SingleOrDefaultAsync(message =>
                message.ContactId == contactId && message.OperatorUserId == operatorUserId && message.RequestId == requestId, Ct);
            if (outgoing is null) return;
            var session = outgoing.FlowSessionId is { } sessionId
                ? await db.Set<WhatsAppFlowSession>().SingleOrDefaultAsync(value => value.Id == sessionId, Ct)
                : null;
            db.Remove(outgoing);
            if (session is not null) db.Remove(session);
        });
}
