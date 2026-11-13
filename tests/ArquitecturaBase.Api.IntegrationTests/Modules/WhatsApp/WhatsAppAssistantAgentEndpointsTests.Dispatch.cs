using System.Diagnostics;
using System.Net;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Caching;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// An automatic reply from the engine's answer to Meta, through the real queue and worker with PostgreSQL, Redis and Meta simulated:
/// the order of its actions (H6) and the case questions with one and three buttons (B1).
/// </summary>
public sealed partial class WhatsAppAssistantAgentEndpointsTests
{
    private static readonly Guid Address = Guid.Parse("018f0000-0000-7000-8000-00000000a003");
    private static readonly Guid MoreHelp = Guid.Parse("018f0000-0000-7000-8000-00000000a004");
    private static readonly Guid Person = Guid.Parse("018f0000-0000-7000-8000-00000000a005");

    // H6: the actions of a reply are queued at once and the queue sends by the time they were requested and then by the id, a Guid
    // v7 that is random within a millisecond: without each action's place, their order was a coin toss. B1: the last one is a
    // case question with three buttons, which the real worker's guard lets through with its CASE_BUTTON ids.
    [Fact]
    public async Task The_three_actions_of_a_reply_leave_in_their_order_through_the_queue_and_the_worker()
    {
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(MetaWebhook.UniqueWaMessageId()));
        await using var host = await Host.CreateAsync(factory, Ct, meta);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant);
        using (var saved = await host.SendAsync("PUT", Route, manager, CasesAgent())) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var contactId = await host.AutomaticContactAsync("ana.orden@example.com", "¿A qué hora abren y dónde están?");

        await host.ReplyAsync(contactId, Reply("exactText", Hours, "Pregunta por el horario"), Reply("exactText", Address, "Pregunta por la dirección"),
            Reply("askButtons", MoreHelp, "Quiere algo más"));
        var sent = await host.DispatchAsync(meta, 3);

        Assert.Equal(["text", "text", "interactive"], sent.Select(body => body.GetProperty("type").GetString()));
        Assert.Equal(["Abrimos de 9 a 18.", "Estamos en Calle Falsa 123."], sent.Take(2).Select(body => body.GetProperty("text").GetProperty("body").GetString()));
        var question = sent[2].GetProperty("interactive");
        Assert.Equal(("button", "¿Querés algo más?"), (question.GetProperty("type").GetString(), question.GetProperty("body").GetProperty("text").GetString()));
        var buttons = question.GetProperty("action").GetProperty("buttons").EnumerateArray().Select(button => button.GetProperty("reply")).ToArray();
        Assert.Equal(["Ver horarios", "Ver dirección", "Hablar con alguien"], buttons.Select(button => button.GetProperty("title").GetString()));
        Assert.All(buttons, button => Assert.StartsWith("CASE_BUTTON:", button.GetProperty("id").GetString(), StringComparison.Ordinal));
        // Each action keeps its own place in the queue, so the order does not depend on the ids; the three went out.
        var queued = await host.OutgoingAsync(contactId);
        Assert.Equal(["Abrimos de 9 a 18.", "Estamos en Calle Falsa 123.", "¿Querés algo más?"], queued.Select(row => row.Body));
        Assert.Equal(3, queued.Select(row => row.RequestedAtUtc).Distinct().Count());
        Assert.All(queued, row => Assert.Equal(WhatsAppOutgoingMessageState.Accepted, row.State));
    }

    // B1: a case question with a single button goes out too, as reply buttons with its CASE_BUTTON id.
    [Fact]
    public async Task A_case_question_with_one_button_leaves_through_the_queue_and_the_worker()
    {
        var meta = new FakeMetaHandler(_ => FakeMetaHandler.Success(MetaWebhook.UniqueWaMessageId()));
        await using var host = await Host.CreateAsync(factory, Ct, meta);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant);
        using (var saved = await host.SendAsync("PUT", Route, manager, CasesAgent())) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var contactId = await host.AutomaticContactAsync("bruno.boton@example.com", "Quiero hablar con alguien.");

        await host.ReplyAsync(contactId, Reply("askButtons", Person, "Pide hablar con alguien"));
        var question = Assert.Single(await host.DispatchAsync(meta, 1)).GetProperty("interactive");

        Assert.Equal("¿Te paso con alguien?", question.GetProperty("body").GetProperty("text").GetString());
        var button = Assert.Single(question.GetProperty("action").GetProperty("buttons").EnumerateArray()).GetProperty("reply");
        Assert.Equal("Sí", button.GetProperty("title").GetString());
        Assert.StartsWith("CASE_BUTTON:", button.GetProperty("id").GetString(), StringComparison.Ordinal);
        Assert.Equal(WhatsAppOutgoingMessageState.Accepted, Assert.Single(await host.OutgoingAsync(contactId)).State);
    }

    private static AssistantObservedProposal Reply(string kind, Guid caseId, string title) => new(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
        new WhatsAppAssistantReplyPayload(kind, "own", caseId, title, "Lo pidió la persona.", null, null, null, null));

    private static object Es(string text) => new { es = text, en = (string?)null };

    // Two saved texts, a question with three buttons and another with one.
    private static object CasesAgent() => new
    {
        expectedRevision = 0, originalCulture = "es",
        profile = new { name = "Asistente", introduction = "Soy el asistente del negocio.", tone = "Cercano.", does = "Responde consultas.", doesNot = "No da precios.",
            handoffMessage = Es("Te paso con una persona del equipo.") },
        cases = new object[]
        {
            new { id = Hours, when = "Pregunta por el horario", inputType = "Any", isActive = true, action = new { kind = "ExactText", text = Es("Abrimos de 9 a 18.") } },
            new { id = Address, when = "Pregunta por la dirección", inputType = "Any", isActive = true, action = new { kind = "ExactText", text = Es("Estamos en Calle Falsa 123.") } },
            new { id = MoreHelp, when = "Quiere algo más", inputType = "Any", isActive = true, action = new { kind = "AskButtons", question = Es("¿Querés algo más?"),
                buttons = new object[] { new { title = Es("Ver horarios"), destination = "ExactText", text = Es("Abrimos de 9 a 18.") },
                    new { title = Es("Ver dirección"), destination = "ExactText", text = Es("Estamos en Calle Falsa 123.") },
                    new { title = Es("Hablar con alguien"), destination = "Handoff" } } } },
            new { id = Person, when = "Pide hablar con alguien", inputType = "Any", isActive = true, action = new { kind = "AskButtons", question = Es("¿Te paso con alguien?"),
                buttons = new object[] { new { title = Es("Sí"), destination = "Handoff" } } } },
        },
        fallback = new { kind = "NoReply" },
        limits = new { blockedTopics = Array.Empty<string>(), cannotPromise = "", maxReplyLength = 400, dailyReplyLimit = 10,
            allowedActions = new[] { "SendForm", "ExactText", "DraftedReply", "AskButtons", "Handoff", "NoReply" } },
    };

    private sealed partial class Host
    {
        // The real queue and worker with Meta simulated. The six seconds between two messages to one person are real time in Redis.
        private static void Dispatching(IServiceCollection services, FakeMetaHandler meta)
        {
            services.AddHttpClient(WhatsAppInfrastructureRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => meta);
            services.Replace(ServiceDescriptor.Singleton<IWhatsAppDispatchCoordinator>(provider =>
                new WhatsAppDispatchCoordinator(provider.GetRequiredService<RedisCache>(), TimeProvider.System)));
        }

        // A linked account in Assistant mode whose last message, a minute ago and out of a burst, is the one the agent answers.
        public async Task<Guid> AutomaticContactAsync(string email, string body)
        {
            var contactId = await LinkedContactAsync("Persona del asistente", email);
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var atUtc = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime.AddMinutes(-1);
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, token);
                contact.ChangeMode(WhatsAppConversationMode.Assistant);
                var message = WhatsAppMessage.Inbound(contact.Id, MetaWebhook.UniqueWaMessageId(), WhatsAppMessageKind.Text, body, null, atUtc);
                message.Classify(WhatsAppMessagePurpose.Business);
                message.SetInboundSequence(contact.RegisterInboundMessage());
                message.MarkProcessed(atUtc);
                contact.RecordInbound(contact.WaId, null, null, atUtc);
                db.Add(message);
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct);
            Assert.True(result.IsSuccess);
            return contactId;
        }

        // The automatic turn as the worker runs it: prepare the operation, submit it and complete it with the engine's answer.
        public async Task ReplyAsync(Guid contactId, AssistantObservedProposal first, params AssistantObservedProposal[] rest)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var dispatch = scope.ServiceProvider.GetRequiredService<IWhatsAppAssistantDispatchService>();
            var work = (await dispatch.PrepareAsync(contactId, Ct)).Value;
            Assert.NotNull(work);
            var operation = Assert.IsType<AssistantOperationRequest>(work.Operation);
            Assert.True((await Operations.SubmitAsync(operation, Ct)).IsSuccess);
            Operations.Complete(work.RequestId, first, rest);
            Assert.True((await dispatch.CompleteAsync(contactId, work.RequestId, await Operations.GetAsync(operation.Context, work.RequestId, Ct), Ct)).IsSuccess);
        }

        // The outgoing worker as it runs, until Meta got the expected messages or half a minute went by (two messages to one person
        // wait six seconds). What Meta got, in its order.
        public async Task<IReadOnlyList<JsonElement>> DispatchAsync(FakeMetaHandler meta, int count)
        {
            var worker = App.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
            var waited = Stopwatch.StartNew();
            while (meta.Calls < count && waited.Elapsed < TimeSpan.FromSeconds(30))
                if (!await worker.ProcessNextAsync(Ct)) await Task.Delay(TimeSpan.FromMilliseconds(200), TimeProvider.System, Ct);
            return [.. meta.Requests.Select(request => JsonDocument.Parse(request.Body!).RootElement.Clone())];
        }

        // The chat's outgoing rows in the queue's order.
        public async Task<IReadOnlyList<(string? Body, DateTime RequestedAtUtc, WhatsAppOutgoingMessageState State)>> OutgoingAsync(Guid contactId)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var rows = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<WhatsAppOutgoingMessage>().AsNoTracking()
                .Where(item => item.ContactId == contactId).OrderBy(item => item.RequestedAtUtc).ThenBy(item => item.Id)
                .Select(item => new { item.Body, item.RequestedAtUtc, item.State }).ToListAsync(Ct);
            return [.. rows.Select(row => (row.Body, row.RequestedAtUtc, row.State))];
        }
    }
}
