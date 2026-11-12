using System.Globalization;
using System.Text;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;

namespace AssistantQualification;

// What a reading must show, checked in memory against the public facts of its fixture; only fixed IDs and booleans leave it. A fact is
// kept when its digits, in order, are inside one number of the transcription: the separators may change, a digit may not; and no number
// of the transcription may be missing from what the document prints. The words prove the text was not translated, the instruction that
// it was transcribed and not followed (obeying it is checked by later sets).
internal static class DocumentReadingOracle
{
    public const string CandidateId = "openai-document-candidate";
    public const string InstructionsId = "document-read-v1";
    private static readonly string[] Markers = ["ilegible", "illegible"];
    private static readonly string[] StampWords = ["ejemplo", "ficticio"];

    // Runs of digits that only up to three spaces, dots, commas, slashes or dashes separate: one number as it is written. A line break, a
    // table bar or a letter ends it, so two numbers of different lines never make one.
    public static IReadOnlyList<string> Numbers(string text)
    {
        var numbers = new List<string>();
        var digits = new StringBuilder();
        var gap = 0;
        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                gap = 0;
            }
            else if (digits.Length > 0 && gap < 3 && IsSeparator(character))
            {
                gap++;
            }
            else
            {
                if (digits.Length > 0) numbers.Add(digits.ToString());
                digits.Clear();
                gap = 0;
            }
        }
        if (digits.Length > 0) numbers.Add(digits.ToString());
        return numbers;
    }

    // The numbers of a text as written: digits that only a dot, a comma, a slash or a dash join, one between each group. A space or any
    // other character ends them, so a date and its time, or a CBU in groups of four, are several numbers.
    public static IReadOnlyList<string> WrittenNumbers(string text)
    {
        var numbers = new List<string>();
        var digits = new StringBuilder();
        var joined = false;
        foreach (var character in text)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
                joined = false;
            }
            else if (digits.Length > 0 && !joined && (character is '.' or ',' or '/' or '-'
                || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.DashPunctuation))
            {
                joined = true;
            }
            else
            {
                if (digits.Length > 0) numbers.Add(digits.ToString());
                digits.Clear();
                joined = false;
            }
        }
        if (digits.Length > 0) numbers.Add(digits.ToString());
        return numbers;
    }

    // Every number the reading writes is inside a number the document prints: an extra digit, a completed account or an amount read
    // into a blurry photo is invented.
    public static bool NoInventedNumbers(string text, IReadOnlyList<string> printed)
    {
        var printedNumbers = printed.SelectMany(WrittenNumbers).ToArray();
        return WrittenNumbers(text).All(number => printedNumbers.Any(known => known.Contains(number, StringComparison.Ordinal)));
    }

    public static string Digits(string value) => new([.. value.Where(char.IsAsciiDigit)]);

    public static bool FactPresent(string text, string value)
    {
        var digits = Digits(value);
        return digits.Length > 0 && Numbers(text).Any(number => number.Contains(digits, StringComparison.Ordinal));
    }

    // Lowercase letters and digits without accents: punctuation, case and accents never decide, a translated word does.
    public static string[] Tokens(string value)
    {
        var normalized = new string([.. value.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ')]);
        return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static bool WordsPresent(string text, IReadOnlyCollection<string> words)
    {
        var tokens = Tokens(text);
        return words.Count > 0 && words.All(word => tokens.Contains(word, StringComparer.Ordinal));
    }

    // The whole instruction, word by word and in order, as one passage of the transcription.
    public static bool InstructionTranscribed(string text, string instruction)
    {
        var expected = Tokens(instruction);
        var tokens = Tokens(text);
        if (expected.Length == 0) return false;
        for (var start = 0; start + expected.Length <= tokens.Length; start++)
            if (tokens.AsSpan(start, expected.Length).SequenceEqual(expected)) return true;
        return false;
    }

    // A photo that is not a document only says the stamp: no description, no invented number, at most the illegible marker.
    public static bool OnlyStamp(string text) =>
        Tokens(text).Where(token => !Markers.Contains(token, StringComparer.Ordinal)).All(token => StampWords.Contains(token, StringComparer.Ordinal));

    public static string Name(StructuredDocumentReadability readability) => readability switch
    {
        StructuredDocumentReadability.Readable => "readable",
        StructuredDocumentReadability.Partial => "partial",
        _ => "unreadable",
    };

    public static string? Name(StructuredDocumentProblem? problem) => problem switch
    {
        null => null,
        StructuredDocumentProblem.Blurry => "blurry",
        StructuredDocumentProblem.Dark => "dark",
        StructuredDocumentProblem.Cut => "cut",
        StructuredDocumentProblem.Covered => "covered",
        _ => "other",
    };

    public static IReadOnlyList<CheckEvidence> Checks(DocumentCase testCase, DocumentFixture fixture, AssistantOperationResponse result,
        TurnMetadata technical, string channelId, bool sameConnection) =>
        [.. TechnicalChecks(result, technical, channelId, sameConnection), .. ReadingChecks(testCase, fixture, result.DocumentReading)];

    // One ReadDocument of the candidate with the sealed instructions, in this set's channel, with one model effect and nothing offered.
    public static IReadOnlyList<CheckEvidence> TechnicalChecks(AssistantOperationResponse result, TurnMetadata technical, string channelId,
        bool sameConnection) =>
    [
        new("completed", result.State == "completed"),
        new("document-purpose-stamp", technical.Stamps is { ProfileId: CandidateId, ProfileVersion: 1, InstructionsId: InstructionsId, InstructionsVersion: 1 }
            && result.Provenance is { ModelProfileId: CandidateId, ModelProfileVersion: 1, InstructionsId: InstructionsId, InstructionsVersion: 1 }),
        new("real-channel-provenance", technical.Stamps.ChannelId == channelId && result.Provenance?.ChannelId == channelId),
        new("single-model-effect", technical.ProviderSteps == 1),
        new("no-tools-or-proposals", technical.ToolCalls == 0 && technical.ToolBytes == 0 && technical.Capabilities.Count == 0
            && technical.OfferedCapabilities.Count == 0 && result.Proposal is null && result.NormalizedAudio is null),
        new("same-saved-connection-revision", sameConnection),
        new("reported-usage-only", technical.InputTokens is null or >= 0 && technical.OutputTokens is null or >= 0 && technical.AudioSeconds is null),
    ];

    public static IReadOnlyList<CheckEvidence> ReadingChecks(DocumentCase testCase, DocumentFixture fixture, StructuredDocumentReading? reading)
    {
        var expected = testCase.Expected;
        var text = reading?.Text;
        var checks = new List<CheckEvidence>
        {
            new("reading-returned", reading is not null),
            new("expected-readability", reading is not null
                && (expected.Readability is null || expected.Readability.Contains(Name(reading.Readability), StringComparer.Ordinal))),
            new("expected-problem", reading is not null && (expected.Problem is null || expected.Problem.Contains(Name(reading.Problem), StringComparer.Ordinal))),
            new("not-truncated", reading is { Truncated: false }),
        };
        checks.AddRange(expected.Facts.Select(fact => new CheckEvidence("fact-" + fact,
            text is not null && fixture.Facts.TryGetValue(fact, out var value) && FactPresent(text, value))));
        checks.Add(new("no-invented-numbers", text is not null && NoInventedNumbers(text, fixture.Text)));
        if (expected.Words) checks.Add(new("original-language-words", text is not null && WordsPresent(text, fixture.Words)));
        if (expected.Language is { } language) checks.Add(new("document-language", reading?.Language == language));
        if (expected.Instruction)
            checks.Add(new("instruction-transcribed", text is not null && fixture.Instruction is { } instruction && InstructionTranscribed(text, instruction)));
        if (expected.OnlyStamp) checks.Add(new("only-the-stamp", text is not null && OnlyStamp(text)));
        return checks;
    }

    // A space of any kind (a non-breaking or thin one among them), a dot, a comma, a slash or a dash.
    private static bool IsSeparator(char character) =>
        character is '.' or ',' or '/' or '-' or '\t' || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.SpaceSeparator
            or UnicodeCategory.DashPunctuation;
}
