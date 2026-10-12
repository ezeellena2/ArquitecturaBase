using System.Net;
using System.Net.Http.Json;
using System.Globalization;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

[Collection(ApiTestGroup.Name)]
public sealed partial class WhatsAppFormSendingTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Form_send_uses_neutral_selection_and_creates_one_private_durable_outgoing_session()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        await using var host = FormsHost(provider, cloud, source, clock, flowSessionLifetimeHours: 6);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        using var unownedEmployee = await OperatorAsync(host, "formrecords.create");
        var senderId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var unownedEmployeeId = Guid.Parse(unownedEmployee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        source.Form = new StructuredFormSelectionRow(definitionId, versionId, 1, "FORM",
            new Dictionary<string, string> { ["es"] = "Formulario" }, 1,
            new Dictionary<string, string> { ["es"] = "Completar" });

        var snapshot = Snapshot(catalog: true);
        var question = snapshot.Questions[0];
        var offeredItem = Guid.NewGuid();
        source.Preparation = new([new(question.FieldId, question.Key, "Persona", question.SourceCatalogId!.Value,
            4, "Manual", "Personas", [new(offeredItem, "Texto recibido", "Detalle recibido")])], []);
        var channels = host.Services.GetServices<IStructuredFormChannel>();
        var channel = Assert.Single(channels, item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value!;
        Assert.Empty(check.Issues);
        var operationId = Guid.NewGuid();
        var publication = new StructuredFormPublicationRequest(operationId, definitionId, versionId, senderId,
            ["formtemplates.publish"], "test-publication-fingerprint", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot);
        Assert.True((await channel.AcceptAsync(publication, Ct)).IsSuccess);

        var worker = host.Services.GetRequiredService<WhatsAppFlowBackgroundService>();
        for (var step = 0; step < 12; step++)
        {
            await worker.ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var binding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .Where(value => value.VersionId == versionId && value.Culture == "es").SingleAsync(Ct));
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);

        var contactId = await factory.InTransactionAsync(services =>
        {
            var contact = WhatsAppContact.Create(TestPhones.Unique().Value.TrimStart('+'), null,
                "Form employee", clock.GetUtcNow().UtcDateTime);
            contact.LinkUser(employeeId);
            services.GetRequiredService<IWhatsAppContactRepository>().Add(contact);
            return Task.FromResult(contact.Id);
        });
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader));
        using var choices = await client.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        Assert.NotNull(context);
        using var prepared = await client.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var preparedJson = await prepared.ReadJsonAsync();
        Assert.Equal(context, preparedJson.GetProperty("recipientContextRevision").GetString());
        Assert.Equal("Texto recibido", preparedJson.GetProperty("preparation").GetProperty("catalogs")[0].GetProperty("options")[0].GetProperty("text").GetString());
        using var denied = await OperatorAsync(host, WhatsAppPermissions.Read);
        using var deniedPreparation = await denied.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{versionId}/preparation", Ct);
        Assert.Equal(HttpStatusCode.Forbidden, deniedPreparation.StatusCode);
        using var missingVersion = await client.GetAsync($"/api/whatsapp/chats/{contactId}/forms/{definitionId}/versions/{Guid.NewGuid()}/preparation", Ct);
        Assert.Equal(HttpStatusCode.Conflict, missingVersion.StatusCode);
        var sendStartedAtUtc = clock.GetUtcNow().UtcDateTime;
        var request = new { requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Completá este formulario" };
        using var accepted = await client.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct);
        Assert.True(accepted.StatusCode == HttpStatusCode.Accepted, await accepted.Content.ReadAsStringAsync(Ct));
        var sessionExpiry = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowSession>()
            .Where(session => session.ContactId == contactId).Select(session => session.ExpiresAtUtc).SingleAsync(Ct));
        Assert.Equal(sendStartedAtUtc.AddHours(6), sessionExpiry);
        var queued = await accepted.ReadJsonAsync();
        Assert.Equal("Flow", queued.GetProperty("kind").GetString());
        using var replay = await client.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct);
        Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(queued.GetProperty("id").GetGuid(), (await replay.ReadJsonAsync()).GetProperty("id").GetGuid());
        source.Preparation = new([], []);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var sentFlow = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        Assert.Equal("Abrir formulario", sentFlow.ButtonText);
        Assert.Equal(offeredItem, Assert.Single(Assert.Single(sentFlow.Catalogs).Options).Id);
        Assert.Equal("Texto recibido", sentFlow.Catalogs[0].Options[0].Text);
        var payload = WhatsAppMessagePayload.Build(sentFlow, new WhatsAppOptions { WabaId = sentFlow.Account.WabaId,
            PhoneNumberId = sentFlow.Account.PhoneNumberId, GraphApiVersion = sentFlow.Account.GraphApiVersion });
        var initial = payload["interactive"]!["action"]!["parameters"]!["flow_action_payload"]!["data"]![question.Key + "_options"]![0]!;
        Assert.Equal(offeredItem.ToString("D"), initial["id"]!.GetValue<string>());
        Assert.Equal("Texto recibido", initial["title"]!.GetValue<string>());
        Assert.Equal("Detalle recibido", initial["description"]!.GetValue<string>());
        Assert.Null(initial["code"]);

        var unownedContactId = await CreateLinkedContactAsync(unownedEmployeeId, clock);
        using var unownedChoices = await client.GetAsync($"/api/whatsapp/chats/{unownedContactId}/forms", Ct);
        var unownedContext = (await unownedChoices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        var unownedRequest = new { requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = unownedContext, message = "Completá este formulario" };
        using var unownedAccepted = await client.PostAsJsonAsync($"/api/whatsapp/chats/{unownedContactId}/form-messages", unownedRequest, Ct);
        Assert.True(unownedAccepted.StatusCode == HttpStatusCode.Accepted, await unownedAccepted.Content.ReadAsStringAsync(Ct));
        provider.OwnsPhone = false;
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.Single(cloud.Messages);
        var path = (await unownedAccepted.ReadJsonAsync()).GetProperty("recoveryPath").GetString();
        using var outcome = await client.GetAsync(path, Ct);
        Assert.Equal("Failed", (await outcome.ReadJsonAsync()).GetProperty("state").GetString());
    }

    private static StructuredFormCheckRequest Snapshot(bool catalog = false)
    {
        var fieldId = Guid.NewGuid();
        return new StructuredFormCheckRequest(1, "es",
            new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" },
            new Dictionary<string, string> { ["es"] = "Completar", ["en"] = "Submit" },
            [new StructuredFormQuestion(fieldId, "field_" + fieldId.ToString("N"), catalog ? "choice" : "shortText",
                new Dictionary<string, string> { ["es"] = "Nombre", ["en"] = "Name" }, null, false, catalog ? null : 100, null, catalog ? Guid.NewGuid() : null)],
            ["es", "en"]);
    }

    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> FormsHost(CapturingFlowProvider provider,
        CapturingWhatsAppCloudClient cloud, CapturingStructuredFormSource source, FakeTimeProvider clock,
        int flowSessionLifetimeHours = 24) =>
        factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:WabaId", "222")
            .UseSetting("WhatsApp:FlowSessionLifetimeHours", flowSessionLifetimeHours.ToString(CultureInfo.InvariantCulture))
            .ConfigureTestServices(services =>
        {
            services.RemoveAll<IWhatsAppFlowProvider>(); services.AddSingleton<IWhatsAppFlowProvider>(provider);
            services.RemoveAll<IWhatsAppCloudClient>(); services.AddSingleton<IWhatsAppCloudClient>(cloud);
            services.RemoveAll<IStructuredFormSource>(); services.AddSingleton<IStructuredFormSource>(source);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.AddFakeLogging();
        }));

    private Task<Guid> CreateLinkedContactAsync(Guid userId, TimeProvider clock) => factory.InTransactionAsync(services =>
    {
        var contact = WhatsAppContact.Create(TestPhones.Unique().Value.TrimStart('+'), null,
            "Form recipient", clock.GetUtcNow().UtcDateTime);
        contact.LinkUser(userId);
        services.GetRequiredService<IWhatsAppContactRepository>().Add(contact);
        return Task.FromResult(contact.Id);
    });

    private async Task<HttpClient> OperatorAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host,
        params string[] permissions)
    {
        var userId = await factory.InTransactionAsync(async services =>
        {
            var roleName = "form-send-" + Guid.NewGuid().ToString("N");
            await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, Ct);
            var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                Email.Create(roleName + "@example.com").Value, null, false, "Operador", "es", Ct);
            await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [roleName], Ct);
            return user.Id;
        });
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return client;
    }
}
