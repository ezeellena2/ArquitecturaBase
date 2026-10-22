using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormCatalogImportHttpRequest([Required] IFormFile File, long ExpectedRevision, [Required] string Mode,
    string? SheetSha256 = null, int? ConfirmDeactivations = null, bool ConfirmEmpty = false, bool ConfirmErrors = false)
{
    public override string ToString() => "FormCatalogImportHttpRequest { Content = [REDACTED] }";
}

public sealed record FormCatalogExportQuery(bool Template = false);
