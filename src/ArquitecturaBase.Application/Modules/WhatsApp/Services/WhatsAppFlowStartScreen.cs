using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>The first screen of a published Flow, where a message that opens it and its interactive preview start.</summary>
internal static class WhatsAppFlowStartScreen
{
    public static string? Of(WhatsAppFlowProjection projection)
    {
        try
        {
            using var document = JsonDocument.Parse(projection.Json);
            var screen = document.RootElement.GetProperty("screens").EnumerateArray().FirstOrDefault().GetProperty("id").GetString();
            return string.IsNullOrWhiteSpace(screen) ? null : screen;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }
}
