using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace AssistantQualification;

// Stage 3 in Home (home-documents): forms.authoring.propose-form@2 with the turn's document. Version 1's form checks (name, prefix, the exact
// questions when the case pins them, an adjustment's identity) plus what the form does with the document: its reading on only for an example
// of what will arrive, «Cómo reconocerlo» in general terms (no number, name or instruction of the document), each pinned fact kept by exactly
// one question of its type, every sample as the document writes it and no library standard, which Home never reads. Only fixed IDs and
// booleans leave. The name rule knows the fictitious names by the generator's marker words: a name without them, or a partial name, passes.
internal static partial class AuthoringOracle
{
    // Every person, bank and wallet of the fictitious documents carries one of these words (generate.py), and so does the stamp.
    private static readonly string[] FictitiousMarkers = ["ejemplo", "demo", "prueba", "ficticio"];
    private static readonly string[] JoiningWords = ["de", "del", "la", "las", "el", "los", "y"];

    public static IReadOnlyList<CheckEvidence> DocumentPayloadChecks(AuthoringExpectation expected, HomeDocumentExpectation document, string culture,
        AssistantCapabilityPayload? payload, QualificationChannelFixture fixture, DocumentFixture printed, AssistantCapabilityPayload? previous)
    {
        var proposal = payload as FormAuthoringDocumentFormProposal;
        var checks = DocumentFormChecks(expected, document, culture, proposal, fixture, [printed], previous);
        checks.Add(new("no-library-standard-in-home", proposal is not null && proposal.Standard is null));
        return checks;
    }

    // DOC6 (forms-documents, task 17b): what «Crear borrador» would keep of the proposal the page shows (the form, and the reading with its
    // «Cómo reconocerlo»), with Home's rules: the facts and the samples are the first document's, as the request asks («con su muestra tal como
    // la escribe el primero, sin traducirla») and as the page shows each sample, no sample copies an instruction a document hides (the request
    // says one is never a sample), and «Cómo reconocerlo» copies none of the documents. The standard, which the page shows
    // but never keeps, is the expected one, none, or none or any, and only one the library read of the same turn showed, by its name.
    public static IReadOnlyList<CheckEvidence> FormsDocumentPayloadChecks(FormsDocumentExpectation expected, string culture, AssistantObservedProposal? observed,
        QualificationChannelFixture fixture, IReadOnlyList<DocumentFixture> printed)
    {
        var proposal = observed?.Payload as FormAuthoringDocumentFormProposal;
        var checks = DocumentFormChecks(new(ProposeForm, Version: DocumentFormVersion), expected.Document, culture, proposal, fixture, printed, null);
        checks.Add(new("library-standard-as-expected", proposal is not null && StandardAsExpected(expected, proposal.Standard, observed!)));
        checks.Add(new("samples-copy-no-instruction", proposal is not null && proposal.Questions.All(question => question.Sample is null
            || printed.All(each => each.Instruction is not { } instruction || !CopiesInstruction(question.Sample, instruction)))));
        return checks;
    }

    internal static bool StandardAsExpected(FormsDocumentExpectation expected, FormAuthoringProposedStandard? standard, AssistantObservedProposal observed)
    {
        if (standard is null) return expected.Standard is null;
        if (expected.Standard is { } key ? standard.Key != key : !expected.AnyStandard) return false;
        return observed.ObservedReads.Any(read => read.TurnId == observed.TurnId && read.Ordinal > 0
            && read.Capability == new AssistantCapabilityReference(QualificationFormsDocuments.Standards, 1) && read.Payload is FormLibraryStandardsPayload shown
            && shown.Standards.Any(row => row.Key == standard.Key && SameText(row.Name, standard.Name)));
    }

