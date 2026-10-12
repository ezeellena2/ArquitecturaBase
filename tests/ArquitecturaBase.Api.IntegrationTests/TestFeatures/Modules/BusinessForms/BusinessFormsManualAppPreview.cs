using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.BusinessForms;

internal sealed class BusinessFormsManualAppPreview(string webRoot, string statePath, TimeSpan? lifetime = null)
{
    internal const string Origin = "https://localhost:7445";
    private const string SampleUserName = "Persona ficticia K6";

    internal static BusinessFormsManualAppPreview? FromEnvironment()
    {
        var webRoot = Environment.GetEnvironmentVariable("FORMS_MANUAL_APP_PREVIEW_WEB_ROOT");
        var statePath = Environment.GetEnvironmentVariable("FORMS_MANUAL_APP_PREVIEW_STATE");
        return !string.IsNullOrWhiteSpace(webRoot) && !string.IsNullOrWhiteSpace(statePath)
            ? new(webRoot, statePath)
            : null;
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

    internal static async Task<IReadOnlyList<object>> CreateSessionsAsync(ApiFactory factory, CancellationToken cancellationToken)
    {
        var sessions = new List<object>();
        await AddSessionAsync("manager", [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage,
            BusinessFormsPermissions.Read, BusinessFormsPermissions.Manage]);
        await AddSessionAsync("reader", [BusinessFormsPermissions.CatalogsRead]);
        await AddSessionAsync("no-read", [BusinessFormsPermissions.CatalogsManage]);
        await AddSessionAsync("second-writer", [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage]);
        return sessions;

        async Task AddSessionAsync(string key, string[] permissions)
        {
            var email = TestEmails.Unique("manual-app-" + key);
            var actor = await factory.InTransactionAsync(async services =>
            {
                var roleName = "manual-app-" + key + "-" + Guid.NewGuid().ToString("N");
                await services.GetRequiredService<IRoleRepository>().CreateAsync(roleName, null, permissions, cancellationToken);
                var user = await services.GetRequiredService<IUserRepository>().CreateAsync(
                    Email.Create(email).Value, null, false, null, "es", cancellationToken);
                if (key == "manager") await services.GetRequiredService<IUserRepository>().SetDisplayNameAsync(user.Id, SampleUserName, cancellationToken);
                await services.GetRequiredService<IUserRepository>().SetRolesAsync(user.Id, [roleName], cancellationToken);
                return user;
            });

            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                BaseAddress = new Uri(Origin),
            };
            var tokens = await client.LoginAsync(factory, email);
            sessions.Add(new
            {
                key,
                accessToken = tokens.AccessToken,
                subject = actor.Id,
                expiresAtUtc = factory.Clock.GetUtcNow().AddMinutes(10).UtcDateTime,
            });
        }
    }

    internal async Task HoldAsync(IReadOnlyList<object> sessions, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.Delete(statePath + ".done");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(lifetime ?? TimeSpan.FromMinutes(10));
        try
        {
            await File.WriteAllTextAsync(statePath,
                JsonSerializer.Serialize(new { baseUrl = Origin, sampleUserName = SampleUserName, sessions }, JsonSerializerOptions.Web), cancellationToken);
            while (!File.Exists(statePath + ".done"))
                await Task.Delay(TimeSpan.FromMilliseconds(500), deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The local browser preview has a bounded lifetime.
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(statePath + ".done");
        }
    }
}
