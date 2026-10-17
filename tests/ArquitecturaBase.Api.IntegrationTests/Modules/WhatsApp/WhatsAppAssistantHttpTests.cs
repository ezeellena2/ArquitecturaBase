using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed partial class WhatsAppAssistantHttpTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Route(Guid contactId) => $"/api/whatsapp/chats/{contactId}/assistant-suggestions";

    [Fact]
    public async Task Manual_http_producer_preserves_all_six_states_and_private_explanation_in_the_receipt_and_thread()
    {
        await using var scenario = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        using var first = scenario.Client(scenario.FirstOperator);
        using var restricted = scenario.Client(scenario.RestrictedOperator);
        var row = await scenario.ContactAsync(Ct); var requestId = Guid.NewGuid(); var route = Route(row.ContactId);
        using var pending = await RequestAsync(first, row, requestId);
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode); AssertState(await pending.ReadJsonAsync(), "Pending");
        await WhatsAppAssistantContractCapture.WriteAsync(pending, "suggestion-pending", Ct);
        await scenario.CompleteAsync(row, requestId, "ready", Ct);
        using var ready = await first.GetAsync(route + $"/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode); var prepared = await ready.ReadJsonAsync(); AssertState(prepared, "Ready");
        Assert.Equal(scenario.Forms.Form.DefinitionId, prepared.GetProperty("proposal").GetProperty("definitionId").GetGuid());
        Assert.Equal("Sucursal Norte", Assert.Single(prepared.GetProperty("proposal").GetProperty("fields").EnumerateArray()).GetProperty("displayValue").GetString());
        await WhatsAppAssistantContractCapture.WriteAsync(ready, "suggestion-ready", Ct);
        using var send = await first.PostAsJsonAsync(route + $"/{requestId}/send", new { requestId = Guid.NewGuid(), includedFieldIds = new[] { scenario.Forms.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, send.StatusCode); var receipt = await send.ReadJsonAsync();
        Assert.Equal(scenario.FirstOperator, receipt.GetProperty("operatorUserId").GetGuid()); AssertExplanation(receipt);
        await WhatsAppAssistantContractCapture.WriteAsync(send, "send-receipt", Ct);
        using var sent = await first.GetAsync(route + $"/{requestId}", Ct);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode); var sentJson = await sent.ReadJsonAsync(); AssertState(sentJson, "Sent");
        Assert.Equal(receipt.GetProperty("id").GetGuid(), sentJson.GetProperty("outgoing").GetProperty("id").GetGuid());
        await WhatsAppAssistantContractCapture.WriteAsync(sent, "suggestion-sent", Ct);
        var threadRoute = $"/api/whatsapp/chats/{row.ContactId}/messages";
        using var latest = await first.GetAsync(threadRoute, Ct); Assert.Equal(HttpStatusCode.OK, latest.StatusCode);
        AssertExplanation(Assert.Single((await latest.ReadJsonAsync()).GetProperty("outgoing").EnumerateArray()));
        using var privateLatest = await restricted.GetAsync(threadRoute, Ct); Assert.Equal(HttpStatusCode.OK, privateLatest.StatusCode);
        Assert.Equal(JsonValueKind.Null, Assert.Single((await privateLatest.ReadJsonAsync()).GetProperty("outgoing").EnumerateArray()).GetProperty("assistant").ValueKind);
        await scenario.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var delivered = Assert.IsType<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFlowMessageResponse>(Assert.Single(scenario.Cloud.Messages));
        Assert.Contains(delivered.InitialValues.SelectMany(item => item.Value), item => item.Value == "Sucursal Norte");
        using var history = await first.GetAsync(threadRoute, Ct); Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var historyJson = await history.ReadJsonAsync(); Assert.True(historyJson.GetProperty("canReadFormRecords").GetBoolean());
        AssertExplanation(Assert.Single(historyJson.GetProperty("messages").EnumerateArray(), item => item.GetProperty("direction").GetString() == "Outbound"));
        await WhatsAppAssistantContractCapture.WriteAsync(history, "thread", Ct);
        using var privateHistory = await restricted.GetAsync(threadRoute, Ct); Assert.Equal(HttpStatusCode.OK, privateHistory.StatusCode);
        var privateJson = await privateHistory.ReadJsonAsync(); Assert.False(privateJson.GetProperty("canReadFormRecords").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Assert.Single(privateJson.GetProperty("messages").EnumerateArray(), item => item.GetProperty("direction").GetString() == "Outbound").GetProperty("assistant").ValueKind);
        await WhatsAppAssistantContractCapture.WriteAsync(privateHistory, "thread-restricted", Ct);

        foreach (var (outcome, expected) in new[] { ("no-reply", "NoReply"), ("blocked", "Blocked"), ("ready", "Discarded") })
        {
            var other = await scenario.ContactAsync(Ct); var id = Guid.NewGuid();
            using var started = await RequestAsync(first, other, id); Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
            await scenario.CompleteAsync(other, id, outcome, Ct);
            using var response = expected == "Discarded"
                ? await first.PostAsync(Route(other.ContactId) + $"/{id}/discard", null, Ct)
                : await first.GetAsync(Route(other.ContactId) + $"/{id}", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); AssertState(await response.ReadJsonAsync(), expected);
            await WhatsAppAssistantContractCapture.WriteAsync(response, "suggestion-" + (expected == "Discarded" ? "discarded" : outcome), Ct);
        }
    }

    [Fact]
    public async Task Two_operators_replay_the_source_and_send_without_creating_or_reattributing_an_outgoing()
    {
        await using var scenario = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        using var first = scenario.Client(scenario.FirstOperator); using var second = scenario.Client(scenario.SecondOperator);
        var row = await scenario.ContactAsync(Ct); var requestId = Guid.NewGuid();
        using var initial = await RequestAsync(first, row, requestId); Assert.Equal(HttpStatusCode.Accepted, initial.StatusCode);
        using var sameSource = await RequestAsync(second, row, Guid.NewGuid()); Assert.Equal(HttpStatusCode.Accepted, sameSource.StatusCode);
        Assert.Equal(requestId, (await sameSource.ReadJsonAsync()).GetProperty("requestId").GetGuid());
        await scenario.CompleteAsync(row, requestId, "ready", Ct);
        var url = Route(row.ContactId) + $"/{requestId}/send";
        using var sent = await first.PostAsJsonAsync(url, new { requestId = Guid.NewGuid(), includedFieldIds = new[] { scenario.Forms.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, sent.StatusCode); var original = await sent.ReadJsonAsync();
        using var replay = await second.PostAsJsonAsync(url, new { requestId = Guid.NewGuid(), includedFieldIds = Array.Empty<Guid>() }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode); var repeated = await replay.ReadJsonAsync();
        Assert.Equal(original.GetProperty("id").GetGuid(), repeated.GetProperty("id").GetGuid()); Assert.Equal(scenario.FirstOperator, repeated.GetProperty("operatorUserId").GetGuid());
        var counts = await scenario.ScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            return (Requests: await db.Set<WhatsAppAssistantRequest>().CountAsync(item => item.ContactId == row.ContactId, Ct),
                Outgoing: await db.Set<WhatsAppOutgoingMessage>().CountAsync(item => item.ContactId == row.ContactId, Ct),
                Sessions: await db.Set<WhatsAppFlowSession>().CountAsync(item => item.ContactId == row.ContactId, Ct));
        });
        Assert.Equal((1, 1, 1), counts);
        Assert.All(scenario.Operations.ReadContexts, context => Assert.Equal(scenario.FirstOperator, context.ActorUserId));
    }

    [Fact]
    public async Task Manual_routes_require_both_permissions_and_refuse_stale_context_or_a_withdrawn_form()
    {
        await using var scenario = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        using var anonymous = scenario.Client(); using var reader = scenario.Client(scenario.ReadOnlyOperator); using var actor = scenario.Client(scenario.FirstOperator);
        var row = await scenario.ContactAsync(Ct); var id = Guid.NewGuid(); var route = Route(row.ContactId);
        using var unauthorized = await RequestAsync(anonymous, row, id); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var forbidden = await RequestAsync(reader, row, id); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var stale = await actor.PostAsJsonAsync(route, new { requestId = id, sourceMessageId = row.SourceMessageId, expectedModeRevision = 1 }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var started = await RequestAsync(actor, row, id); Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        using var anonymousGet = await anonymous.GetAsync(route + $"/{id}", Ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousGet.StatusCode);
        using var anonymousDiscard = await anonymous.PostAsync(route + $"/{id}/discard", null, Ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousDiscard.StatusCode);
        using var anonymousSend = await anonymous.PostAsJsonAsync(route + $"/{id}/send", new { requestId = Guid.NewGuid(), includedFieldIds = Array.Empty<Guid>() }, Ct); Assert.Equal(HttpStatusCode.Unauthorized, anonymousSend.StatusCode);
        using var pendingSend = await actor.PostAsJsonAsync(route + $"/{id}/send", new { requestId = Guid.NewGuid(), includedFieldIds = Array.Empty<Guid>() }, Ct); Assert.Equal(HttpStatusCode.Conflict, pendingSend.StatusCode);
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            using var deniedGetOrDiscard = method == HttpMethod.Get ? await reader.GetAsync(route + $"/{id}", Ct) : await reader.PostAsync(route + $"/{id}/discard", null, Ct);
            Assert.Equal(HttpStatusCode.Forbidden, deniedGetOrDiscard.StatusCode);
        }
        using var deniedSend = await reader.PostAsJsonAsync(route + $"/{id}/send", new { requestId = Guid.NewGuid(), includedFieldIds = Array.Empty<Guid>() }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedSend.StatusCode);
        using var crossContact = await actor.GetAsync(Route(Guid.NewGuid()) + $"/{id}", Ct); Assert.Equal(HttpStatusCode.NotFound, crossContact.StatusCode);
        await scenario.CompleteAsync(row, id, "ready", Ct); scenario.Forms.IsPublished = false;
        using var withdrawn = await actor.GetAsync(route + $"/{id}", Ct); AssertState(await withdrawn.ReadJsonAsync(), "Blocked");
        using var cannotSend = await actor.PostAsJsonAsync(route + $"/{id}/send", new { requestId = Guid.NewGuid(), includedFieldIds = new[] { scenario.Forms.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.Conflict, cannotSend.StatusCode);
        scenario.Forms.IsPublished = true; await scenario.AddNewerSourceAsync(row, Ct);
        using var older = await actor.GetAsync(route + $"/{id}", Ct); Assert.Equal(HttpStatusCode.Conflict, older.StatusCode);
        Assert.Equal(0, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().CountAsync(item => item.ContactId == row.ContactId, Ct)));
    }

    [Fact]
    public async Task Revoking_the_original_operator_prevents_another_operator_from_sending_the_proposal()
    {
        await using var scenario = await WhatsAppAssistantScenario.CreateAsync(factory, Ct);
        using var first = scenario.Client(scenario.FirstOperator); using var second = scenario.Client(scenario.SecondOperator);
        var row = await scenario.ContactAsync(Ct); var id = Guid.NewGuid();
        using var started = await RequestAsync(first, row, id); Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await scenario.CompleteAsync(row, id, "ready", Ct);
        await scenario.InTransactionAsync(async services =>
        { await services.GetRequiredService<IUserRepository>().SetRolesAsync(scenario.FirstOperator, [], Ct); return true; }, Ct);
        using var denied = await second.PostAsJsonAsync(Route(row.ContactId) + $"/{id}/send", new { requestId = Guid.NewGuid(), includedFieldIds = new[] { scenario.Forms.FieldId } }, Ct);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, await scenario.ScopeAsync(services => services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().CountAsync(item => item.ContactId == row.ContactId, Ct)));
    }

    private static Task<HttpResponseMessage> RequestAsync(HttpClient client, WhatsAppAssistantContact contact, Guid requestId) =>
        client.PostAsJsonAsync(Route(contact.ContactId), new { requestId, sourceMessageId = contact.SourceMessageId, expectedModeRevision = 0 }, Ct);
    private static void AssertState(JsonElement response, string state)
    { Assert.Equal(state, response.GetProperty("state").GetString()); Assert.True(response.TryGetProperty("proposal", out _)); Assert.True(response.TryGetProperty("outgoing", out _)); }
    private static void AssertExplanation(JsonElement message)
    {
        var explanation = message.GetProperty("assistant"); Assert.Equal("La persona contó una visita y mencionó el lugar.", explanation.GetProperty("reason").GetString());
        Assert.Equal("Sucursal Norte", Assert.Single(explanation.GetProperty("proposedFields").EnumerateArray()).GetProperty("displayValue").GetString());
    }
}
