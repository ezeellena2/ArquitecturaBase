using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// Casos del asistente (AS1-A, AS3): the screen «Asistente» saves the main agent with its revision and 409, answers each problem at
/// its path, and Probar runs in its own channel with the chosen account. The agent is one row for the whole installation, so the
/// tests use a database of their own.
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed partial class WhatsAppAssistantAgentEndpointsTests(ApiFactory factory)
{
    private const string Route = "/api/whatsapp/assistant-agent";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Guid Hours = Guid.Parse("018f0000-0000-7000-8000-00000000a001");

    public static TheoryData<string, string> Routes => new()
    {
        { "GET", Route }, { "PUT", Route }, { "POST", Route + "/tests" }, { "GET", Route + "/tests/" + Guid.NewGuid() + "?contactId=" + Guid.NewGuid() },
        { "GET", Route + "/test-recipients" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_route_needs_a_session_and_configure_assistant(string method, string path)
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var reader = await host.OperatorAsync(WhatsAppPermissions.Read, WhatsAppPermissions.Send);

        using var anonymous = await host.SendAsync(method, path, null);
        using var forbidden = await host.SendAsync(method, path, reader);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task The_agent_reads_its_defaults_saves_with_its_revision_and_a_stale_save_is_a_conflict()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant);

        using var defaults = await host.SendAsync("GET", Route, manager);
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        var read = await defaults.ReadJsonAsync();
        Assert.Equal((0, "NoReply", 400), (read.GetProperty("revision").GetInt64(), read.GetProperty("fallback").GetProperty("kind").GetString(),
            read.GetProperty("limits").GetProperty("maxReplyLength").GetInt32()));
        Assert.Empty(read.GetProperty("cases").EnumerateArray());
        await WhatsAppAssistantAgentContractCapture.WriteAsync(defaults, "agent-defaults", Ct);

        using var saved = await host.SendAsync("PUT", Route, manager, Agent(0));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var agent = await saved.ReadJsonAsync();
        Assert.Equal(1, agent.GetProperty("revision").GetInt64());
        var item = Assert.Single(agent.GetProperty("cases").EnumerateArray());
        Assert.Equal(("Pregunta por el horario", "ExactText", "Abrimos de 9 a 18."), (item.GetProperty("when").GetString(),
            item.GetProperty("action").GetProperty("kind").GetString(), item.GetProperty("action").GetProperty("text").GetProperty("es").GetString()));
        await WhatsAppAssistantAgentContractCapture.WriteAsync(saved, "agent-saved", Ct);

        using var stale = await host.SendAsync("PUT", Route, manager, Agent(0));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("WhatsApp.AssistantAgent.RevisionConflict", (await stale.ReadJsonAsync()).GetProperty("code").GetString());
        await WhatsAppAssistantAgentContractCapture.WriteAsync(stale, "agent-conflict", Ct);

        using var again = await host.SendAsync("GET", Route, manager);
        Assert.Equal(1, (await again.ReadJsonAsync()).GetProperty("revision").GetInt64());
    }

    [Fact]
    public async Task An_invalid_agent_answers_each_problem_at_its_path_and_an_unknown_property_is_rejected()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant);

        using var invalid = await host.SendAsync("PUT", Route, manager, Agent(0, when: " ", buttonTitle: new string('x', 21)));
        using var unknown = await host.SendAsync("PUT", Route, manager, new { expectedRevision = 0, originalCulture = "es", color = "red" });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await invalid.ReadJsonAsync()).GetProperty("errors");
        Assert.True(errors.TryGetProperty("cases[0].when", out _));
        Assert.True(errors.TryGetProperty("cases[1].action.buttons[0].title", out _));
        await WhatsAppAssistantAgentContractCapture.WriteAsync(invalid, "agent-invalid", Ct);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Probar_writes_as_a_linked_account_in_its_own_channel_and_shows_what_would_go_out()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read, Permissions.Users.Read, "formrecords.read");
        var configurer = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read);
        var contactId = await host.LinkedContactAsync("Ana Gómez", "ana.probar@example.com");
        using (var saved = await host.SendAsync("PUT", Route, manager, Agent(0))) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var recipients = await host.SendAsync("GET", Route + "/test-recipients?search=ana", manager);
        using var withoutEmail = await host.SendAsync("GET", Route + "/test-recipients?search=ana", configurer);
        Assert.Equal(HttpStatusCode.OK, recipients.StatusCode);
        var recipient = Assert.Single((await recipients.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal((contactId, "Ana Gómez", "ana.probar@example.com"), (recipient.GetProperty("contactId").GetGuid(),
            recipient.GetProperty("name").GetString(), recipient.GetProperty("email").GetString()));
        Assert.Equal(JsonValueKind.Null, Assert.Single((await withoutEmail.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("email").ValueKind);
        await WhatsAppAssistantAgentContractCapture.WriteAsync(recipients, "test-recipients", Ct);

        var requestId = Guid.NewGuid();
        using var started = await host.SendAsync("POST", Route + "/tests", manager, new { requestId, contactId, messages = new[] { "Hola", "¿A qué hora abren?" } });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        await WhatsAppAssistantAgentContractCapture.WriteAsync(started, "test-accepted", Ct);
        var submitted = Assert.Single(host.Operations.Submitted);
        Assert.Equal(("whatsapp-assistant-test", manager, (Guid?)contactId, "Hola\n¿A qué hora abren?"),
            (submitted.Context.ChannelId, submitted.Context.ActorUserId, submitted.Context.ChannelConversationId, submitted.Text));
        Assert.Contains(new AssistantCapabilityReference("whatsapp.reply", 1), submitted.Context.AllowedCapabilities);
        Assert.Contains(WhatsAppPermissions.Read, submitted.Context.RequiredPermissions.Actor);

        using var pending = await host.SendAsync("GET", Route + $"/tests/{requestId}?contactId={contactId}", manager);
        Assert.Equal("pending", (await pending.ReadJsonAsync()).GetProperty("state").GetString());
        await WhatsAppAssistantAgentContractCapture.WriteAsync(pending, "test-pending", Ct);

        host.Operations.Complete(requestId, new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
            new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.", "Abrimos de 9 a 18.", null, null, null)));
        using var completed = await host.SendAsync("GET", Route + $"/tests/{requestId}?contactId={contactId}", manager);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        var result = await completed.ReadJsonAsync();
        Assert.Equal("completed", result.GetProperty("state").GetString());
        var action = Assert.Single(result.GetProperty("actions").EnumerateArray());
        Assert.Equal(("ExactText", "Own", "Pregunta por el horario", "Abrimos de 9 a 18."), (action.GetProperty("kind").GetString(),
            action.GetProperty("caseSource").GetString(), action.GetProperty("caseTitle").GetString(), action.GetProperty("text").GetString()));
        // Whoever reads records sees why (H4); without the forms module nobody reads records, so nobody does.
        if (host.App.Services.GetService<ArquitecturaBase.Api.IntegrationTests.TestFeatures.IStructuredFormPublicationDriver>() is not null)
            Assert.Equal("Pregunta el horario.", action.GetProperty("reason").GetString());
        await WhatsAppAssistantAgentContractCapture.WriteAsync(completed, "test-completed", Ct);
    }

    [Fact]
    public async Task Probar_needs_one_to_five_messages_and_an_account_linked_to_a_chat()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read);
        var contactId = await host.LinkedContactAsync("Bruno Díaz", "bruno.probar@example.com");
        using (var saved = await host.SendAsync("PUT", Route, manager, Agent(0))) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var tooMany = await host.SendAsync("POST", Route + "/tests", manager,
            new { requestId = Guid.NewGuid(), contactId, messages = Enumerable.Repeat("Hola", 6).ToArray() });
        using var unknown = await host.SendAsync("POST", Route + "/tests", manager, new { requestId = Guid.NewGuid(), contactId = Guid.NewGuid(), messages = new[] { "Hola" } });
        using var missing = await host.SendAsync("GET", Route + $"/tests/{Guid.NewGuid()}?contactId={contactId}", manager);

        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.True((await tooMany.ReadJsonAsync()).GetProperty("errors").TryGetProperty("messages", out _));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(host.Operations.Submitted);
    }

    // H4: Probar is for someone who reads WhatsApp chats, the email is searched and shown only with users.read, and without reading
    // records a test shows neither why nor what a form would fill.
    [Fact]
    public async Task Probar_follows_the_permissions_of_who_tests()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read, Permissions.Users.Read);
        var tester = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read);
        var configurer = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant);
        var contactId = await host.LinkedContactAsync("Carla Ruiz", "carla.reservada@example.com");
        var nameless = await host.LinkedContactAsync(null, "sin.nombre.reservado@example.com", "Perfil de WhatsApp");
        var unnamed = await host.LinkedContactAsync(null, "sin.nada.reservado@example.com", "");
        using (var saved = await host.SendAsync("PUT", Route, manager, Agent(0))) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var byEmail = await host.SendAsync("GET", Route + "/test-recipients?search=carla.reservada", manager);
        using var hiddenEmail = await host.SendAsync("GET", Route + "/test-recipients?search=carla.reservada", tester);
        using var byName = await host.SendAsync("GET", Route + "/test-recipients?search=carla", tester);
        using var namelessForManager = await host.SendAsync("GET", Route + "/test-recipients?search=sin.nombre.reservado", manager);
        using var namelessForTester = await host.SendAsync("GET", Route + "/test-recipients?search=perfil de whatsapp", tester);
        using var emailOfNameless = await host.SendAsync("GET", Route + "/test-recipients?search=sin.nombre.reservado", tester);
        Assert.Equal("carla.reservada@example.com", Assert.Single((await byEmail.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("email").GetString());
        Assert.Empty((await hiddenEmail.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        var found = Assert.Single((await byName.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal((contactId, "Carla Ruiz", JsonValueKind.Null), (found.GetProperty("contactId").GetGuid(), found.GetProperty("name").GetString(), found.GetProperty("email").ValueKind));
        Assert.Equal("sin.nombre.reservado@example.com", Assert.Single((await namelessForManager.ReadJsonAsync()).GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        var withoutEmail = Assert.Single((await namelessForTester.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        Assert.Equal((nameless, "Perfil de WhatsApp", JsonValueKind.Null), (withoutEmail.GetProperty("contactId").GetGuid(), withoutEmail.GetProperty("name").GetString(), withoutEmail.GetProperty("email").ValueKind));
        Assert.Empty((await emailOfNameless.ReadJsonAsync()).GetProperty("items").EnumerateArray());
        using var everyone = await host.SendAsync("GET", Route + "/test-recipients?search=%2B", tester);
        Assert.StartsWith("+", (await everyone.ReadJsonAsync()).GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("contactId").GetGuid() == unnamed).GetProperty("name").GetString(), StringComparison.Ordinal);

        var requestId = Guid.NewGuid();
        using var list = await host.SendAsync("GET", Route + "/test-recipients", configurer);
        using var start = await host.SendAsync("POST", Route + "/tests", configurer, new { requestId, contactId, messages = new[] { "Hola" } });
        using var read = await host.SendAsync("GET", Route + $"/tests/{requestId}?contactId={contactId}", configurer);
        Assert.All(new[] { list, start, read }, response => Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode));
        Assert.Empty(host.Operations.Submitted);

        using var started = await host.SendAsync("POST", Route + "/tests", tester, new { requestId, contactId, messages = new[] { "¿A qué hora abren?" } });
        Assert.Equal(HttpStatusCode.Accepted, started.StatusCode);
        host.Operations.Complete(requestId, new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
            new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Leyó el aviso con el total.", "Abrimos de 9 a 18.", null, null, null)));
        using var completed = await host.SendAsync("GET", Route + $"/tests/{requestId}?contactId={contactId}", tester);
        var action = Assert.Single((await completed.ReadJsonAsync()).GetProperty("actions").EnumerateArray());
        Assert.Equal(("Abrimos de 9 a 18.", ""), (action.GetProperty("text").GetString(), action.GetProperty("reason").GetString()));
    }

    private static object Agent(long revision, string when = "Pregunta por el horario", string buttonTitle = "Sí") => new
    {
        expectedRevision = revision, originalCulture = "es",
        profile = new { name = "Asistente", introduction = "Soy el asistente del negocio.", tone = "Cercano.", does = "Responde consultas.", doesNot = "No da precios.",
            handoffMessage = new { es = "Te paso con una persona del equipo.", en = (string?)null } },
        cases = buttonTitle == "Sí"
            ? new object[] { new { id = Hours, when, inputType = "Any", isActive = true, action = new { kind = "ExactText", text = new { es = "Abrimos de 9 a 18.", en = (string?)null } } } }
            : [new { id = Hours, when, inputType = "Any", isActive = true, action = new { kind = "ExactText", text = new { es = "Abrimos de 9 a 18.", en = (string?)null } } },
                new { id = Guid.Parse("018f0000-0000-7000-8000-00000000a002"), when = "Pide hablar con alguien", inputType = "Any", isActive = true,
                    action = new { kind = "AskButtons", question = new { es = "¿Te paso con alguien?", en = (string?)null },
                        buttons = new[] { new { title = new { es = buttonTitle, en = (string?)null }, destination = "Handoff" } } } }],
        fallback = new { kind = "NoReply" },
        limits = new { blockedTopics = Array.Empty<string>(), cannotPromise = "", maxReplyLength = 400, dailyReplyLimit = 10,
            allowedActions = new[] { "SendForm", "ExactText", "DraftedReply", "AskButtons", "Handoff", "NoReply" } },
    };

    // AS4: the chat with the agent, for the front's contract. The list with «Necesita una persona», the thread with the why of a
    // reply and the line of what the assistant decided without a message, and the operator's text suggestion with its case.
    [Fact]
    public async Task The_chat_carries_the_handoff_the_why_of_a_reply_its_lines_and_the_operators_text_suggestion()
    {
        await using var host = await Host.CreateAsync(factory, Ct);
        var manager = await host.OperatorAsync(WhatsAppPermissions.ConfigureAssistant, WhatsAppPermissions.Read, WhatsAppPermissions.Send);
        using (var saved = await host.SendAsync("PUT", Route, manager, Agent(0))) Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var contactId = await host.LinkedContactAsync("Ana Gómez", "ana.chat@example.com");
        var (sourceMessageId, modeRevision) = await host.HandedOffConversationAsync(contactId);

        using var list = await host.SendAsync("GET", "/api/whatsapp/chats?page=1&pageSize=20&needsPerson=true", manager);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var chats = await list.ReadJsonAsync();
        Assert.Equal(1, chats.GetProperty("needsPersonCount").GetInt32());
        var chat = Assert.Single(chats.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(("case", "Pide una persona"), (chat.GetProperty("needsPerson").GetProperty("reasonCode").GetString(),
            chat.GetProperty("needsPerson").GetProperty("reason").GetString()));
        await WhatsAppAssistantAgentContractCapture.WriteAsync(list, "chats-needs-person", Ct);

        using var thread = await host.SendAsync("GET", $"/api/whatsapp/chats/{contactId}/messages", manager);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var history = await thread.ReadJsonAsync();
        Assert.Equal("case", history.GetProperty("contact").GetProperty("needsPerson").GetProperty("reasonCode").GetString());
        var reply = Assert.Single(history.GetProperty("outgoing").EnumerateArray()).GetProperty("assistantReply");
        Assert.Equal(("ExactText", "Own", "Pregunta por el horario", 1, 1), (reply.GetProperty("kind").GetString(), reply.GetProperty("caseSource").GetString(),
            reply.GetProperty("caseTitle").GetString(), reply.GetProperty("ordinal").GetInt32(), reply.GetProperty("count").GetInt32()));
        var line = Assert.Single(history.GetProperty("assistantEvents").EnumerateArray()).GetProperty("reply");
        Assert.Equal(("NoReply", "Fallback"), (line.GetProperty("kind").GetString(), line.GetProperty("caseSource").GetString()));
        await WhatsAppAssistantAgentContractCapture.WriteAsync(thread, "thread-agent", Ct);

        var requestId = Guid.NewGuid();
        var suggestions = $"/api/whatsapp/chats/{contactId}/assistant-suggestions";
        using (var requested = await host.SendAsync("POST", suggestions, manager, new { requestId, sourceMessageId, expectedModeRevision = modeRevision }))
            Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        // The worker prepares the operation, submits it and completes it with the engine's answer.
        await using (var scope = host.App.Services.CreateAsyncScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<IWhatsAppAssistantDispatchService>();
            var work = (await dispatch.PrepareAsync(contactId, Ct)).Value;
            Assert.Equal(requestId, work!.RequestId);
            var operation = Assert.IsType<AssistantOperationRequest>(work.Operation);
            Assert.Equal(new AssistantCapabilityReference[] { new("whatsapp.agent", 1), new("forms.assistant-candidates", 1), new("whatsapp.agent-history", 1) },
                operation.Context.PreloadedCapabilities);
            Assert.True((await host.Operations.SubmitAsync(operation, Ct)).IsSuccess);
            host.Operations.Complete(requestId, new AssistantObservedProposal(Guid.NewGuid(), Guid.NewGuid(), new("whatsapp.reply", 1),
                new WhatsAppAssistantReplyPayload("exactText", "own", Hours, "Pregunta por el horario", "Pregunta el horario.", "Abrimos de 9 a 18.", null, null, null)));
            Assert.True((await dispatch.CompleteAsync(contactId, requestId, await host.Operations.GetAsync(operation.Context, requestId, Ct), Ct)).IsSuccess);
        }
        using var ready = await host.SendAsync("GET", suggestions + $"/{requestId}", manager);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        var suggestion = await ready.ReadJsonAsync();
        Assert.Equal(("Ready", JsonValueKind.Null), (suggestion.GetProperty("state").GetString(), suggestion.GetProperty("proposal").ValueKind));
        Assert.Equal(("Abrimos de 9 a 18.", "Own", "Pregunta por el horario"), (suggestion.GetProperty("textProposal").GetProperty("text").GetString(),
            suggestion.GetProperty("textProposal").GetProperty("caseSource").GetString(), suggestion.GetProperty("textProposal").GetProperty("caseTitle").GetString()));
        await WhatsAppAssistantAgentContractCapture.WriteAsync(ready, "suggestion-text-ready", Ct);
    }

    private sealed partial class Host : IAsyncDisposable
    {
        private Host(WebApplicationFactory<Program> app) => App = app;
        public WebApplicationFactory<Program> App { get; }
        public Operations Operations { get; } = new();

        public static async Task<Host> CreateAsync(ApiFactory factory, CancellationToken ct, FakeMetaHandler? meta = null)
        {
            var connection = factory.NewDatabaseConnectionString("assistant_agent");
            Host? host = null;
            var app = factory.WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:appdb", connection)
                .ConfigureTestServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<IAssistantOperations>(_ => host!.Operations));
                    if (meta is not null) Dispatching(services, meta);
                }));
            host = new Host(app);
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync(ct);
            await app.Services.SeedDatabaseAsync(ct);
            return host;
        }

        public async Task<HttpResponseMessage> SendAsync(string method, string path, Guid? actor, object? body = null)
        {
            using var client = App.CreateClient();
            using var request = new HttpRequestMessage(new HttpMethod(method), path);
            if (actor is { } id) request.Headers.Add(TestAuthHandler.UserIdHeader, id.ToString());
            if (body is not null || method is "PUT" or "POST") request.Content = JsonContent.Create(body ?? new { });
            return await client.SendAsync(request, Ct);
        }

        public async Task<Guid> OperatorAsync(params string[] permissions)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var roleName = "agent-" + Guid.NewGuid().ToString("N");
            Guid userId = Guid.Empty;
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                await scope.ServiceProvider.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, token);
                var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                var user = await users.CreateAsync(Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", token);
                await users.SetRolesAsync(user.Id, [roleName], token);
                userId = user.Id;
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct);
            Assert.True(result.IsSuccess);
            return userId;
        }

        public async Task<Guid> LinkedContactAsync(string? name, string email, string? profileName = null)
        {
            await using var scope = App.Services.CreateAsyncScope();
            Guid contactId = Guid.Empty;
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                var phone = PhoneNumber.Create("+" + MetaWebhook.UniqueWaId()).Value;
                var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>().CreateAsync(Email.Create(email).Value, phone, true, name, "es", token);
                var contact = WhatsAppContact.Create(phone.Value[1..], MetaWebhook.UniqueBsuid(), profileName ?? name,
                    scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime);
                contact.LinkUser(user.Id);
                scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Add(contact);
                contactId = contact.Id;
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct);
            Assert.True(result.IsSuccess);
            return contactId;
        }

        // A conversation the agent answered and then handed off (AS4): the person's messages, the exact text with its why, a «No
        // responder» with its outcome and the mark with its reason. The latest message is the source of a suggestion.
        public async Task<(Guid SourceMessageId, long ModeRevision)> HandedOffConversationAsync(Guid contactId)
        {
            await using var scope = App.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var nowUtc = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            (Guid, long) source = default;
            var result = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteInTransactionAsync(async token =>
            {
                var contact = await db.Set<WhatsAppContact>().SingleAsync(item => item.Id == contactId, token);
                contact.ChangeMode(WhatsAppConversationMode.Assistant);
                WhatsAppMessage Inbound(string body, DateTime atUtc)
                {
                    var message = WhatsAppMessage.Inbound(contact.Id, "wamid.agent-chat-" + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.Text, body, null, atUtc);
                    message.Classify(WhatsAppMessagePurpose.Business);
                    message.SetInboundSequence(contact.RegisterInboundMessage());
                    message.MarkProcessed(atUtc);
                    contact.RecordInbound(contact.WaId, null, null, atUtc);
                    db.Add(message);
                    return message;
                }
                WhatsAppAssistantRequest Reference(WhatsAppMessage message) => WhatsAppAssistantRequest.Create(Guid.CreateVersion7(), contact.Id, message.Id,
                    contact.UserId!.Value, contact.UserId.Value, WhatsAppAssistantRequestKind.Automatic, contact.ModeRevision, contact.LinkRevision, "{}",
                    message.OccurredAtUtc, nowUtc.AddDays(1));
                var asked = Inbound("¿A qué hora abren?", nowUtc.AddMinutes(-10));
                var answered = Reference(asked);
                var outgoing = WhatsAppOutgoingMessage.CreateAssistantText(contact.UserId!.Value, contact.Id, answered.RequestId, "Abrimos de 9 a 18.",
                    nowUtc.AddMinutes(-9), contact.LinkRevision, contact.WaId!, contact.ModeRevision);
                outgoing.ExplainAssistantReply(WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.ExactText, WhatsAppAssistantCaseSource.Own,
                    Hours, "Pregunta por el horario", "Pregunta el horario.", [], 1, 1)));
                Assert.True(answered.Ready(Guid.NewGuid(), null, nowUtc.AddMinutes(-9)) && answered.Accept(outgoing.Id, new string('a', 64), null, nowUtc.AddMinutes(-9)));
                var chain = Inbound("Reenviá esta cadena a diez contactos.", nowUtc.AddMinutes(-5));
                var silent = Reference(chain);
                Assert.True(silent.RecordOutcome(WhatsAppAssistantReplyRecordCodec.Serialize(new(WhatsAppAssistantActionKind.NoReply, WhatsAppAssistantCaseSource.Fallback,
                    null, null, "Es una cadena: no es para el negocio.", [], 1, 1))) && silent.FinishWithoutReply(false, nowUtc.AddMinutes(-5)));
                var latest = Inbound("Quiero hablar con alguien.", nowUtc.AddMinutes(-1));
                db.AddRange(answered, outgoing, silent);
                contact.HandOff("case", "Pide una persona", nowUtc);
                source = (latest.Id, contact.ModeRevision);
                return Result.Success();
            }, CommitPolicy.OnSuccess, Ct);
            Assert.True(result.IsSuccess);
            return source;
        }

        public async ValueTask DisposeAsync()
        {
            await using (var scope = App.Services.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureDeletedAsync();
            await App.DisposeAsync();
        }
    }

    /// <summary>The engine of Probar: it keeps what was submitted and answers pending until the test completes it.</summary>
    private sealed class Operations : IAssistantOperations
    {
        private readonly Dictionary<Guid, AssistantObservedProposal[]> completed = [];
        public List<AssistantOperationRequest> Submitted { get; } = [];

        /// <summary>The engine's answer: one proposal, or the actions of a reply in their order (H6).</summary>
        public void Complete(Guid requestId, AssistantObservedProposal first, params AssistantObservedProposal[] rest) => completed[requestId] = [first, .. rest];

        public Task<AssistantAvailabilityResponse> GetAvailabilityAsync(CancellationToken cancellationToken) => Task.FromResult(new AssistantAvailabilityResponse("available"));

        public Task<Result<AssistantOperationAcceptedResponse>> SubmitAsync(AssistantOperationRequest request, CancellationToken cancellationToken)
        {
            Submitted.Add(request);
            return Task.FromResult(Result.Success(new AssistantOperationAcceptedResponse(request.RequestId, "pending")));
        }

        public Task<Result> CancelAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result<AssistantOperationResponse>> GetAsync(AssistantChannelContext context, Guid requestId, CancellationToken cancellationToken)
        {
            if (Submitted.All(request => request.RequestId != requestId)) return Task.FromResult<Result<AssistantOperationResponse>>(Error.NotFound("Assistant.Operation.NotFound", "Not found."));
            return Task.FromResult<Result<AssistantOperationResponse>>(completed.TryGetValue(requestId, out var proposals)
                ? new AssistantOperationResponse(requestId, "completed", proposals[0], new("qualified-text", 1, "neutral", 1, new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc), context.ChannelId), null)
                    { Proposals = proposals.Length > 1 ? proposals : [] }
                : new AssistantOperationResponse(requestId, "pending", null, null, null));
        }
    }
}
