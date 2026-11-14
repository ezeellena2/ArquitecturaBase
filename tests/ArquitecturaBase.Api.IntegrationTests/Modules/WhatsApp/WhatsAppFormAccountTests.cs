using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("WhatsApp:WabaId", "333")]
    [InlineData("WhatsApp:PhoneNumberId", "444")]
    public async Task A_published_form_cannot_be_queued_from_a_different_meta_account(string setting, string value)
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        await using var host = FormsHost(provider, cloud, source, clock);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, versionId, 1, "FORM", snapshot.Name, snapshot.Questions.Count, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        Assert.Empty(check.Issues);
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new StructuredFormPublicationRequest(operationId, definitionId, versionId,
            authorId, ["formtemplates.publish"], "account-fixture-publication", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(),
            snapshot), Ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        await using var changed = host.WithWebHostBuilder(builder => builder.UseSetting(setting, value));
        using var client = changed.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, authorId.ToString());
        using var choices = await client.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        Assert.Equal(HttpStatusCode.OK, choices.StatusCode);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();
        using var response = await client.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Account fixture",
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(FlowErrors.AccountMismatch.Code, (await response.ReadJsonAsync()).GetProperty("code").GetString());
        using var thread = await client.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        Assert.Empty((await thread.ReadJsonAsync()).GetProperty("outgoing").EnumerateArray());
        Assert.Empty(cloud.Messages);
    }

    [Fact]
    public async Task Sending_a_form_does_not_ask_meta_for_the_account_and_only_the_dispatch_verifies_it()
    {
        Assert.SkipUnless(factory.Services.GetService<IStructuredFormPublicationDriver>() is not null,
            "The optional structured form publisher must be installed for this composition test.");
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource();
        await using var host = FormsHost(provider, cloud, source, clock);
        using var author = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, "formtemplates.publish");
        using var employee = await OperatorAsync(host, "formrecords.create");
        var authorId = Guid.Parse(author.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var definitionId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var snapshot = Snapshot();
        source.Form = new(definitionId, versionId, 1, "FORM", snapshot.Name, snapshot.Questions.Count, snapshot.SubmitLabel);
        var channel = Assert.Single(host.Services.GetServices<IStructuredFormChannel>(), item => item.ChannelId == "phone");
        var check = (await channel.CheckAsync(snapshot, Ct)).Value;
        var operationId = Guid.NewGuid();
        Assert.True((await channel.AcceptAsync(new StructuredFormPublicationRequest(operationId, definitionId, versionId,
            authorId, ["formtemplates.publish"], "account-lookup-fixture", check.ProfileRevision,
            check.Cultures.Select(item => new StructuredFormPublicationCulture(item.Culture, item.CompilationFingerprint)).ToArray(),
            snapshot), Ct)).IsSuccess);
        for (var step = 0; step < 12; step++)
        {
            await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operationId, Ct);
            clock.Advance(TimeSpan.FromSeconds(16));
        }
        var contactId = await CreateLinkedContactAsync(employeeId, clock);
        using var choices = await author.GetAsync($"/api/whatsapp/chats/{contactId}/forms", Ct);
        var context = (await choices.ReadJsonAsync()).GetProperty("recipientContextRevision").GetString();

        // H33: Meta's lookup of the account's numbers is down, and «Enviar» neither waits for it nor fails.
        provider.MembershipUnavailable = true;
        var lookups = provider.MembershipLookups;
        using var response = await author.PostAsJsonAsync($"/api/whatsapp/chats/{contactId}/form-messages", new
        {
            requestId = Guid.NewGuid(), definitionId, versionId, expectedModeRevision = 0,
            expectedRecipientContextRevision = context, message = "Account lookup fixture",
        }, Ct);

        Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(lookups, provider.MembershipLookups);
        // Only the dispatch asks Meta, as documented, and with the lookup down nothing goes out.
        await host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        Assert.Equal(lookups + 1, provider.MembershipLookups);
        Assert.Empty(cloud.Messages);
        var path = (await response.ReadJsonAsync()).GetProperty("recoveryPath").GetString();
        using var outcome = await author.GetAsync(path, Ct);
        Assert.Equal("Failed", (await outcome.ReadJsonAsync()).GetProperty("state").GetString());
    }
}
