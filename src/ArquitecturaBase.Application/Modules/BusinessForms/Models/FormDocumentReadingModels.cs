using System.Text.Json.Serialization;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

// DOC1: the reading of documents, a setting of the form like «Dónde se completa»: whether it is on, «Cómo reconocerlo» (up to 1000
// characters, required while on) and up to five lookups in catalogs. A draft without it has null, so drafts, versions and their
// fingerprints keep their bytes; turned off, it keeps what was written.
public sealed record FormDocumentReading(bool Enabled, string Recognition, IReadOnlyList<FormDocumentLookup> Lookups)
{
    public const int MaxRecognitionLength = 1000;
    public const int MaxLookups = 5;
    public override string ToString() => "FormDocumentReading { Content = [REDACTED] }";
}
// A lookup fills a catalog question (QuestionId) with the option whose code ("code"), text ("text") or catalog field (its id in "D"
// format) matches what another question read (SourceQuestionId). In a draft each names a question by its FieldId or, when it is
// new, by its LocalId; saved, by its FieldId.
public sealed record FormDocumentLookup(Guid QuestionId, Guid SourceQuestionId, string Field)
{
    public const string Code = "code";
    public const string Text = "text";
    public override string ToString() => "FormDocumentLookup { Content = [REDACTED] }";
}
// DOC1: the fixed validation of a question bound by a library standard: an account (a CBU or a CVU whose check digits add up, or an
// alias) or a CUIT or CUIL. Only a bound short text has it; every capture enforces it, whatever the channel.
public static class FormQuestionValidation
{
    public const string BankAccount = "bankAccount";
    public const string TaxId = "taxId";
    public static bool IsKnown(string? value) => value is BankAccount or TaxId;
}
// «Cómo viene leyendo» (DOC1, decision 3 of the user): since the start of the window (SinceUtc, 30 days back), the documents confirmed
// into a response of the form; per question the reading prefilled (proposed and included), how many values it prefilled and how many
// were confirmed without changes; the question corrected most often (null without corrections) and the questions whose values mark a repeated document
// (from the standard the form came from; empty for an own form). Examples, the latest corrections, are values of responses: they
// come only with «Ver respuestas», and the key is omitted without it.
public sealed record FormDocumentReadingStatsResponse(DateTime SinceUtc, int Documents, IReadOnlyList<FormDocumentReadingQuestionResponse> Questions,
    FormDocumentReadingCorrectedResponse? MostCorrected, IReadOnlyList<Guid> DuplicateQuestionIds)
{
    public const int WindowDays = 30;
    public const int MaxExamples = 10;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<FormDocumentReadingExampleResponse>? Examples { get; init; }
}
public sealed record FormDocumentReadingQuestionResponse(Guid QuestionId, int Read, int Unchanged);
public sealed record FormDocumentReadingCorrectedResponse(Guid QuestionId, int Corrections);
// A correction: the response and its number, when it was confirmed, the question, and the value read and the one confirmed as display
// text in the language of whoever asks (empty when it was left empty).
public sealed record FormDocumentReadingExampleResponse(Guid RecordId, string RecordNumber, DateTime ConfirmedAtUtc, Guid QuestionId, string Read,
    string Confirmed)
{
    public override string ToString() => "FormDocumentReadingExampleResponse { Content = [REDACTED] }";
}
// What the reader computes in SQL: the documents confirmed since a moment and the per-question counts.
public sealed record FormDocumentReadingStatsRow(int Documents, IReadOnlyList<FormDocumentReadingQuestionResponse> Questions);
// The form whose reading is asked about: its prefix and the standard it came from with its sealed bound map, when it has one.
public sealed record FormDocumentReadingDefinitionRow(string Prefix, string? StandardKey, int? StandardVersion, string? StandardBindingsJson);
// A correction as the reader finds it: the canonical values read and confirmed and, of a catalog option, the texts the capture kept.
public sealed record FormDocumentReadingCorrectionRow(Guid RecordId, long Number, Guid VersionId, DateTime SubmittedAtUtc, Guid FieldId, string? Read,
    string? Confirmed, string? ReadText, string? ConfirmedText)
{
    public override string ToString() => "FormDocumentReadingCorrectionRow { Content = [REDACTED] }";
}
