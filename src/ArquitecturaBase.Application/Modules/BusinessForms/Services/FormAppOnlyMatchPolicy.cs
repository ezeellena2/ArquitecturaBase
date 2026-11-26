using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// O55: the form completed only in the app that a request for a missing form names, by the words of its name in any of its languages:
/// the same words, or the shorter name inside the other when it has two words or more («cargar el pedido de prueba» names «Pedido de
/// prueba»). Only one form may match: with none, or with several, the request stays a missing form.
/// </summary>
internal static class FormAppOnlyMatchPolicy
{
    // The words that never tell two names apart.
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        "de", "del", "la", "las", "el", "los", "lo", "un", "una", "unos", "unas", "a", "al", "y", "e", "o", "u", "para", "por", "en", "con",
        "the", "of", "for", "and", "an", "to", "in", "on", "with",
    };

    /// <summary>The form with its name in the person's language (or its original one) and its page to load it in the app.</summary>
    public static StructuredAppOnlyForm? Find(string requested, IReadOnlyList<FormAppOnlyRow> forms, string culture)
    {
        var words = FormNameWords.Of(requested, Ignored);
        if (words.Count == 0) return null;
        var matches = forms.Select(form => (Form: form, Names: FormDraftPolicy.Read<Dictionary<string, string>>(form.NameJson)))
            .Where(item => item.Names.Values.Any(name => Matches(words, FormNameWords.Of(name, Ignored)))).Take(2).ToArray();
        return matches is [var match]
            ? new(match.Form.DefinitionId, FormAssistantCandidatePolicy.Text(match.Names, culture, match.Form.Culture),
                "/formularios/" + match.Form.DefinitionId.ToString("D") + "/cargar")
            : null;
    }

    private static bool Matches(HashSet<string> requested, HashSet<string> name) => name.Count > 0
        && (requested.SetEquals(name) || Math.Min(requested.Count, name.Count) >= 2 && (requested.IsSubsetOf(name) || name.IsSubsetOf(requested)));
}
