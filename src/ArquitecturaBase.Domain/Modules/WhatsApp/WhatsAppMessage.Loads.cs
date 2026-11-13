namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// VC1-A (task 29): the notice of the loads of a spreadsheet keeps «Por qué lo propuso» from its outgoing when it goes out, as the reply of
// the agent keeps its why, so a notice already read keeps it; the retention clears it with the body.
public sealed partial class WhatsAppMessage
{
    public string? SpreadsheetRowsJson { get; private set; }

    /// <summary>Only an outbound text, once; nothing to keep is no change.</summary>
    public bool RecordSpreadsheetRows(string? rowsJson)
    {
        if (rowsJson is null) return true;
        if (Direction != WhatsAppMessageDirection.Outbound || Kind != WhatsAppMessageKind.Text || string.IsNullOrWhiteSpace(rowsJson)
            || rowsJson.Length > WhatsAppOutgoingMessage.MaxSpreadsheetRowsJsonLength) return false;
        if (SpreadsheetRowsJson is not null) return SpreadsheetRowsJson == rowsJson;
        SpreadsheetRowsJson = rowsJson;
        return true;
    }
}
