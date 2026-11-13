namespace ArquitecturaBase.Domain.Modules.WhatsApp;

// VC1-A (task 29): the loads of a spreadsheet. The Flow of a load carries a footer, only for Meta (the chat draws a load as the card of its
// form, without it), and the notice of the loads carries «Por qué lo propuso»: the rows it recognized of each form, by the ids of the forms,
// which the chat names when it is read.
public sealed partial class WhatsAppOutgoingMessage
{
    /// <summary>Meta's limit for the footer of an interactive message.</summary>
    public const int MaxFooterLength = 60;

    public const int MaxSpreadsheetRowsJsonLength = 8192;

    /// <summary>The footer of the Flow of a load («Cobranzas · prellenado»); null in every other outgoing.</summary>
    public string? Footer { get; private set; }

    /// <summary>«Por qué lo propuso» of the notice of the loads of a spreadsheet; null in every other outgoing.</summary>
    public string? SpreadsheetRowsJson { get; private set; }

    /// <summary>Only a pending Flow of the assistant, once, with visible text up to Meta's limit.</summary>
    public bool AttachFooter(string footer)
    {
        if (Kind != WhatsAppOutgoingMessageKind.Flow || Origin != WhatsAppMessageOrigin.Assistant || State != WhatsAppOutgoingMessageState.Pending
            || string.IsNullOrWhiteSpace(footer) || footer.Length > MaxFooterLength || footer.Any(char.IsControl)) return false;
        if (Footer is not null) return Footer == footer;
        Footer = footer;
        return true;
    }

    /// <summary>Only a pending text of the assistant, once: a JSON object within its limit.</summary>
    public bool ExplainSpreadsheetRows(string rowsJson)
    {
        if (Kind != WhatsAppOutgoingMessageKind.Text || Origin != WhatsAppMessageOrigin.Assistant || State != WhatsAppOutgoingMessageState.Pending
            || string.IsNullOrWhiteSpace(rowsJson) || rowsJson.Length > MaxSpreadsheetRowsJsonLength || rowsJson.TrimStart()[0] != '{') return false;
        if (SpreadsheetRowsJson is not null) return SpreadsheetRowsJson == rowsJson;
        SpreadsheetRowsJson = rowsJson;
        return true;
    }
}
