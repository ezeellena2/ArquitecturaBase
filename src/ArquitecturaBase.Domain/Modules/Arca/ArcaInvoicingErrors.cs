using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaInvoicingErrors
{
    public static readonly Error PointOfSaleInvalid = Error.Validation("Arca.Invoicing.PointOfSaleInvalid", "The point of sale must be between 1 and 99998.");
    public static readonly Error PointOfSaleUnavailable = Error.Validation("Arca.Invoicing.PointOfSaleUnavailable", "ARCA no longer offers that point of sale.");
    public static readonly Error GrossIncomeInvalid = Error.Validation("Arca.Invoicing.GrossIncomeInvalid", "The gross income number is required and has up to 30 characters.");
    public static readonly Error ActivityStartInvalid = Error.Validation("Arca.Invoicing.ActivityStartInvalid", "The activity start date is required.");
    public static readonly Error VoucherNotFound = Error.NotFound("Arca.Invoicing.VoucherNotFound", "The issuance does not exist.");
    public static readonly Error PointOfSaleMissing = Error.Validation("Arca.Invoicing.PointOfSaleMissing", "Choose the point of sale before issuing.");
    public static readonly Error IssuerDataMissing = Error.Validation("Arca.Invoicing.IssuerDataMissing", "The registry has not brought the company data yet.");
    public static readonly Error ReceiverInvalid = Error.Validation("Arca.Invoicing.ReceiverInvalid", "The receiver needs a name and a valid document.");
    public static readonly Error ConceptInvalid = Error.Validation("Arca.Invoicing.ConceptInvalid", "The concept is products, services or both.");
    public static readonly Error ServicePeriodInvalid = Error.Validation("Arca.Invoicing.ServicePeriodInvalid", "A service needs its period and payment due date, in order.");
    public static readonly Error IssueDateOutOfRange = Error.Validation("Arca.Invoicing.IssueDateOutOfRange", "ARCA does not take that date.");
    public static readonly Error ItemsInvalid = Error.Validation("Arca.Invoicing.ItemsInvalid", "Each item needs a description, a quantity and a price, and the total must be above zero.");
    public static readonly Error TotalMismatch = Error.Validation("Arca.Invoicing.TotalMismatch", "The total does not match the items.");
    public static readonly Error InvoiceNotAuthorized = Error.Conflict("Arca.Invoicing.InvoiceNotAuthorized", "The invoice has not been issued.");
    public static readonly Error InvoiceAlreadyCancelled = Error.Conflict("Arca.Invoicing.InvoiceAlreadyCancelled", "The invoice already has a credit note.");
    public static readonly Error IssuerNotRegistered = Error.Validation("Arca.Invoicing.IssuerNotRegistered", "The company has no VAT registration to invoice with.");
    public static readonly Error ReceiverConditionInvalid = Error.Validation("Arca.Invoicing.ReceiverConditionInvalid", "The VAT condition of the receiver does not fit the voucher class.");
    public static readonly Error VoucherNotInReview = Error.Conflict("Arca.Invoicing.VoucherNotInReview", "The issuance is no longer waiting for a review.");
    public static readonly Error VoucherRevisionConflict = Error.Conflict("Arca.Invoicing.VoucherRevisionConflict", "The issuance changed since it was read.");
    public static readonly Error ReviewDecisionInvalid = Error.Validation("Arca.Invoicing.ReviewDecisionInvalid", "That decision does not fit why the issuance is waiting.");
    public static readonly Error VoucherNotCancellable = Error.Conflict("Arca.Invoicing.VoucherNotCancellable", "The issuance already has a number and cannot be cancelled.");
}
