using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;
using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «¿La emito?» (EM2-A): the summary of an issuance for whoever loaded the response, in their language, with the document the
/// issuer decided, the receiver and the total, and the two choices. «No emitir» voids the response with «No se emitió». A summary
/// that failed (decision 4) says why, with «Volver a probar» and «No emitir».
/// </summary>
internal static class FormIssueConfirmationPolicy
{
    public const string Issue = "issue", Skip = "skip", Retry = "retry";

    // As the boards EM2-A and IV3: the document, the receiver, the total with the amounts the document itemizes between
    // parentheses (the net and each tax of an invoice A, the VAT an invoice B contains), the date (dd/MM/yyyy, MM/dd/yyyy in
    // English) and the question last.
    public static string Body(StructuredIssuePreview preview, string culture)
    {
        var document = preview.Document?.GetValueOrDefault(culture) ?? preview.Document?.Values.FirstOrDefault() ?? "";
        var date = preview.Date?.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture);
        var amounts = (preview.Amounts ?? []).Select(item => (item.Label.GetValueOrDefault(culture) ?? item.Label.Values.First()) + ": $ " + Money(item.Value, culture)).ToArray();
        var total = Money(preview.Total, culture) + (amounts.Length == 0 ? "" : " (" + string.Join("; ", amounts) + ")");
        var summary = DisplayNumbers.Format(culture, FormResponseTexts.Get(date is null ? "Issue.Confirm.Body" : "Issue.Confirm.BodyDated", culture),
            document, preview.Receiver ?? "", total, date);
        return summary + "\n" + FormResponseTexts.Get("Issue.Confirm.Question", culture);
    }

    /// <summary>A summary that failed: «Todavía no puedo emitirla: » and the reason of the issuer, both in the language of the person.</summary>
    public static string Failed(StructuredIssuePreview preview, string culture)
    {
        var code = preview.FailureCode ?? throw new InvalidOperationException("The summary did not fail.");
        var reason = ErrorMessages.ResourceManager.GetString(code, CultureInfo.GetCultureInfo(culture)) ?? code;
        return DisplayNumbers.Format(culture, FormResponseTexts.Get("Issue.Failed.Body", culture), reason);
    }

    private static string Money(string? value, string culture) =>
        decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            ? DisplayNumbers.Number(amount, culture, 2, 2) : value ?? "";

    /// <summary>
    /// «Emitir» and «No emitir», or «Volver a probar» and «No emitir» after a summary that failed. A message that does not go by
    /// its run (one after «Volver a probar») names the run in each button, because the channel hands back the id of the message.
    /// </summary>
    public static IReadOnlyList<StructuredRecordActionChoice> Choices(bool failed, string culture, Guid? runId)
    {
        var suffix = runId is { } run ? "." + Base64Url.EncodeToString(run.ToByteArray()) : "";
        return [new((failed ? Retry : Issue) + suffix, FormResponseTexts.Get(failed ? "Issue.Confirm.Retry" : "Issue.Confirm.Issue", culture)),
            new(Skip + suffix, FormResponseTexts.Get("Issue.Confirm.Skip", culture))];
    }

    /// <summary>
    /// The id of a message of «¿La emito?» for the channel, which keeps one per id and person: the run for the first one, and for
    /// the one after each «Volver a probar», an id derived from the run and the revision of that decision, so a run that lost its
    /// lease and asks again repeats it instead of sending another.
    /// </summary>
    public static Guid MessageId(Guid runId, long? decisionRevision) => decisionRevision is not { } revision ? runId
        : new(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"issue-confirmation:{runId:N}:{revision}"))).AsSpan(0, 16));

    /// <summary>
    /// The id of the notice that the issuance waits for its provider («ARCA no responde»), one per run: its own, because the run
    /// may already be the id of the «¿La emito?» of a response from a channel, and always the same, so it goes once.
    /// </summary>
    public static Guid NoticeId(Guid runId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"issue-notice:{runId:N}"))).AsSpan(0, 16));

    /// <summary>A button pressed: its choice and the run, the one it names or else the message it came from.</summary>
    public static bool TryReadChoice(Guid messageId, string choiceId, out Guid runId, out string choice)
    {
        var parts = choiceId.Split('.');
        runId = messageId;
        choice = parts[0];
        if (choice is not (Issue or Skip or Retry) || parts.Length > 2) return false;
        if (parts.Length == 1) return true;
        Span<byte> bytes = stackalloc byte[16];
        if (!Base64Url.IsValid(parts[1], out var length) || length != 16 || !Base64Url.TryDecodeFromChars(parts[1], bytes, out _)) return false;
        runId = new Guid(bytes);
        return true;
    }


    /// <summary>What a channel answers after «No emitir»: «No la emitimos. FAC-000012 quedó anulada.», the response named as the app names it.</summary>
    public static string Voided(string prefix, long number, string culture) =>
        string.Format(CultureInfo.InvariantCulture, FormResponseTexts.Get("Issue.Voided", culture), prefix + "-" + number.ToString("D6", CultureInfo.InvariantCulture));
}
