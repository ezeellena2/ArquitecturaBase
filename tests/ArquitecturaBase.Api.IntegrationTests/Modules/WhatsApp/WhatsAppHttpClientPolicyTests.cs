using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed class WhatsAppHttpClientPolicyTests
{
    [Theory]
    [InlineData(WhatsAppInfrastructureRegistration.HttpClientName)]
    [InlineData(WhatsAppInfrastructureRegistration.FlowHttpClientName)]
    [InlineData("WhatsAppMedia")]
    [InlineData("WhatsAppMediaUpload")]
    public void Graph_clients_do_not_follow_redirects(string clientName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:PhoneNumberId"] = "test-phone-id",
                ["WhatsApp:AccessToken"] = "test-access-token",
                ["WhatsApp:GraphApiVersion"] = "v25.0",
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhatsAppInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(clientName);
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler ?? throw new InvalidOperationException("The HTTP handler chain is incomplete.");

        var allowAutoRedirect = handler switch
        {
            HttpClientHandler client => client.AllowAutoRedirect,
            SocketsHttpHandler sockets => sockets.AllowAutoRedirect,
            _ => throw new InvalidOperationException("The HTTP transport handler is not recognized."),
        };
        Assert.False(allowAutoRedirect);
    }
}
