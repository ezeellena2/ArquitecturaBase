using System.Net;
using System.Net.Http.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppChatTests
{
    [Theory]
    [InlineData("available", HttpStatusCode.OK)]
    [InlineData("notInstalled", HttpStatusCode.Conflict)]
    [InlineData("notConfigured", HttpStatusCode.Conflict)]
    [InlineData("paused", HttpStatusCode.Conflict)]
    [InlineData("modelNotQualified", HttpStatusCode.Conflict)]
    public async Task Configure_alone_enables_only_an_available_assistant(string availability, HttpStatusCode status)
    {
        var (contactId, _) = await ReceiveAsync();
        var actor = await OperatorAsync(WhatsAppPermissions.ConfigureAssistant);
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAssistantOperations>();
            services.AddSingleton<IAssistantOperations>(new ModeAssistantOperations(availability));
        }));
        using var client = host.CreateClient();
        using var response = await client.SendAsync(HttpMethod.Put, $"/api/whatsapp/chats/{contactId}/mode",
            JsonContent.Create(new { mode = "Assistant", expectedRevision = 0 }), userId: actor.ToString());
        Assert.Equal(status, response.StatusCode);
        var body = await response.ReadJsonAsync();
        if (status == HttpStatusCode.OK)
        {
            Assert.Equal("Assistant", body.GetProperty("mode").GetString());
            Assert.Equal(1, body.GetProperty("modeRevision").GetInt64());
            Assert.False(body.GetProperty("assistantSendInProgress").GetBoolean());
        }
        else Assert.Equal(WhatsAppChatErrors.AssistantUnavailableCode, body.GetProperty("code").GetString());
    }
}
