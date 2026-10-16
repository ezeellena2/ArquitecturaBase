namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>The immutable private PDF copy belonging to one action execution and culture.</summary>
public static class FormPdfStorageKey
{
    public static string Create(Guid runId, string culture)
    {
        if (runId == Guid.Empty) throw new ArgumentException("A PDF execution is required.", nameof(runId));
        if (culture is not ("es" or "en")) throw new ArgumentException("The PDF culture is unsupported.", nameof(culture));
        return "form-documents/" + runId.ToString("N") + "/" + culture + ".pdf";
    }
    public static bool TryParse(string key, out Guid runId, out string culture)
    {
        runId = Guid.Empty; culture = "";
        var parts = key.Split('/');
        if (parts.Length != 3 || parts[0] != "form-documents" || parts[2] is not ("es.pdf" or "en.pdf")
            || !Guid.TryParseExact(parts[1], "N", out var parsed) || parsed == Guid.Empty) return false;
        var parsedCulture = parts[2][..2];
        if (key != Create(parsed, parsedCulture)) return false;
        runId = parsed; culture = parsedCulture; return true;
    }
}
