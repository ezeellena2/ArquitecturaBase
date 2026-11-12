namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;

/// <summary>
/// Casos del asistente: the bytes of the screen «Asistente» and of Probar, intact, only with WHATSAPP_ASSISTANT_AGENT_HTTP_OUTPUT
/// (cohort whatsapp-assistant-agent-http). It never completes or rewrites a body.
/// </summary>
internal static class WhatsAppAssistantAgentContractCapture
{
    public static async Task WriteAsync(HttpResponseMessage response, string name, CancellationToken ct)
    {
        var output = Environment.GetEnvironmentVariable("WHATSAPP_ASSISTANT_AGENT_HTTP_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, name + ".json"), await response.Content.ReadAsByteArrayAsync(ct), ct);
    }
}
