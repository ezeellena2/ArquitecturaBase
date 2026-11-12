using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The closed JSON of «Por qué respondió esto»: enums by name and letters as they are, so a reply's why fits its column (8 KiB on
/// the outgoing, 4 KiB on the reference). A row that does not read is shown without its why, never with a guess.
/// </summary>
internal static class WhatsAppAssistantReplyRecordCodec
{
    public const int MaxUsed = 10;
    public const int MaxUsedLength = 120;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 4,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string Serialize(WhatsAppAssistantReplyRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return JsonSerializer.Serialize(record with { Used = [.. record.Used.Take(MaxUsed).Select(Clip)] }, Options);
    }

    public static WhatsAppAssistantReplyRecord? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<WhatsAppAssistantReplyRecord>(json, Options) is { Reason: not null, Used: not null } record
                && Enum.IsDefined(record.Kind) && Enum.IsDefined(record.CaseSource) && record.Ordinal >= 1 && record.Ordinal <= record.Count
                ? record : null;
        }
        catch (JsonException) { return null; }
    }

    private static string Clip(string text) => text.Length <= MaxUsedLength ? text
        : text[..(char.IsHighSurrogate(text[MaxUsedLength - 2]) ? MaxUsedLength - 2 : MaxUsedLength - 1)] + "…";
}
