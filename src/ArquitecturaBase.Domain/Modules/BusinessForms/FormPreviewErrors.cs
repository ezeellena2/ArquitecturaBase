using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Domain.Modules.BusinessForms;
public static class FormPreviewErrors
{
    public static readonly Error CultureInvalid = Error.Validation("Forms.Preview.CultureInvalid", "The preview culture must be es or en.");
    public static readonly Error NotAvailable = Error.Validation("Forms.Preview.NotAvailable", "The official preview is not available for this version.");
    public static readonly Error Expired = Error.Validation("Forms.Preview.Expired", "The official preview expired.");
}
