using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Fact]
    public async Task Queued_form_keeps_its_sealed_version_when_another_version_becomes_active()
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        await using var host = FormsHost(provider, cloud, source, clock);
        var collector = host.Services.GetFakeLogCollector();
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid();
        var firstVersionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, firstVersionId, 1, snapshot.Name, 1, snapshot.SubmitLabel);
        source.PublishedForms.Add(source.Form);

        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        var publicationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new(publicationId, definitionId, firstVersionId, authorId,
            ["formtemplates.publish"], "sealed-version-regression", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(), snapshot), Ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(publicationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var binding = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowBinding>()
            .SingleAsync(value => value.VersionId == firstVersionId && value.Culture == "es", Ct));
        Assert.Equal(WhatsAppFlowPublicationState.Published, binding.State);

        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var requestId = Guid.NewGuid();
        var request = new
        {
            requestId, definitionId, versionId = firstVersionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString(),
            message = "LOG_PRIVATE_FORM_BODY_" + Guid.NewGuid().ToString("N"),
        };
        using var queued = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", request, Ct);
        Assert.Equal(HttpStatusCode.Accepted, queued.StatusCode);

        // The real BusinessForms selector exposes only the active version. A later publication replaces it.
        source.Form = new(definitionId, Guid.NewGuid(), 2,
            new Dictionary<string, string> { ["es"] = "Versión nueva", ["en"] = "New version" }, 1, snapshot.SubmitLabel);
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);

        var sent = Assert.IsType<WhatsAppFlowMessageResponse>(Assert.Single(cloud.Messages));
        Assert.Equal(binding.MetaFlowId, sent.FlowId);
        using var outcome = await author.GetAsync($"/api/whatsapp/chats/{contactId}/outgoing/{requestId}", Ct);
        var receipt = await outcome.ReadJsonAsync();
        Assert.Equal("Accepted", receipt.GetProperty("state").GetString());
        Assert.Equal(firstVersionId, receipt.GetProperty("form").GetProperty("versionId").GetGuid());
        var logs = string.Join("\n", collector.GetSnapshot().Select(record => string.Join("\n", [record.Message,
            record.Exception?.ToString() ?? string.Empty,
            .. record.StructuredState?.Select(pair => pair.Value ?? string.Empty) ?? []])));
        Assert.Contains("Handling PrepareWhatsAppFlowMessage", logs, StringComparison.Ordinal);
        Assert.Contains("Handled PrepareWhatsAppFlowMessage", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(request.message, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(sent.FlowToken, logs, StringComparison.Ordinal);
    }
}
