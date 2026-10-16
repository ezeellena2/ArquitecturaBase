using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;
public sealed record SaveFormPdfDesignHttpRequest(long? ExpectedRevision, FormPdfDesign? Design)
{ public override string ToString() => "SaveFormPdfDesignHttpRequest { Content = [REDACTED] }"; }
public sealed record PreviewFormPdfDesignHttpRequest(FormPdfDesign? Design, string? Culture)
{ public override string ToString() => "PreviewFormPdfDesignHttpRequest { Content = [REDACTED] }"; }
