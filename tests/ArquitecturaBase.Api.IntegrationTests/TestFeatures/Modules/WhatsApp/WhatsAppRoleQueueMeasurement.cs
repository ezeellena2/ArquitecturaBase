using System.Text.Json;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal static class WhatsAppRoleQueueMeasurement
{
    public static async Task WriteAsync(int roleRecipients, IReadOnlyList<double> dispatchMilliseconds, CancellationToken ct)
    {
        var path = Environment.GetEnvironmentVariable("WHATSAPP_ROLE_QUEUE_MEASUREMENT_OUTPUT");
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var evidence = new
        {
            roleRecipients,
            manualDispatchPosition = dispatchMilliseconds.Count,
            dispatchMilliseconds,
            backgroundPollingDelayIncluded = false,
            provider = "controlled; no Meta network",
            scope = "actual PostgreSQL/Redis, channel enqueue, manual HTTP and outgoing worker; three recipients in distinct chats",
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, JsonSerializerOptions.Web), ct);
    }
}
