using System.Net;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppFormSendingTests
{
    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public async Task Thread_hides_form_context_without_send_and_email_without_users_read(string culture)
    {
        var clock = new FakeTimeProvider(factory.Clock.GetUtcNow());
        var provider = new CapturingFlowProvider(clock);
        var cloud = new CapturingWhatsAppCloudClient();
        var source = new CapturingStructuredFormSource
        {
            Form = new StructuredFormSelectionRow(Guid.NewGuid(), Guid.NewGuid(), 1, "FORM",
                new Dictionary<string, string> { ["es"] = "Formulario", ["en"] = "Form" }, 1,
                new Dictionary<string, string> { ["es"] = "Completar", ["en"] = "Submit" }),
        };
        await using var host = FormsHost(provider, cloud, source, clock);
        using var readOnly = await OperatorAsync(host, WhatsAppPermissions.Read);
        using var sender = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send);
        using var userReader = await OperatorAsync(host, WhatsAppPermissions.Read, WhatsAppPermissions.Send, Permissions.Users.Read);
        using var employee = await OperatorAsync(host, "formrecords.create");
        var employeeId = Guid.Parse(employee.DefaultRequestHeaders.GetValues(TestAuthHandler.UserIdHeader).Single());
        var contactId = await CreateLinkedContactAsync(employeeId, clock);

        async Task<JsonElement> ReadThreadAsync(HttpClient client, string scenario)
        {
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture);
            using var response = await client.GetAsync($"/api/whatsapp/chats/{contactId}/messages", Ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var output = Environment.GetEnvironmentVariable("WHATSAPP_FORM_PRIVACY_HTTP_OUTPUT");
            if (!string.IsNullOrWhiteSpace(output))
            {
                var directory = Path.Combine(output, culture);
                Directory.CreateDirectory(directory);
                await File.WriteAllBytesAsync(Path.Combine(directory, scenario + ".json"),
                    await response.Content.ReadAsByteArrayAsync(Ct), Ct);
            }
            return await response.ReadJsonAsync();
        }

        var readOnlyThread = (await ReadThreadAsync(readOnly, "thread-read-only")).GetProperty("contact");
        Assert.Equal(JsonValueKind.Null, readOnlyThread.GetProperty("linkedAccount").ValueKind);
        Assert.Equal(JsonValueKind.Null, readOnlyThread.GetProperty("formEligibility").ValueKind);

        var senderThread = (await ReadThreadAsync(sender, "thread-send-without-users-read")).GetProperty("contact");
        Assert.NotEqual(JsonValueKind.Null, senderThread.GetProperty("linkedAccount").ValueKind);
        Assert.True(senderThread.GetProperty("linkedAccount").GetProperty("email").ValueKind is JsonValueKind.Null);
        Assert.Equal("Eligible", senderThread.GetProperty("formEligibility").GetProperty("status").GetString());

        var userReaderThread = (await ReadThreadAsync(userReader, "thread-send-with-users-read")).GetProperty("contact");
        Assert.NotEqual(JsonValueKind.Null, userReaderThread.GetProperty("linkedAccount").GetProperty("email").ValueKind);
    }
}
