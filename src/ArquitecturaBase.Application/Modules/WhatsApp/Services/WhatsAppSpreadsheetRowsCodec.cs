using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// VC1-A (task 29): «Por qué lo propuso» of the notice of the loads. It is kept with the ids of the forms and the names they had, and read
/// with the names of now in the reader's language. A row is in one place only: sent (the loads, in their order), for the team (past the
/// limit, not sendable, not fitting or not seen) or repeated (with the response or the earlier row); one repeated without either, or that
/// repeats a row not listed, is left out, and without a recognized form there is nothing to say.
/// </summary>
internal static class WhatsAppSpreadsheetRowsCodec
{
    private static readonly StructuredDocumentRowKind[] Recognized =
        [StructuredDocumentRowKind.Load, StructuredDocumentRowKind.NotSendable, StructuredDocumentRowKind.OverLimit, StructuredDocumentRowKind.Repeated];

    private static readonly StructuredDocumentRowKind[] ForTeam =
        [StructuredDocumentRowKind.NotSendable, StructuredDocumentRowKind.OverLimit, StructuredDocumentRowKind.Unmatched, StructuredDocumentRowKind.Unseen];

    public static string? Serialize(StructuredDocumentRowsRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var recognized = registration.Rows.Where(row => row.Form is not null && Recognized.Contains(row.Kind)).GroupBy(row => row.Form!.DefinitionId)
            .Select(group => new WhatsAppSpreadsheetFormCountRecord(group.Key, group.First().Form!.Name, group.Count())).ToArray();
        if (recognized.Length == 0) return null;
        var sent = registration.Rows.Where(row => row.Kind == StructuredDocumentRowKind.Load).OrderBy(row => row.Position).Select(Row).ToArray();
        var team = registration.Rows.Where(row => ForTeam.Contains(row.Kind)).Select(Row).ToArray();
        var repeated = registration.Rows.Where(row => row.Kind == StructuredDocumentRowKind.Repeated)
            .Select(row => row.SameAs is { } same ? new WhatsAppRepeatedRowResponse(Row(row), null, new(same.Sheet, same.Number))
                : row.Duplicate?.RecordNumber is { Length: > 0 } number ? new WhatsAppRepeatedRowResponse(Row(row), number, null) : null)
            .OfType<WhatsAppRepeatedRowResponse>().ToList();
        var listed = sent.Concat(team).Concat(repeated.Select(entry => entry.Row)).ToHashSet();
        repeated.RemoveAll(entry => entry.SameAs is { } same && !listed.Contains(same));
        return JsonSerializer.Serialize(new WhatsAppSpreadsheetRowsRecord(recognized, sent, team, repeated), JsonSerializerOptions.Web);
    }

    /// <summary>The notice's rows for the chat, its forms named as they are now, or with the name they had; null when it has none or does not read.</summary>
    public static WhatsAppSpreadsheetRowsResponse? Read(string? json, IReadOnlyDictionary<Guid, string> formNames)
    {
        ArgumentNullException.ThrowIfNull(formNames);
        if (json is null) return null;
        WhatsAppSpreadsheetRowsRecord? record;
        try { record = JsonSerializer.Deserialize<WhatsAppSpreadsheetRowsRecord>(json, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
        if (record is not { Recognized.Count: > 0 } || record.Sent is null || record.ForTeam is null || record.Repeated is null) return null;
        return new(record.Recognized.Select(form => new WhatsAppSpreadsheetFormCountResponse(
                formNames.TryGetValue(form.DefinitionId, out var current) ? current : form.Name, form.Count)).ToArray(),
            record.Sent, record.ForTeam, record.Repeated);
    }

    private static WhatsAppSheetRowResponse Row(StructuredDocumentRowRegistration row) => new(row.Sheet, row.Number);
}
