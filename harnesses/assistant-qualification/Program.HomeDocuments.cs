namespace AssistantQualification;

internal static partial class Program
{
    // home-documents (Stage 3 in Home): its cases and the documents they bring, verified against the manifest and the document-reading cases the
    // case file names before a run touches its ledger, database or provider, and before a listing. A changed fixture never stops a paid run halfway.
    internal static Task<VerifiedHomeDocumentSet> HomeDocumentsAsync(CancellationToken ct) => QualificationHomeDocuments.LoadVerifiedAsync(ct);
}
