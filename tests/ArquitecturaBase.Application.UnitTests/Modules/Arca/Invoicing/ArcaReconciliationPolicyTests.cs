using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaReconciliationPolicyTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static readonly ArcaVoucherRequest Sent = new(4, 11, 12, 1, 80, 30712345671, Today,
        new ArcaVoucherAmounts(605500m, 0m, 605500m, 0m, 0m, 0m, []), null, null, null, "PES", 1m, 1, []);

    private static readonly ArcaIssuedVoucher Same = new(1, 80, 30712345671, Today, 605500m, "A", "76451234567890", new DateOnly(2026, 11, 19));

    [Fact]
    public void The_number_in_arca_with_our_data_was_issued()
    {
        Assert.Equal(ArcaReconciliation.Issued, ArcaReconciliationPolicy.Decide(Sent, Same, 12, null, Today));
    }

    [Theory]
    [InlineData(2, 80, 30712345671L, 605500)]
    [InlineData(1, 96, 30712345671L, 605500)]
    [InlineData(1, 80, 20111111112L, 605500)]
    [InlineData(1, 80, 30712345671L, 1)]
    public void The_number_in_arca_with_other_data_was_taken(int concept, int documentType, long documentNumber, int total)
    {
        var other = Same with { Concept = concept, DocumentType = documentType, DocumentNumber = documentNumber, Total = total };

        Assert.Equal(ArcaReconciliation.Taken, ArcaReconciliationPolicy.Decide(Sent, other, 12, null, Today));
    }

    [Fact]
    public void Another_date_is_another_voucher()
    {
        Assert.Equal(ArcaReconciliation.Taken, ArcaReconciliationPolicy.Decide(Sent, Same with { IssuedOn = new DateOnly(2026, 11, 8) }, 12, null, Today));
    }

    // ADR 0031: a number ARCA does not have yet goes again when ARCA can take its date, even after a 10016: the number may be ours,
    // authorized late and not shown yet, and freeing it could invoice the sale twice.
    [Fact]
    public void A_number_that_arca_does_not_have_and_is_still_next_was_not_issued_when_arca_takes_its_date()
    {
        Assert.Equal(ArcaReconciliation.NotIssued, ArcaReconciliationPolicy.Decide(Sent, null, 11, null, Today));
        Assert.Equal(ArcaReconciliation.NotIssued, ArcaReconciliationPolicy.Decide(Sent, null, 11, Today, Today));
        Assert.Equal(ArcaReconciliation.NotIssued, ArcaReconciliationPolicy.Decide(Sent, null, 11, Today.AddDays(-3), Today.AddDays(5)));
    }

    [Fact]
    public void A_number_still_next_with_a_date_before_the_last_voucher_had_its_date_refused()
    {
        Assert.Equal(ArcaReconciliation.DateRefused, ArcaReconciliationPolicy.Decide(Sent, null, 11, Today.AddDays(1), Today));
    }

    [Fact]
    public void A_number_still_next_whose_date_left_the_window_had_its_date_refused()
    {
        Assert.Equal(ArcaReconciliation.DateRefused, ArcaReconciliationPolicy.Decide(Sent, null, 11, null, Today.AddDays(6)));
    }

    [Fact]
    public void A_number_that_arca_does_not_have_but_already_passed_is_missing()
    {
        Assert.Equal(ArcaReconciliation.Missing, ArcaReconciliationPolicy.Decide(Sent, null, 12, null, Today));
        Assert.Equal(ArcaReconciliation.Missing, ArcaReconciliationPolicy.Decide(Sent, null, 15, null, Today));
        Assert.Equal(ArcaReconciliation.Missing, ArcaReconciliationPolicy.Decide(Sent, null, 12, Today.AddDays(1), Today));
    }
}
