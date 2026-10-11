using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Http_flow_contracts_preserve_form_presentation_across_queue_and_history(string culture)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var source = new CapturingStructuredFormSource();
        var transport = new ControlledWhatsAppSendClient();
        var preview = WhatsAppFormAppPreview.FromEnvironment(culture);
        await using var host = FormsHost(provider, new CapturingWhatsAppCloudClient(), source, clock)
            .WithWebHostBuilder(builder =>
            {
                preview?.Configure(builder);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWhatsAppCloudClient>();
                    services.AddSingleton<IWhatsAppCloudClient>(transport);
                });
            });
        if (preview is not null) WhatsAppFormAppPreview.Listen(host);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        author.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = await FixtureEmployeeAsync(culture);
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, versionId, 1, "FORM", snapshot.Name, 1, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new(operationId, definitionId, versionId, authorId,
            ["formtemplates.publish"], "synthetic-fixture-publication", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot), Ct)).IsSuccess);
        for (var index = 0; index < 12; index++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }

        var captures = new List<object>();
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        var previewScenarios = new List<(string Key, Guid ActorId, Guid ContactId, string Culture)>
            { ("send-es", authorId, contactId, culture) };
        var choices = await CaptureAsync("choices", author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct), HttpStatusCode.OK);
        Assert.Equal(culture, choices.GetProperty("recipientCulture").GetString());
        Assert.Equal(JsonValueKind.Object, choices.GetProperty("page").GetProperty("items")[0].GetProperty("name").ValueKind);
        var initialThread = await CaptureAsync("thread-before-send", author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct), HttpStatusCode.OK);
        Assert.Empty(initialThread.GetProperty("outgoing").EnumerateArray());
        var requestId = Guid.NewGuid();
        var request = new JsonObject
        {
            ["requestId"] = requestId.ToString(), ["definitionId"] = definitionId.ToString(), ["versionId"] = versionId.ToString(),
            ["expectedModeRevision"] = 0, ["expectedRecipientContextRevision"] = choices.GetProperty("recipientContextRevision").GetString(),
            ["message"] = culture == "es" ? "Completá este formulario" : "Complete this form",
        };
        using var denied = await OperatorAsync(host, WhatsAppPermissions.Read);
        previewScenarios.Add(("read-es", Guid.Parse(denied.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single()), contactId, culture));
        denied.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
        await CaptureAsync("choices-forbidden", denied.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct), HttpStatusCode.Forbidden);
        var stale = request.DeepClone().AsObject(); stale["expectedRecipientContextRevision"] = "stale-context";
        var contextError = await CaptureAsync("send-context-changed", author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", stale, Ct), HttpStatusCode.Conflict);
        Assert.Equal(FlowErrors.RecipientContextChanged.Code, contextError.GetProperty("code").GetString());
        var missing = request.DeepClone().AsObject(); missing["versionId"] = Guid.NewGuid().ToString();
        var versionError = await CaptureAsync("send-version-unavailable", author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", missing, Ct), HttpStatusCode.Conflict);
        Assert.Equal(FlowErrors.FormUnavailable.Code, versionError.GetProperty("code").GetString());
        var sent = await CaptureAsync("send-pending", author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct), HttpStatusCode.Accepted);
        Assert.Equal(JsonValueKind.Object, sent.GetProperty("form").GetProperty("name").ValueKind);
        var recoveryPath = $"/api/whatsapp/chats/{contactId}/outgoing/{requestId}";
        Assert.Equal(recoveryPath, sent.GetProperty("recoveryPath").GetString());
        await CaptureAsync("outgoing-pending", author.GetAsync(recoveryPath, Ct), HttpStatusCode.OK);
        var pendingThread = await CaptureAsync("thread-pending", author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct), HttpStatusCode.OK);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var completed = await CaptureAsync("outgoing-accepted", author.GetAsync(recoveryPath, Ct), HttpStatusCode.OK);
        Assert.Equal("Accepted", completed.GetProperty("state").GetString());
        var acceptedThread = await CaptureAsync("thread-accepted", author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct), HttpStatusCode.OK);
        await CaptureAsync("thread-reloaded", author.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct), HttpStatusCode.OK);

        foreach (var uncertain in new[] { true, false })
        {
            var nextContactId = await CreateLinkedContactAsync(await FixtureEmployeeAsync(culture), clock);
            previewScenarios.Add((uncertain ? "uncertain-es" : "failed-es", authorId, nextContactId, culture));
            using var selection = await author.GetAsync($"/api/whatsapp/chats/{nextContactId}/forms", Ct);
            var nextRequestId = Guid.NewGuid();
            var next = request.DeepClone().AsObject(); next["requestId"] = nextRequestId.ToString();
            next["expectedRecipientContextRevision"] = (await selection.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
            await CaptureAsync(uncertain ? "send-before-uncertain" : "send-before-failed", author.PostAsJsonAsync($"/api/whatsapp/chats/{nextContactId}/form-messages", next, Ct), HttpStatusCode.Accepted);
            transport.NextResult = uncertain ? WhatsAppSendResult.Uncertain() : WhatsAppSendResult.Failed(WhatsAppSendFailure.Undeliverable);
            await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
            var outcome = await CaptureAsync(uncertain ? "outgoing-uncertain" : "outgoing-failed",
                author.GetAsync($"/api/whatsapp/chats/{nextContactId}/outgoing/{nextRequestId}", Ct), HttpStatusCode.OK);
            Assert.Equal(uncertain ? "Uncertain" : "Failed", outcome.GetProperty("state").GetString());
            await CaptureAsync(uncertain ? "thread-uncertain" : "thread-failed",
                author.GetAsync($"/api/whatsapp/chats/{nextContactId}/messages", Ct), HttpStatusCode.OK);
        }

        var output = Environment.GetEnvironmentVariable("WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
            await File.WriteAllTextAsync(Path.Combine(output, culture, "manifest.json"), JsonSerializer.Serialize(new
            {
                sourceRevision = Environment.GetEnvironmentVariable("WHATSAPP_FORMS_HTTP_SOURCE_REVISION"),
                test = nameof(Http_flow_contracts_preserve_form_presentation_across_queue_and_history),
                culture, transport = "Captured provider; PostgreSQL and Redis Testcontainers; actual HTTP bodies", captures,
            }, JsonSerializerOptions.Web), Ct);

        // C4/C5 require the same version card after reload, without depending on a live catalogue or local storage.
        Assert.Equal(versionId, pendingThread.GetProperty("outgoing")[0].GetProperty("form").GetProperty("versionId").GetGuid());
        var confirmed = Assert.Single(acceptedThread.GetProperty("messages").EnumerateArray(), message => message.GetProperty("kind").GetString() == "Flow");
        Assert.Equal(versionId, confirmed.GetProperty("form").GetProperty("versionId").GetGuid());
        if (preview is not null)
        {
            var englishContact = await CreateLinkedContactAsync(await FixtureEmployeeAsync("en"), clock);
            previewScenarios.Add(("send-en", authorId, englishContact, "en"));
            await preview.HoldAsync(factory, host, previewScenarios, clock, transport, Ct);
        }

        async Task<JsonElement> CaptureAsync(string name, Task<HttpResponseMessage> pending, HttpStatusCode status)
        {
            using var response = await pending;
            Assert.Equal(status, response.StatusCode);
            var body = await response.Content.ReadAsByteArrayAsync(Ct);
            var directory = Environment.GetEnvironmentVariable("WHATSAPP_FORMS_HTTP_CONTRACT_OUTPUT");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(directory, culture); Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, name + ".json"), body, Ct);
            }
            captures.Add(new { name, status = (int)status, sha256 = Convert.ToHexString(SHA256.HashData(body)), length = body.Length });
            using var document = JsonDocument.Parse(body); return document.RootElement.Clone();
        }
    }

    private Task<Guid> FixtureEmployeeAsync(string culture) => factory.InTransactionAsync(async services =>
    {
        var roleName = "http-form-employee-" + Guid.NewGuid().ToString("N");
        await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, ["formrecords.create"], Ct);
        var users = services.GetRequiredService<IUserRepository>();
        var user = await users.CreateAsync(Email.Create(roleName + "@example.com").Value, null, false, "Empleado de prueba", culture, Ct);
        await users.SetRolesAsync(user.Id, [roleName], Ct); return user.Id;
    });
}
