using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Resources;

// The inbox's own texts (task 7): the missing form a document left unclassified by «Otro» notes, in the language of who sent it.
internal static class FormReceivedDocumentTexts
{
    private static readonly ResourceManager Texts = new("ArquitecturaBase.Application.Modules.BusinessForms.Resources.Documents", typeof(FormReceivedDocumentTexts).Assembly);

    public static string UnknownTopic(string culture) => Get("UnknownTopic", culture);

    // What the inbox's extraction asks the engine for: an interpretation is written, the document goes as its attachment.
    public static string ExtractionRequest(string culture) => Get("ExtractionRequest", culture);

    // What «Armar formulario con la IA» asks the engine for (DOC6): the documents go as the conversation's document.
    public static string FormProposalRequest(string culture) => Get("FormProposalRequest", culture);

    public static string TopicDescription(string culture, string topic) => DisplayNumbers.Format(culture, Get("TopicDescription", culture), topic);

    private static string Get(string name, string culture) => Texts.GetString(name, CultureInfo.GetCultureInfo(culture))
        ?? throw new InvalidOperationException($"The received documents text {name} is missing.");
}
