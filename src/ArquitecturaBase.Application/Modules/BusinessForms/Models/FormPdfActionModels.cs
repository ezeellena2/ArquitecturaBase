namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormPdfDesign(IReadOnlyDictionary<string, string> Name, IReadOnlyList<Guid> QuestionIds,
    bool IncludeApproval, IReadOnlyDictionary<string, string>? Footer, bool IncludePhotos)
{
    public override string ToString() => "FormPdfDesign { Content = [REDACTED] }";
}

public sealed record FormPdfActionSnapshotResponse(Guid RunId, Guid? DesignId, long DesignRevision, FormPdfDesign Design,
    IReadOnlyList<string> Cultures, string AppName, FormAuthorResponse Author, FormActionDecisionSnapshot? Decision)
{
    public override string ToString() => "FormPdfActionSnapshotResponse { Content = [REDACTED] }";
}

/// <summary>Localized response data used only while rendering; it is never stored in an action run.</summary>
public sealed record FormPdfRenderRequest(FormPdfActionSnapshotResponse Snapshot, FormResponseStoredRow Response)
{
    /// <summary>RE1-A: what external actions returned, already in the copy's language; a zone each, above the answers.</summary>
    public IReadOnlyList<FormPdfResult> Results { get; init; } = [];
    public override string ToString() => "FormPdfRenderRequest { Content = [REDACTED] }";
}
/// <summary>A result in the PDF (RE1-A): the action's name and each datum with its name, its kind and its canonical value.</summary>
public sealed record FormPdfResult(string Name, IReadOnlyList<FormPdfResultValue> Values)
{
    public override string ToString() => "FormPdfResult { Content = [REDACTED] }";
}
public sealed record FormPdfResultValue(string Label, string Kind, string? Value)
{
    public override string ToString() => "FormPdfResultValue { Content = [REDACTED] }";
}

public enum FormPdfActionState { Completed, Failed, Uncertain, NotFound }

public sealed record FormPdfArtifact(string Culture, string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "FormPdfArtifact { Content = [REDACTED] }";
}

public sealed record FormPdfActionOutcomeResponse(FormPdfActionState State, IReadOnlyList<FormPdfArtifact> Artifacts, string? FailureCode = null)
{
    public override string ToString() => "FormPdfActionOutcomeResponse { Content = [REDACTED] }";
}
