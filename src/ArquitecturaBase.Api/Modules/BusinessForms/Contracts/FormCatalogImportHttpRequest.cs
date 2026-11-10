using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormCatalogImportHttpRequest([Required] IFormFile File, long ExpectedRevision, [Required] string Mode,
    string? SheetSha256 = null, int? ConfirmDeactivations = null, bool ConfirmEmpty = false, bool ConfirmErrors = false)
{
    public override string ToString() => "FormCatalogImportHttpRequest { Content = [REDACTED] }";
}

public sealed record FormCatalogExportQuery(bool Template = false);

/// <summary>The spreadsheet a proposal of the Home chat mapped: the turn's request, the proposal and the catalog's revision.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogAssistantImportPreviewHttpRequest(Guid? RequestId, Guid? ProposalId, long? ExpectedRevision);

/// <summary>Applying it: the same, the sheet that was reviewed and what the person confirmed.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FormCatalogAssistantImportHttpRequest(Guid? RequestId, Guid? ProposalId, long? ExpectedRevision, string? SheetSha256 = null,
    int? ConfirmDeactivations = null, bool ConfirmEmpty = false, bool ConfirmErrors = false)
{
    public override string ToString() => "FormCatalogAssistantImportHttpRequest { Content = [REDACTED] }";
}
