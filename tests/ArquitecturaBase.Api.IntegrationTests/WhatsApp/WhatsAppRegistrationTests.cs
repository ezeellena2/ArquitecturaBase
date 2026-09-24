using System.Net;
using System.Text;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Api.IntegrationTests.WhatsApp;

/// <summary>
/// El interruptor es <c>WhatsApp:PhoneNumberId</c>, como el ClientId de Google: sin él, WhatsApp queda apagado y la
/// Api arranca igual; con él, lo que falte o esté mal frena el arranque (sección 14 del spec).
/// </summary>
[Collection(ApiTestGroup.Name)]
public sealed class WhatsAppRegistrationTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_test_api_runs_with_whatsapp_on_and_keeps_the_messages_in_memory()
    {
        Assert.True(factory.Services.GetRequiredService<IWhatsAppAvailability>().IsEnabled);
        Assert.True(factory.Services.GetRequiredService<IWhatsAppAvailability>().IsWebhookEnabled);
        Assert.Same(factory.WhatsApp, factory.Services.GetRequiredService<IWhatsAppOutbox>());
    }

    [Fact]
    public async Task Readiness_includes_whatsapp_and_liveness_does_not()
    {
        var healthChecks = factory.Services.GetRequiredService<HealthCheckService>();

        var ready = await healthChecks.CheckHealthAsync(Ct);
        var live = await healthChecks.CheckHealthAsync(registration => registration.Tags.Contains("live"), Ct);

        Assert.Equal(HealthStatus.Healthy, ready.Entries[WhatsAppHealthCheck.Name].Status);
        Assert.DoesNotContain(WhatsAppHealthCheck.Name, live.Entries.Keys);
    }

    [Fact]
    public async Task Without_a_phone_number_id_whatsapp_is_off_and_the_api_starts()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:PhoneNumberId", ""));
        using var client = api.CreateClient();

        using var response = await client.GetAsync("/health", Ct);
        var health = await api.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(api.Services.GetRequiredService<IWhatsAppAvailability>().IsEnabled);
        Assert.DoesNotContain(api.Services.GetServices<IHostedService>(), service => service is WhatsAppSenderBackgroundService);
        Assert.Equal(HealthStatus.Healthy, health.Entries[WhatsAppHealthCheck.Name].Status);
        Assert.Equal("disabled", health.Entries[WhatsAppHealthCheck.Name].Description);
    }

    /// <summary>Apagado, igual hay un outbox: Application nunca recibe un null. No encola nada y lo dice.</summary>
    [Fact]
    public void Without_a_phone_number_id_the_outbox_drops_every_message()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WhatsApp:GraphApiVersion"] = "v25.0" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhatsApp(configuration);
        using var provider = services.BuildServiceProvider();

        var outbox = provider.GetRequiredService<IWhatsAppOutbox>();

        Assert.False(provider.GetRequiredService<IWhatsAppAvailability>().IsEnabled);
        Assert.False(outbox.TryEnqueue(new WhatsAppTextMessage(TestPhones.Unique(), "Hola")));
    }

    /// <summary>El mensaje se basta solo: trae el comando entero, listo para copiar, sin mandar a buscarlo a otro lado.</summary>
    [Fact]
    public async Task Api_does_not_start_with_a_phone_number_id_and_no_access_token_and_explains_how_to_load_it()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("WhatsApp:AccessToken", ""));

        var messages = StartupFailureMessages(api);

        Assert.Contains(messages, message => message.Contains(
            """dotnet user-secrets set "WhatsApp:AccessToken" "<token>" --project src/ArquitecturaBase.Api""",
            StringComparison.Ordinal));
    }

    /// <summary>
    /// El webhook necesita sus dos secretos (sección 14 del spec). Sin ninguno, que es como queda el desarrollo hasta que
    /// se configura el túnel, la Api arranca, el envío funciona y las rutas del webhook no existen: responden el 404 del
    /// framework, no el index.html del SPA. Al arrancar queda un aviso que dice qué falta, sin ningún valor.
    /// </summary>
    [Fact]
    public async Task Without_the_webhook_secrets_the_webhook_is_off_and_the_api_starts()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("WhatsApp:AppSecret", "")
            .UseSetting("WhatsApp:VerifyToken", "")
            .ConfigureLogging(logging => logging.AddFakeLogging()));
        using var client = api.CreateClient();

        using var verification = await client.GetAsync(
            new Uri("/webhooks/whatsapp?hub.mode=subscribe&hub.verify_token=x&hub.challenge=1", UriKind.Relative), Ct);
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var post = await client.PostAsync(new Uri("/webhooks/whatsapp", UriKind.Relative), content, Ct);
        var problem = await verification.ReadJsonAsync();
        var availability = api.Services.GetRequiredService<IWhatsAppAvailability>();
        var notices = api.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Category == typeof(WhatsAppWebhookOffNotice).FullName)
            .ToList();

        Assert.Equal(HttpStatusCode.NotFound, verification.StatusCode);
        Assert.Equal("Http.NotFound", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.True(availability.IsEnabled);
        Assert.False(availability.IsWebhookEnabled);
        Assert.Contains(api.Services.GetServices<IHostedService>(), service => service is WhatsAppSenderBackgroundService);

        var notice = Assert.Single(notices);
        Assert.Equal(LogLevel.Warning, notice.Level);
        Assert.Contains("WhatsApp:AppSecret", notice.Message, StringComparison.Ordinal);
        Assert.Contains("WhatsApp:VerifyToken", notice.Message, StringComparison.Ordinal);
    }

    /// <summary>Con uno solo de los dos secretos, lo que falta es un error de configuración: la Api no arranca y dice cómo cargarlo.</summary>
    [Theory]
    [InlineData("WhatsApp:AppSecret")]
    [InlineData("WhatsApp:VerifyToken")]
    public async Task Api_does_not_start_with_only_one_webhook_secret_and_explains_how_to_load_the_other(string missing)
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting(missing, ""));

        var messages = StartupFailureMessages(api);

        Assert.Contains(messages, message =>
            message.Contains($"dotnet user-secrets set \"{missing}\"", StringComparison.Ordinal)
            && message.Contains("--project src/ArquitecturaBase.Api", StringComparison.Ordinal));
        Assert.DoesNotContain(messages, message =>
            message.Contains(ApiFactory.WhatsAppAppSecret, StringComparison.Ordinal)
            || message.Contains(ApiFactory.WhatsAppVerifyToken, StringComparison.Ordinal));
    }

    /// <summary>
    /// El bot contesta con enlaces a la web, y la dirección de la web es el origen público (<c>Authentication:Issuer</c>).
    /// Con el webhook prendido y sin esa dirección, el primer mensaje fallaría recién al contestarlo: la Api no arranca y
    /// dice qué falta.
    /// </summary>
    [Fact]
    public async Task Api_does_not_start_with_the_webhook_on_and_no_public_origin_and_says_what_is_missing()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder.UseSetting("Authentication:Issuer", ""));

        var messages = StartupFailureMessages(api);

        Assert.Contains(messages, message =>
            message.Contains("Missing Authentication:Issuer", StringComparison.Ordinal)
            && message.Contains("webhook", StringComparison.Ordinal));
    }

    /// <summary>Sin el webhook no hay bot, y el ingreso con código no necesita el origen público: la Api arranca igual.</summary>
    [Fact]
    public async Task Without_the_webhook_the_api_starts_without_a_public_origin()
    {
        await using var api = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Authentication:Issuer", "")
            .UseSetting("WhatsApp:AppSecret", "")
            .UseSetting("WhatsApp:VerifyToken", ""));
        using var client = api.CreateClient();

        using var response = await client.GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(api.Services.GetServices<IHostedService>(), service => service is WhatsAppInboundProcessor);
    }

    [Theory]
    [InlineData("WhatsApp:GraphApiVersion", "25.0")]
    [InlineData("WhatsApp:GraphApiVersion", "v25")]
    [InlineData("WhatsApp:Templates:LoginCode", "")]
    [InlineData("WhatsApp:Templates:Invitation", "")]

    // Meta no acepta otro mensaje a la misma persona antes de los 6 segundos (131056): esperar menos es perder el
    // intento. Con el valor por defecto, 6, arrancan todos los tests.
    [InlineData("WhatsApp:RetryDelaySeconds", "0")]
    [InlineData("WhatsApp:RetryDelaySeconds", "5")]
    [InlineData("WhatsApp:InboundPollSeconds", "0")]
    public void WhatsApp_registration_rejects_invalid_settings_at_startup_validation(string key, string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsApp:PhoneNumberId"] = ApiFactory.WhatsAppPhoneNumberId,
                ["WhatsApp:AccessToken"] = "test-access-token",
                [key] = value,
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWhatsApp(configuration);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains(key, StringComparison.Ordinal));
    }

    /// <summary>
    /// ServiceDefaults le pone la resiliencia estándar a todos los HttpClient, y esa reintenta un 500. Un reintento de
    /// un POST a Meta puede duplicar el mensaje: este cliente no reintenta, reintenta la cola (sección 9 del spec).
    /// </summary>
    [Fact]
    public async Task The_registered_client_does_not_retry_a_post_that_fails()
    {
        var handler = new FakeMetaHandler(_ => FakeMetaHandler.Error(HttpStatusCode.InternalServerError, 131000));
        await using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            // Con el reloj real: con el de los tests, la espera de un reintento no terminaría nunca.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton(TimeProvider.System);
            services.AddHttpClient(WhatsAppRegistration.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        }));

        var client = api.Services.GetRequiredService<IWhatsAppCloudClient>();
        var result = await client.SendAsync(new WhatsAppTextMessage(TestPhones.Unique(), "Hola"), Ct);

        Assert.Equal(WhatsAppSendFailure.Transient, result.Failure);
        Assert.Equal(1, handler.Calls);
    }

    private static List<string> StartupFailureMessages(WebApplicationFactory<Program> api)
    {
        var exception = Assert.ThrowsAny<Exception>(() => api.Services);

        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }
}
