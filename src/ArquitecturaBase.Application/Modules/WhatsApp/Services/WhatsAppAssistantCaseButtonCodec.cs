using System.Globalization;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The id of a button of a case («Preguntar con botones»): the reference that sent it and the button's place. The tap reads the
/// rest (where it leads) from what was sent, never from the id.
/// </summary>
public static class WhatsAppAssistantCaseButtonCodec
{
    public const string Prefix = "CASE_BUTTON:";

    public static string Encode(Guid requestId, int index)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(requestId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, 2);
        return Prefix + requestId.ToString("N") + ":" + index.ToString(CultureInfo.InvariantCulture);
    }

    public static bool TryParse(string? id, out Guid requestId, out int index)
    {
        requestId = Guid.Empty;
        index = -1;
        if (id is null || !id.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var parts = id[Prefix.Length..].Split(':');
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out requestId) && requestId != Guid.Empty
            && parts[1] is "0" or "1" or "2" && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
