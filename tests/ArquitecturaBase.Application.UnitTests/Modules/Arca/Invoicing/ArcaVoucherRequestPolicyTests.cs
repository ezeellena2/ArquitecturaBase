using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Connection;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVoucherRequestPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 9, 13, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_class_c_request_carries_the_total_as_net_and_nothing_else()
    {
        var (voucher, payload) = Invoice();

        var request = ArcaVoucherRequestPolicy.Build(voucher, payload, 12);

        Assert.Equal(new ArcaVoucherAmounts(605500m, 0m, 605500m, 0m, 0m, 0m, []), request.Amounts);
        Assert.Equal((4, 11, 12L, 1, 80, 30712345671L, new DateOnly(2026, 11, 9), "PES", 1m, 1),
            (request.PointOfSale, request.VoucherType, request.Number, request.Concept, request.DocumentType, request.DocumentNumber, request.IssuedOn,
             request.CurrencyId, request.CurrencyRate, request.ReceiverVatConditionId));
        Assert.Empty(request.Associated);
    }

    [Fact]
    public void A_credit_note_request_associates_the_invoice()
    {
        var (voucher, payload) = Invoice();
        var cancels = new ArcaAssociatedVoucher(11, 4, 12, "27287654338", new DateOnly(2026, 11, 9));

        var request = ArcaVoucherRequestPolicy.Build(voucher, payload with { Cancels = cancels }, 1);

        Assert.Equal([cancels], request.Associated);
    }

    [Fact]
    public void A_class_a_request_carries_the_net_the_vat_and_one_entry_per_rate()
    {
        var (voucher, payload) = Invoice(ArcaVoucherClass.A, pricesIncludeVat: false);

        var request = ArcaVoucherRequestPolicy.Build(voucher, payload, 7);

        ArcaVoucherOrderPolicyTests.AssertAmounts(
            new ArcaVoucherAmounts(629200.21m, 0m, 520000.19m, 0m, 0m, 109200.02m, [new(4, 0.19m, 0.02m), new(5, 520000m, 109200m)]), request.Amounts);
        Assert.Equal((1, 80, 1), (request.VoucherType, request.DocumentType, request.ReceiverVatConditionId));
    }

    [Fact]
    public void A_class_b_request_with_prices_with_vat_splits_each_rate()
    {
        var (voucher, payload) = Invoice(ArcaVoucherClass.B, pricesIncludeVat: true);

        var request = ArcaVoucherRequestPolicy.Build(voucher, payload, 7);

        Assert.Equal(520000.19m, request.Amounts.Total);
        Assert.Equal([new ArcaVatAmount(4, 0.17m, 0.02m), new ArcaVatAmount(5, 429752.07m, 90247.93m)], request.Amounts.VatRates);
    }

    [Fact]
    public void The_vat_of_the_lines_and_whether_prices_include_it_survive_the_json()
    {
        var (_, payload) = Invoice(ArcaVoucherClass.B, pricesIncludeVat: true);

        var read = ArcaVoucherRequestPolicy.FromJson(ArcaVoucherRequestPolicy.ToJson(payload));

        Assert.Equal(payload.Lines, read.Lines);
        Assert.True(read.PricesIncludeVat);
    }

    [Fact]
    public void The_payload_survives_its_json()
    {
        var (_, payload) = Invoice();

        var read = ArcaVoucherRequestPolicy.FromJson(ArcaVoucherRequestPolicy.ToJson(payload));

        Assert.Equal(payload.Receiver, read.Receiver);
        Assert.Equal(payload.Lines, read.Lines);
        Assert.Equal(payload.Issuer, read.Issuer);
    }

    [Fact]
    public void The_fingerprint_is_stable_and_changes_with_what_is_sent()
    {
        var (voucher, payload) = Invoice();
        var request = ArcaVoucherRequestPolicy.Build(voucher, payload, 12);

        Assert.Equal(ArcaVoucherRequestPolicy.Fingerprint(request), ArcaVoucherRequestPolicy.Fingerprint(ArcaVoucherRequestPolicy.Build(voucher, payload, 12)));
        Assert.Matches("^[0-9a-f]{64}$", ArcaVoucherRequestPolicy.Fingerprint(request));
        Assert.NotEqual(ArcaVoucherRequestPolicy.Fingerprint(request), ArcaVoucherRequestPolicy.Fingerprint(request with { Number = 13 }));
        Assert.NotEqual(ArcaVoucherRequestPolicy.Fingerprint(request), ArcaVoucherRequestPolicy.Fingerprint(request with { DocumentNumber = 20111111112 }));
    }

    internal static (ArcaVoucher Voucher, ArcaVoucherPayload Payload) Invoice()
    {
        var payload = new ArcaVoucherPayload(new ArcaVoucherParty(80, 30712345671, "Agro del Sur S.A.", 1, "Ruta 8 km 225"), null, null, null,
            [new ArcaVoucherLine("Semilla de soja", 10m, 52000m, 520000m), new ArcaVoucherLine("Flete", 1m, 85500m, 85500m)],
            new ArcaVoucherIssuer("Lucía Fernández", "Av. San Martín 1250, Pergamino", ArcaTaxStatus.Monotax, "27-28765433-8", new DateOnly(2019, 3, 1)), null);
        var voucher = ArcaVoucher.Create(Guid.CreateVersion7(), ArcaEnvironment.Testing, Guid.CreateVersion7(), ArcaVoucherClass.C, ArcaVoucherKind.Invoice,
            "27287654338", 4, 1, 80, 30712345671, new DateOnly(2026, 11, 9), 605500m, ArcaVoucherRequestPolicy.ToJson(payload), null, Now);
        return (voucher, payload);
    }

    internal static (ArcaVoucher Voucher, ArcaVoucherPayload Payload) Invoice(ArcaVoucherClass voucherClass, bool pricesIncludeVat)
    {
        var receiver = voucherClass == ArcaVoucherClass.A ? new ArcaVoucherParty(80, 30712345671, "Agro del Sur S.A.", 1, "Ruta 8 km 225")
            : new ArcaVoucherParty(99, 0, "Consumidor final", 5, null);
        var payload = new ArcaVoucherPayload(receiver, null, null, null,
            [
                new ArcaVoucherLine("Semilla de soja", 10m, 52000m, 520000m, new(ArcaVatTreatmentKind.Taxed, 5)),
                new ArcaVoucherLine("Flete", 1.5m, 0.125m, 0.19m, new(ArcaVatTreatmentKind.Taxed, 4)),
            ],
            new ArcaVoucherIssuer("Lucía Fernández", "Av. San Martín 1250, Pergamino", ArcaTaxStatus.RegisteredTaxpayer, "27-28765433-8", new DateOnly(2019, 3, 1)),
            null, pricesIncludeVat);
        var amounts = ArcaVoucherRequestPolicy.Amounts(voucherClass, payload.Lines, pricesIncludeVat);
        var voucher = ArcaVoucher.Create(Guid.CreateVersion7(), ArcaEnvironment.Testing, Guid.CreateVersion7(), voucherClass, ArcaVoucherKind.Invoice,
            "27287654338", 4, 1, receiver.DocumentType, receiver.DocumentNumber, new DateOnly(2026, 11, 9), amounts.Total, ArcaVoucherRequestPolicy.ToJson(payload),
            null, Now);
        return (voucher, payload);
    }
}
