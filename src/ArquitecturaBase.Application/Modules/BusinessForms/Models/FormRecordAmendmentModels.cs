namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

/// <summary>Voiding a response (CW11-A): it keeps its number and history and stops counting.</summary>
public sealed record VoidFormRecordRequest(string? Reason)
{
    public override string ToString() => "VoidFormRecordRequest { Reason = [REDACTED] }";
}

/// <summary>
/// A correction loaded in the app (CW10-A): the same send as a new response, plus why, and the files of the replaced version
/// that stay. <see cref="RequestId"/> identifies the send: repeating it with the same content returns the first result.
/// </summary>
public sealed record CorrectFormEntryRequest(Guid RequestId, Guid VersionId, IReadOnlyList<FormEntryAnswer>? Answers,
    IReadOnlyList<FormEntryFileInput>? Files, string? Reason, IReadOnlyList<Guid>? KeptFileIds)
{
    public SubmitFormEntryRequest Entry => new(RequestId, VersionId, Answers, Files);
    public override string ToString() => "CorrectFormEntryRequest { Content = [REDACTED] }";
}

/// <summary>
/// What a correction opens: the active version, as for a new response, and the replaced response mapped to it — its answers
/// to the questions that remain and its files, so that what was there before stays in view.
/// </summary>
public sealed record FormEntryCorrectionResponse(FormEntryResponse Entry, Guid RecordId, string Number,
    IReadOnlyList<FormEntryCorrectionAnswer> Answers, IReadOnlyList<FormEntryCorrectionFile> Files)
{
    public override string ToString() => "FormEntryCorrectionResponse { Content = [REDACTED] }";
}

/// <summary>An answer as it was: <see cref="Value"/> canonical, or the chosen options, with their texts for a catalog.</summary>
public sealed record FormEntryCorrectionAnswer(Guid FieldId, string? Value, IReadOnlyList<Guid>? ChoiceIds,
    IReadOnlyList<FormEntryOptionResponse>? Options)
{
    public override string ToString() => "FormEntryCorrectionAnswer { Content = [REDACTED] }";
}

public sealed record FormEntryCorrectionFile(Guid FileId, Guid FieldId, string Name, string ContentType, long Size, bool HasThumbnail)
{
    public override string ToString() => "FormEntryCorrectionFile { Content = [REDACTED] }";
}

/// <summary>The correction a capture records: the response it replaces, why, and the files of that response that stay.</summary>
public sealed record FormRecordCorrection(Guid RecordId, string Reason, IReadOnlyList<Guid> KeptFileIds)
{
    public override string ToString() => "FormRecordCorrection { Reason = [REDACTED] }";
}
