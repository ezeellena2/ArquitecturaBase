using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("Preparation", "Failed", 0)]
    [InlineData("Lease", "Failed", 0)]
    [InlineData("Provider", "Uncertain", 1)]
    public async Task A_failed_dispatch_is_uncertain_only_after_invoking_the_provider(string phase, string state, int providerCalls)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var source = new CapturingStructuredFormSource();
        var failures = new FormSendFailureAdapters(phase);
        await using var host = FormsHost(new CapturingFlowProvider(clock), new CapturingWhatsAppCloudClient(), source, clock)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IWhatsAppCloudClient>(); services.AddSingleton<IWhatsAppCloudClient>(failures);
                services.RemoveAll<IWhatsAppDispatchCoordinator>(); services.AddSingleton<IWhatsAppDispatchCoordinator>(failures);
                if (phase == "Preparation")
                {
                    services.RemoveAll<IWhatsAppFormMessageDispatchService>();
                    services.AddSingleton<IWhatsAppFormMessageDispatchService>(failures);
                }
            }));
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, versionId, 1, "FORM", snapshot.Name, snapshot.Questions.Count, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        Assert.Empty(check.Issues);
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new StructuredFormPublicationRequest(operationId, definitionId, versionId,
            authorId, ["formtemplates.publish"], "dispatch-fixture-publication", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(),
            snapshot), Ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var accepted = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Dispatch fixture",
        }, Ct);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var path = (await accepted.ReadJsonAsync()).GetProperty("recoveryPath").GetString();
        var worker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(providerCalls, failures.ProviderCalls);
        using var outcome = await author.GetAsync(path, Ct);
        Assert.Equal(HttpStatusCode.OK, outcome.StatusCode);
        Assert.Equal(state, (await outcome.ReadJsonAsync()).GetProperty("state").GetString());
        await worker.ProcessNextAsync(Ct);
        Assert.Equal(providerCalls, failures.ProviderCalls);
        using var reloaded = await author.GetAsync(path, Ct);
        Assert.Equal(state, (await reloaded.ReadJsonAsync()).GetProperty("state").GetString());
    }
}
