namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record SaveFormPdfDesignRequest(long? ExpectedRevision, FormPdfDesign? Design)
{ public override string ToString() => "SaveFormPdfDesignRequest { Content = [REDACTED] }"; }
public sealed record PreviewFormPdfDesignRequest(FormPdfDesign? Design, string? Culture)
{ public override string ToString() => "PreviewFormPdfDesignRequest { Content = [REDACTED] }"; }
public sealed record FormPdfDesignResponse(Guid? Id, Guid DefinitionId, long Revision, string OriginalCulture,
    FormPdfDesign Design, DateTime? CreatedAtUtc, FormAuthorResponse? Author)
{ public override string ToString() => "FormPdfDesignResponse { Content = [REDACTED] }"; }
public sealed record FormPdfDesignStoredRow(Guid Id, Guid DefinitionId, long Revision, string OriginalCulture,
    string DesignJson, DateTime CreatedAtUtc, FormAuthorResponse? Author)
{ public override string ToString() => "FormPdfDesignStoredRow { Content = [REDACTED] }"; }
public sealed record FormPdfDocumentRow(Guid Id, Guid RecordId, string Culture, string Name, string StorageKey)
{ public override string ToString() => "FormPdfDocumentRow { Content = [REDACTED] }"; }
public sealed record FormPdfContentResponse(Stream Content, string Name, string Culture)
{ public override string ToString() => "FormPdfContentResponse { Content = [REDACTED] }"; }
