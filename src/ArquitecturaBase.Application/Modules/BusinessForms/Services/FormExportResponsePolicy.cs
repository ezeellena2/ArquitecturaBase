using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The columns of the response itself in the Excel, before its answers, with the words of the sheet: its number, when it
/// arrived in the zone of the account that exports (as the sheet shows it; what is kept stays in UTC), who loaded it, its
/// channel, its state with the words of the app and, with «Incluir las anuladas», whether it was voided (XL2).
/// </summary>
internal sealed class FormExportResponsePolicy(FormRecordOriginPolicy origins, IUserReader users)
{
    public static List<string> Headers(bool voided, string culture)
    {
        var headers = new List<string> { FormResponseTexts.Get("Number", culture), FormResponseTexts.Get("Received", culture),
            FormResponseTexts.Get("Actor", culture), FormResponseTexts.Get("Channel", culture), FormResponseTexts.Get("State", culture) };
        if (voided) headers.Add(FormResponseTexts.Get("Voided", culture));
        return headers;
    }

    /// <summary>The zone of the account's profile; UTC if it cannot be read.</summary>
    public async Task<TimeZoneInfo> ZoneAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(userId, cancellationToken);
        return account is not null && TimeZoneInfo.TryFindSystemTimeZoneById(account.TimeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;
    }

    /// <summary>The channel of each response of a batch, by its receipt: a correction arrived through the channel of its first version.</summary>
    public async Task<Dictionary<Guid, string>> ChannelsAsync(Guid actor, IReadOnlyCollection<FormResponseStoredRow> rows, CancellationToken cancellationToken)
    {
        var found = await origins.ListAsync(actor, rows.Select(row => row.OriginReceiptId).ToArray(), cancellationToken);
        return rows.ToDictionary(row => row.Record.SourceReceiptId, row => found.GetValueOrDefault(row.OriginReceiptId)?.ChannelId ?? "");
    }

    public static List<FormWorkbookCell> Cells(FormResponseStoredRow row, string number, string channel, TimeZoneInfo zone, bool voided, string culture)
    {
        var cells = new List<FormWorkbookCell> { new("text", number),
            new("dateTime", TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(row.SubmittedAtUtc, DateTimeKind.Utc), zone)),
            new("text", row.Actor.DisplayName ?? FormResponseTexts.Get("unknown", culture)),
            new("text", FormResponseTexts.Get(channel.Length == 0 ? "unknown" : channel, culture)),
            new("text", FormResponseTexts.Get("State." + row.State, culture)) };
        if (voided) cells.Add(new("text", row.State == nameof(FormRecordState.Voided) ? FormResponseTexts.Get("true", culture) : ""));
        return cells;
    }
}
