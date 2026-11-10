using System.Globalization;
using System.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Resources;

internal static class FormAssistantTexts
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Assistant", typeof(FormAssistantTexts).Assembly);
    public static string SuggestedAfterResponse(string culture, string number) => string.Format(CultureInfo.GetCultureInfo(culture),
        Texts.GetString("SuggestedAfterResponse", CultureInfo.GetCultureInfo(culture))
            ?? throw new InvalidOperationException("The form suggestion reason is missing."), number);
    public static string ClarificationSelectedReason(string culture) => Texts.GetString("ClarificationSelectedReason", CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("The form clarification selection reason is missing.");
    public static string ClarificationQuestion(string culture) => Texts.GetString("ClarificationQuestion", CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException("The form clarification question is missing.");
    public static string AuthoringQuestionType(string culture, string type, string? catalog) =>
        Format(culture, "Authoring.Type." + type, catalog ?? "");
    public static string AuthoringCatalogCode(string culture, string code) => Format(culture, "Authoring.Catalog.Code", code);
    public static string AuthoringCatalogOption(string culture, string option) => Format(culture, "Authoring.Catalog.Option", option);
    public static string AuthoringCatalogFields(string culture, int count, string names) =>
        Format(culture, count == 1 ? "Authoring.Catalog.Field" : "Authoring.Catalog.Fields", names);
    public static string AuthoringPair(string culture, string first, string last) => Format(culture, "Authoring.Pair", first, last);
    // IA, Stage 2: what a column of the spreadsheet fills in an import («el campo Rubro de la columna C (Rubro)»).
    public static string AuthoringImportColumn(string culture, string target, string? field, string letter, string? header)
    {
        var filled = target switch
        {
            "codeAndOption" => Format(culture, "Authoring.Import.CodeAndOption"),
            "code" => Format(culture, "Authoring.Import.Code"),
            "option" => Format(culture, "Authoring.Import.Option"),
            "value" => Format(culture, "Authoring.Import.Value"),
            "detail" => Format(culture, "Authoring.Import.Detail"),
            _ => Format(culture, "Authoring.Import.Field", field ?? ""),
        };
        return header is null ? Format(culture, "Authoring.Import.Column", filled, letter) : Format(culture, "Authoring.Import.HeadedColumn", filled, letter, header);
    }
    private static string Format(string culture, string name, params object[] values) => string.Format(CultureInfo.GetCultureInfo(culture),
        Texts.GetString(name, CultureInfo.GetCultureInfo(culture)) ?? throw new InvalidOperationException($"The authoring text {name} is missing."), values);
}
