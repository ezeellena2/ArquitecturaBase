using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.Arca;

public static class ArcaInvoicingErrors
{
    public static readonly Error PointOfSaleInvalid = Error.Validation("Arca.Invoicing.PointOfSaleInvalid", "The point of sale must be between 1 and 99998.");
    public static readonly Error PointOfSaleUnavailable = Error.Validation("Arca.Invoicing.PointOfSaleUnavailable", "ARCA no longer offers that point of sale.");
    public static readonly Error GrossIncomeInvalid = Error.Validation("Arca.Invoicing.GrossIncomeInvalid", "The gross income number is required and has up to 30 characters.");
    public static readonly Error ActivityStartInvalid = Error.Validation("Arca.Invoicing.ActivityStartInvalid", "The activity start date is required.");
    public static readonly Error ClassNotSupported = Error.Validation("Arca.Invoicing.ClassNotSupported", "Invoices of classes A and B are not available yet.");
    public static readonly Error IssuerNotRegistered = Error.Validation("Arca.Invoicing.IssuerNotRegistered", "The company has no VAT registration to invoice with.");
    public static readonly Error ReceiverConditionInvalid = Error.Validation("Arca.Invoicing.ReceiverConditionInvalid", "The VAT condition of the receiver does not fit the voucher class.");
    public static readonly Error VoucherNotInReview = Error.Conflict("Arca.Invoicing.VoucherNotInReview", "The issuance is no longer waiting for a review.");
    public static readonly Error VoucherRevisionConflict = Error.Conflict("Arca.Invoicing.VoucherRevisionConflict", "The issuance changed since it was read.");
    public static readonly Error ReviewDecisionInvalid = Error.Validation("Arca.Invoicing.ReviewDecisionInvalid", "That decision does not fit why the issuance is waiting.");
    public static readonly Error VoucherNotCancellable = Error.Conflict("Arca.Invoicing.VoucherNotCancellable", "The issuance already has a number and cannot be cancelled.");
}
