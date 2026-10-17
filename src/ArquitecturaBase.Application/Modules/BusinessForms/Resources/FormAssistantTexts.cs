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
}