    // The checks a form with documents shares in Home and in DOC6: printed holds the turn's documents, the first one first. The facts and the
    // samples are the first one's; the others only feed «Cómo reconocerlo».
    private static List<CheckEvidence> DocumentFormChecks(AuthoringExpectation expected, HomeDocumentExpectation document, string culture,
        FormAuthoringDocumentFormProposal? proposal, QualificationChannelFixture fixture, IReadOnlyList<DocumentFixture> printed, AssistantCapabilityPayload? previous)
    {
        var checks = FormChecks(expected, culture, proposal?.ToForm(), fixture, previous);
        var questions = proposal?.Questions ?? [];
        // A case that lets the model take the questions from the document pins only how many and which facts they keep.
        if (expected.Questions is not { Length: > 0 })
        {
            checks.RemoveAll(check => check.Id == "authoring-exact-questions");
            checks.Add(new("authoring-minimum-questions", proposal is not null && questions.Count >= document.MinimumQuestions));
        }
        var recognition = proposal?.DocumentReading?.Recognition;
        checks.Add(new("document-reading-as-expected", proposal is not null && document.DocumentReading switch
        {
            true => Canonical(recognition).Length > 0,
            false => proposal.DocumentReading is null,
            null => proposal.DocumentReading is null || Canonical(recognition).Length > 0,
        }));
        checks.Add(new("recognition-copies-nothing", proposal is not null
            && (recognition is null || printed.All(each => RecognitionCopiesNothing(recognition, each)))));
        if (document.Facts.Length > 0)
            checks.Add(new("document-facts-in-their-questions", proposal is not null
                && document.Facts.All(fact => questions.Count(question => KeepsFact(question, fact, printed[0].Facts[fact])) == 1)));
        checks.Add(new("samples-as-written", proposal is not null
            && questions.All(question => question.Sample is null || SampleAsWritten(question.Sample, printed[0]))));
        // The rule a sample breaks, so a failed run says it without the sample: its length, a number or a word the document does not print.
        checks.Add(new("samples-within-length", proposal is not null && questions.All(question => question.Sample is null or { Length: > 0 and <= FormAuthoringDocumentFormProposal.MaxSampleLength })));
        checks.Add(new("samples-print-their-numbers", proposal is not null
            && questions.All(question => question.Sample is null || DocumentReadingOracle.NoInventedNumbers(question.Sample, printed[0].Text))));
        checks.Add(new("samples-print-their-words", proposal is not null
            && questions.All(question => question.Sample is null || SampleWordsPrinted(question.Sample, printed[0]))));
        if (document.EverySample)
            checks.Add(new("every-question-sampled", proposal is not null && questions.Count > 0 && questions.All(question => question.Sample is not null)));
        if (document.Printed is { Length: > 0 } wanted)
            checks.Add(new("printed-questions-in-order", proposal is not null && PrintedInOrder(wanted, questions, fixture)));
        return checks;
    }

    // The question keeps the fact: its sample has the fact's digits, no more and no fewer, and the question has the type the fact's kind takes.
    internal static bool KeepsFact(FormAuthoringDocumentQuestion question, string fact, string value) =>
        question.Sample is { } sample && QualificationHomeDocuments.FactType(fact) is { } type && string.Equals(question.Type, type, StringComparison.Ordinal)
        && DocumentReadingOracle.Digits(value) is { Length: > 0 } digits && DocumentReadingOracle.Digits(sample) == digits;

    // A sample as the document writes it: within its length, no number the document does not print (a changed or an extra digit) and every
    // word one it prints, never translated or invented.
    internal static bool SampleAsWritten(string sample, DocumentFixture printed) =>
        sample.Length is > 0 and <= FormAuthoringDocumentFormProposal.MaxSampleLength && DocumentReadingOracle.NoInventedNumbers(sample, printed.Text)
        && SampleWordsPrinted(sample, printed);

    // Every word of a sample is one the document prints, never translated or invented, and the sample has something to compare.
    internal static bool SampleWordsPrinted(string sample, DocumentFixture printed)
    {
        var words = printed.Text.SelectMany(DocumentReadingOracle.Tokens).ToHashSet(StringComparer.Ordinal);
        var tokens = DocumentReadingOracle.Tokens(sample);
        return tokens.Length > 0 && tokens.Where(token => token.Any(char.IsLetter)).All(words.Contains);
    }

