using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFormAppPreview(string webRoot, string statePath, TimeSpan? lifetime = null)
{
    internal const string Origin = "https://localhost:7445";

    internal static WhatsAppFormAppPreview? FromEnvironment(string culture, TimeSpan? lifetime = null)
    {
        var webRoot = Environment.GetEnvironmentVariable("FORM_APP_PREVIEW_WEB_ROOT");
        var statePath = Environment.GetEnvironmentVariable("FORM_APP_PREVIEW_STATE");
        return culture == "es" && !string.IsNullOrWhiteSpace(webRoot) && !string.IsNullOrWhiteSpace(statePath)
            ? new(webRoot, statePath, lifetime) : null;
    }

    internal void Configure(IWebHostBuilder builder)
    {
        builder.UseWebRoot(webRoot);
        builder.UseSetting("Authentication:Issuer", Origin + "/");
        builder.UseSetting("Authentication:Clients:Web:RedirectUris:1", Origin + "/auth/callback");
        builder.UseSetting("Authentication:Clients:Web:PostLogoutRedirectUris:1", Origin + "/login");
    }

    internal static void Listen(WebApplicationFactory<Program> host)
    {
        host.ClientOptions.BaseAddress = new Uri(Origin);
        host.ClientOptions.AllowAutoRedirect = false;
        host.UseKestrel(options => options.ListenLocalhost(7445, endpoint => endpoint.UseHttps()));
    }

    internal async Task HoldAsync(ApiFactory factory, WebApplicationFactory<Program> host,
        IReadOnlyList<(string Key, Guid ActorId, Guid ContactId, string Culture)> scenarios,
        TimeProvider clock, ControlledWhatsAppSendClient transport, CancellationToken cancellationToken,
        object? fixtures = null, Func<CancellationToken, Task>? advance = null)
    {
        await host.Services.SeedDatabaseAsync(cancellationToken);
        var sessions = new List<object>();
        foreach (var scenario in scenarios)
        {
            var email = await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Id == scenario.ActorId)
                .Select(user => user.Email).SingleAsync(cancellationToken));
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(Origin) };
            var tokens = await client.LoginAsync(factory, email!);
            sessions.Add(new { key = scenario.Key, accessToken = tokens.AccessToken, subject = scenario.ActorId,
                expiresAtUtc = clock.GetUtcNow().AddMinutes(10).UtcDateTime, contactId = scenario.ContactId,
                recipientCulture = scenario.Culture, formName = scenario.Culture == "en" ? "Form" : "Formulario" });
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(lifetime ?? TimeSpan.FromMinutes(10));
        try
        {
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(new { baseUrl = Origin, sessions, fixtures }, JsonSerializerOptions.Web), cancellationToken);
            var worker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
            while (!File.Exists(statePath + ".done"))
            {
                transport.NextResult = WhatsAppSendResult.Sent("wamid.preview-" + Guid.NewGuid().ToString("N"));
                await worker.ProcessNextAsync(deadline.Token);
                if (advance is not null) await advance(deadline.Token);
                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The preview lifetime is an intentional stop, not a failure of the integration test.
        }
        finally { File.Delete(statePath); }
    }
}
