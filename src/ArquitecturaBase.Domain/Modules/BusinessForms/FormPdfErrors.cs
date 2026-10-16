using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormPdfErrors
{
    public static readonly Error ContentInvalid = Error.Validation("Forms.Pdf.ContentInvalid", "The PDF design is invalid.");
    public static readonly Error RevisionConflict = Error.Conflict("Forms.Pdf.RevisionConflict", "The PDF design changed. Reload it.");
    public static readonly Error NotFound = Error.NotFound("Forms.Pdf.NotFound", "The PDF document was not found.");
}
