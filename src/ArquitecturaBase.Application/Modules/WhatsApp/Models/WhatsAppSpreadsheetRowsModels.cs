namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

/// <summary>
/// VC1-A (task 29): «Por qué lo propuso» of the notice of the loads of a spreadsheet, as the chat shows it to everyone with whatsapp.read: the
/// rows it recognized of each form (named in the reader's language), the ones it sent (up to three), the ones it passed to the team and the
/// repeated ones, each with the response it is already in or the row of the same spreadsheet it repeats. It names forms, rows and response
/// numbers, never a value of a response.
/// </summary>
public sealed record WhatsAppSpreadsheetRowsResponse(IReadOnlyList<WhatsAppSpreadsheetFormCountResponse> Recognized, IReadOnlyList<WhatsAppSheetRowResponse> Sent,
    IReadOnlyList<WhatsAppSheetRowResponse> ForTeam, IReadOnlyList<WhatsAppRepeatedRowResponse> Repeated);

public sealed record WhatsAppSpreadsheetFormCountResponse(string Form, int Count)
{
    public override string ToString() => "WhatsAppSpreadsheetFormCountResponse { Content = [REDACTED] }";
}

/// <summary>A row of a spreadsheet: its sheet, only when the book has more than one (so all of a notice are named or none is), and its number.</summary>
public sealed record WhatsAppSheetRowResponse(string? Sheet, int Number)
{
    public override string ToString() => "WhatsAppSheetRowResponse { Content = [REDACTED] }";
}

/// <summary>A repeated row: the number of the response it is already in, or the earlier row of the same spreadsheet it is equal to.</summary>
public sealed record WhatsAppRepeatedRowResponse(WhatsAppSheetRowResponse Row, string? RecordNumber, WhatsAppSheetRowResponse? SameAs);

/// <summary>What the notice keeps (<c>SpreadsheetRowsJson</c>): the forms by their ids, with the name they had in the person's language.</summary>
internal sealed record WhatsAppSpreadsheetRowsRecord(IReadOnlyList<WhatsAppSpreadsheetFormCountRecord> Recognized, IReadOnlyList<WhatsAppSheetRowResponse> Sent,
    IReadOnlyList<WhatsAppSheetRowResponse> ForTeam, IReadOnlyList<WhatsAppRepeatedRowResponse> Repeated);

internal sealed record WhatsAppSpreadsheetFormCountRecord(Guid DefinitionId, string Name, int Count);

/// <summary>A load of a spreadsheet: its form prepared like a document's, the body of its Flow («1 de 3 · …») and its footer, only for Meta.</summary>
internal sealed record WhatsAppAssistantSpreadsheetLoad(WhatsAppAssistantDocumentSend Send, string Body, string Footer)
{
    public override string ToString() => "WhatsAppAssistantSpreadsheetLoad { Content = [REDACTED] }";
}
