namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormPdfDesign(IReadOnlyDictionary<string, string> Name, IReadOnlyList<Guid> QuestionIds,
    bool IncludeApproval, IReadOnlyDictionary<string, string>? Footer, bool IncludePhotos)
{
    public override string ToString() => "FormPdfDesign { Content = [REDACTED] }";
}

public sealed record FormPdfActionSnapshotResponse(Guid RunId, Guid? DesignId, long DesignRevision, FormPdfDesign Design,
    FormActionRecordSnapshot Record, IReadOnlyList<string> Cultures)
{
    public override string ToString() => "FormPdfActionSnapshotResponse { Content = [REDACTED] }";
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
