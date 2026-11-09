using ArquitecturaBase.Domain.Modules.Arca;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVoucherClassPolicyTests
{
    [Theory]
    [InlineData(ArcaTaxStatus.Monotax, 5)]
    [InlineData(ArcaTaxStatus.Monotax, 1)]
    [InlineData(ArcaTaxStatus.Exempt, 6)]
    [InlineData(ArcaTaxStatus.Exempt, 16)]
    public void A_monotax_or_exempt_issuer_invoices_class_c_to_any_receiver(ArcaTaxStatus issuer, int receiver)
    {
        var decided = ArcaVoucherClassPolicy.Decide(issuer, receiver);

        Assert.Equal(ArcaVoucherClass.C, decided.Value);
    }

    [Fact]
    public void A_registered_taxpayer_is_not_supported_until_classes_a_and_b()
    {
        Assert.Equal(ArcaInvoicingErrors.ClassNotSupported, ArcaVoucherClassPolicy.Decide(ArcaTaxStatus.RegisteredTaxpayer, 5).Error);
    }

    [Fact]
    public void An_issuer_without_vat_registration_cannot_invoice()
    {
        Assert.Equal(ArcaInvoicingErrors.IssuerNotRegistered, ArcaVoucherClassPolicy.Decide(ArcaTaxStatus.NotRegistered, 5).Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void A_receiver_condition_that_arca_does_not_have_is_rejected(int receiver)
    {
        Assert.Equal(ArcaInvoicingErrors.ReceiverConditionInvalid, ArcaVoucherClassPolicy.Decide(ArcaTaxStatus.Monotax, receiver).Error);
    }

    [Theory]
    [InlineData(ArcaVoucherClass.A, ArcaVoucherKind.Invoice, 1)]
    [InlineData(ArcaVoucherClass.A, ArcaVoucherKind.CreditNote, 3)]
    [InlineData(ArcaVoucherClass.B, ArcaVoucherKind.Invoice, 6)]
    [InlineData(ArcaVoucherClass.B, ArcaVoucherKind.CreditNote, 8)]
    [InlineData(ArcaVoucherClass.C, ArcaVoucherKind.Invoice, 11)]
    [InlineData(ArcaVoucherClass.C, ArcaVoucherKind.CreditNote, 13)]
    public void Each_class_and_kind_has_its_arca_code(ArcaVoucherClass voucherClass, ArcaVoucherKind kind, int code)
    {
        Assert.Equal(code, ArcaVoucherTypes.Code(voucherClass, kind));
    }

    [Theory]
    [InlineData(1, ArcaVoucherClass.A, true)]
    [InlineData(1, ArcaVoucherClass.B, false)]
    [InlineData(5, ArcaVoucherClass.A, false)]
    [InlineData(5, ArcaVoucherClass.B, true)]
    [InlineData(13, ArcaVoucherClass.A, true)]
    [InlineData(15, ArcaVoucherClass.B, true)]
    [InlineData(15, ArcaVoucherClass.C, true)]
    public void Each_receiver_condition_admits_the_classes_of_the_manual(int condition, ArcaVoucherClass voucherClass, bool admitted)
    {
        Assert.Equal(admitted, ArcaReceiverVatConditions.Admits(condition, voucherClass));
    }
}
