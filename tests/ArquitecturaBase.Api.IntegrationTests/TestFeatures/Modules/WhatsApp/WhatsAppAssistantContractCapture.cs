namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

internal static class WhatsAppAssistantContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken ct)
    {
        var output = Environment.GetEnvironmentVariable("WHATSAPP_ASSISTANT_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"), await response.Content.ReadAsByteArrayAsync(ct), ct);
    }
}
