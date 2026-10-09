using System.Globalization;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal static class AiAssistantConversationTitlePolicy
{
    public static string Normalize(string question)
    {
        var normalized = string.Join(' ', question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var starts = StringInfo.ParseCombiningCharacters(normalized);
        return starts.Length <= 80 ? normalized : normalized[..starts[80]] + "…";
    }
}
