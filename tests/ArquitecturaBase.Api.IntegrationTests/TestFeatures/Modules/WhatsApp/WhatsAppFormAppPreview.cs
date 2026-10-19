using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppFormAppPreview(string webRoot, string statePath, TimeSpan? lifetime = null)
{
    internal const string Origin = "https://localhost:7445";

    internal static WhatsAppFormAppPreview? FromStageFiveEnvironment(Func<string, string?>? readEnvironment = null)
    {
        readEnvironment ??= Environment.GetEnvironmentVariable;
        var webRoot = readEnvironment("STAGE5_APP_PREVIEW_WEB_ROOT");
        var statePath = readEnvironment("STAGE5_APP_PREVIEW_STATE");
        return !string.IsNullOrWhiteSpace(webRoot) && !string.IsNullOrWhiteSpace(statePath)
            ? new(webRoot, statePath, TimeSpan.FromMinutes(35)) : null;
    }

    internal static PreviewAssistantAvailability ParseAvailability(string value) => value.Trim() switch
    {
        "available" => PreviewAssistantAvailability.Available,
        "paused" => PreviewAssistantAvailability.Paused,
        "modelNotQualified" => PreviewAssistantAvailability.ModelNotQualified,
        "notConfigured" => PreviewAssistantAvailability.NotConfigured,
        "notInstalled" => PreviewAssistantAvailability.NotInstalled,
        _ => throw new InvalidOperationException("The preview availability control is invalid.")
    };

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
        object? fixtures = null, Func<CancellationToken, Task>? advance = null,
        IReadOnlyList<(string Key, Guid ActorId)>? additionalActors = null,
        Action<PreviewAssistantAvailability>? onAvailabilityChanged = null)
    {
        await host.Services.SeedDatabaseAsync(cancellationToken);
        var seeds = new List<(string Key, Guid ActorId, Guid? ContactId, string? Culture)>();
        foreach (var actor in scenarios) seeds.Add((actor.Key, actor.ActorId, actor.ContactId, actor.Culture));
        foreach (var actor in additionalActors ?? []) seeds.Add((actor.Key, actor.ActorId, null, null));
        var actorIds = seeds.Select(actor => actor.ActorId).Distinct().ToArray();
        var roster = new List<(string Key, Guid ActorId, Guid? ContactId, string? Culture, string Email, int EmailBaseline)>();
        foreach (var actor in seeds)
        {
            var email = await factory.ExecuteDbContextAsync(db => db.Users.Where(user => user.Id == actor.ActorId)
                .Select(user => user.Email).SingleAsync(cancellationToken));
            Assert.NotNull(email);
            roster.Add((actor.Key, actor.ActorId, actor.ContactId, actor.Culture, email, factory.EmailSender.CountFor(email)));
        }

        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var availabilityPath = statePath + ".availability";
        File.Delete(statePath + ".done");
        if (onAvailabilityChanged is not null) File.Delete(availabilityPath);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(lifetime ?? TimeSpan.FromMinutes(10));
        string? publishedState = null;
        try
        {
            var worker = host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>();
            while (!File.Exists(statePath + ".done"))
            {
                // Only the opt-in preview follows wall time; authentication keeps its normal lifetimes.
                if (clock is FakeTimeProvider previewClock) previewClock.AdjustTime(TimeProvider.System.GetUtcNow());
                if (onAvailabilityChanged is not null && File.Exists(availabilityPath))
                    onAvailabilityChanged(ParseAvailability(await File.ReadAllTextAsync(availabilityPath, deadline.Token)));
                var actors = new List<object>();
                foreach (var actor in roster)
                {
                    var count = factory.EmailSender.CountFor(actor.Email);
                    var code = count > actor.EmailBaseline
                        ? CapturingEmailSender.CodeOf(await factory.EmailSender.WaitForAsync(actor.Email, count))
                        : null;
                    actors.Add(new { key = actor.Key, subject = actor.ActorId, email = actor.Email, code,
                        contactId = actor.ContactId, recipientCulture = actor.Culture });
                }
                var controls = onAvailabilityChanged is null ? null : new
                {
                    assistantAvailabilityFile = availabilityPath,
                    allowedStates = Enum.GetValues<PreviewAssistantAvailability>()
                        .Select(value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString())).ToArray()
                };
                var state = JsonSerializer.Serialize(new { baseUrl = Origin, actors, fixtures, controls }, JsonSerializerOptions.Web);
                if (state != publishedState)
                {
                    await File.WriteAllTextAsync(statePath, state, cancellationToken);
                    publishedState = state;
                }
                transport.NextResult = WhatsAppSendResult.Sent("wamid.preview-" + Guid.NewGuid().ToString("N"));
                await worker.ProcessNextAsync(deadline.Token);
                if (advance is not null) await advance(deadline.Token);
                var nowUtc = clock.GetUtcNow().UtcDateTime;
                var publicationIds = await factory.ExecuteDbContextAsync(db => db.Set<WhatsAppFlowOperation>().AsNoTracking()
                    .Where(operation => actorIds.Contains(operation.ActorUserId) && !operation.Completed
                        && (operation.NextCheckAtUtc == null || operation.NextCheckAtUtc <= nowUtc))
                    .OrderBy(operation => operation.CreatedAtUtc).Select(operation => operation.NeutralOperationId).ToArrayAsync(deadline.Token));
                foreach (var operation in publicationIds)
                    await host.Services.GetRequiredService<WhatsAppFlowBackgroundService>().ProcessNextAsync(operation, deadline.Token);
                await Task.Delay(TimeSpan.FromSeconds(1), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The preview lifetime is an intentional stop, not a failure of the integration test.
        }
        finally
        {
            File.Delete(statePath);
            File.Delete(statePath + ".done");
            if (onAvailabilityChanged is not null) File.Delete(availabilityPath);
        }
    }
}

internal enum PreviewAssistantAvailability { Available, Paused, ModelNotQualified, NotConfigured, NotInstalled }