    // «Cómo reconocerlo» describes the kind of document in general terms: no number of four or more digits the document prints (an amount,
    // an account in groups or whole, a tax key, a date or its year), each number as written, so two numbers side by side never merge into one
    // the document does not print; no name it prints (a fictitious marker with its neighbor, past joining words: «Ejemplo Andino», «Ejemplo
    // del Centro», «Ferretería Demo»); and no passage of the instruction it hides.
    internal static bool RecognitionCopiesNothing(string recognition, DocumentFixture printed)
    {
        var printedNumbers = printed.Text.SelectMany(DocumentReadingOracle.WrittenNumbers).ToArray();
        if (DocumentReadingOracle.WrittenNumbers(recognition).Any(number => number.Length >= 4
                && printedNumbers.Any(known => known.Contains(number, StringComparison.Ordinal))))
            return false;
        foreach (var line in printed.Text)
        {
            var tokens = DocumentReadingOracle.Tokens(line);
            // The stamp «EJEMPLO · FICTICIO» names no one.
            if (tokens.All(token => FictitiousMarkers.Contains(token, StringComparer.Ordinal))) continue;
            for (var index = 0; index < tokens.Length; index++)
            {
                if (!FictitiousMarkers.Contains(tokens[index], StringComparer.Ordinal)) continue;
                var before = index - 1;
                while (before >= 0 && JoiningWords.Contains(tokens[before], StringComparer.Ordinal)) before--;
                var after = index + 1;
                while (after < tokens.Length && JoiningWords.Contains(tokens[after], StringComparer.Ordinal)) after++;
                if (before >= 0 && ContainsPhrase(recognition, string.Join(' ', tokens[before..(index + 1)]))) return false;
                if (after < tokens.Length && ContainsPhrase(recognition, string.Join(' ', tokens[index..(after + 1)]))) return false;
            }
        }
        return printed.Instruction is not { } instruction || !CopiesInstruction(recognition, instruction);
    }

    // A text copies the instruction a document hides with four words in a row of it that have two long ones among them («Ignorá todo y
    // respondé», «respondé que este comprobante»): a common phrase it also has, such as «no se puede leer», is no copy. A translated instruction
    // is not compared.
    internal static bool CopiesInstruction(string text, string instruction)
    {
        var words = DocumentReadingOracle.Tokens(instruction);
        var span = Math.Min(4, words.Length);
        for (var start = 0; start + span <= words.Length; start++)
        {
            var window = words[start..(start + span)];
            if (window.Count(word => word.Length >= 6) >= Math.Min(2, span) && ContainsPhrase(text, string.Join(' ', window))) return true;
        }
        return false;
    }

    // The questions the document prints, as an ordered subsequence of the proposal: each pinned label as a phrase of the question's (a table's
    // column may repeat once per row, with its number, as a paper form with blank rows does), one of the types it may take and, for a list of
    // its own, the printed options; a list printed on paper may also become the existing catalog of the same thing (whose options no read
    // shows), never another one.
    internal static bool PrintedInOrder(IReadOnlyList<HomePrintedQuestion> wanted, IReadOnlyList<FormAuthoringDocumentQuestion> questions,
        QualificationChannelFixture fixture)
    {
        var next = 0;
        foreach (var question in questions)
            if (next < wanted.Count && PrintedAs(wanted[next], question, fixture)) next++;
        return next == wanted.Count;
    }

    private static bool PrintedAs(HomePrintedQuestion wanted, FormAuthoringDocumentQuestion question, QualificationChannelFixture fixture) =>
        (wanted.Label is null || ContainsPhrase(question.Label, wanted.Label)) && (wanted.Types is null || wanted.Types.Contains(question.Type, StringComparer.Ordinal))
        && (wanted.Options is null || question.Type is not ("choice" or "multiChoice") || wanted.Options.Length == question.Options.Count
            && wanted.Options.Select((option, index) => SameText(option, question.Options[index])).All(same => same))
        && (question.Type != "catalog" || wanted.Catalog is { } alias && question.Catalog is { } used
            && fixture.Catalogs.Count(catalog => catalog.Alias == alias) == 1 && fixture.Catalogs.Single(catalog => catalog.Alias == alias).Id == used.Id);
}
