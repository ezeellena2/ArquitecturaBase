using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// El pedido a <c>FECAESolicitar</c> sale de la emisión y de lo que fijó al encolarse. En la clase C sólo hay neto (el
/// subtotal) y total, en pesos; A y B llegan con la etapa 4. La huella (SHA-256) cubre todo lo que se manda, con el número:
/// se guarda antes de llamar (ADR 0028).
/// </summary>
internal static class ArcaVoucherRequestPolicy
{
    public const string Pesos = "PES";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static string ToJson(ArcaVoucherPayload payload) => JsonSerializer.Serialize(payload, Json);

    public static ArcaVoucherPayload FromJson(string json) =>
        JsonSerializer.Deserialize<ArcaVoucherPayload>(json, Json) ?? throw new InvalidOperationException("An ARCA voucher has an empty payload.");

    public static ArcaVoucherRequest Build(ArcaVoucher voucher, ArcaVoucherPayload payload, long number) => new(
        voucher.PointOfSale,
        voucher.VoucherType,
        number,
        voucher.Concept,
        payload.Receiver.DocumentType,
        payload.Receiver.DocumentNumber,
        voucher.RequestedOn,
        Amounts(voucher),
        payload.ServiceFrom,
        payload.ServiceTo,
        payload.PaymentDueOn,
        Pesos,
        1m,
        payload.Receiver.VatConditionId,
        payload.Cancels is { } cancels ? [cancels] : []);

    public static string Fingerprint(ArcaVoucherRequest request)
    {
        var amounts = request.Amounts;
        var text = string.Join('|', new object?[]
        {
            request.PointOfSale, request.VoucherType, request.Number, request.Concept, request.DocumentType, request.DocumentNumber,
            Date(request.IssuedOn), amounts.Total, amounts.NotTaxed, amounts.Net, amounts.Exempt, amounts.Taxes, amounts.Vat,
            string.Join(';', amounts.VatRates.Select(rate => $"{rate.RateId}:{Amount(rate.Base)}:{Amount(rate.Amount)}")),
            Date(request.ServiceFrom), Date(request.ServiceTo), Date(request.PaymentDueOn), request.CurrencyId, request.CurrencyRate,
            request.ReceiverVatConditionId,
            string.Join(';', request.Associated.Select(associated =>
                $"{associated.VoucherType}:{associated.PointOfSale}:{associated.Number}:{associated.Cuit}:{Date(associated.IssuedOn)}")),
        }.Select(value => value switch
        {
            decimal amount => Amount(amount),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value?.ToString() ?? string.Empty,
        }));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static ArcaVoucherAmounts Amounts(ArcaVoucher voucher) => voucher.Class == ArcaVoucherClass.C
        ? new ArcaVoucherAmounts(voucher.Total, 0m, voucher.Total, 0m, 0m, 0m, [])
        : throw new InvalidOperationException("Vouchers of classes A and B are not issued yet.");

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
}
