using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// El pedido a <c>FECAESolicitar</c> sale de la emisión y de lo que fijó al encolarse, en pesos. En la clase C sólo hay neto
/// (el subtotal) y total; en la A y la B, el desglose del IVA por alícuota (<see cref="ArcaVatBreakdown"/>), con exento y no
/// gravado aparte. La huella (SHA-256) cubre todo lo que se manda, con el número: se guarda antes de llamar (ADR 0028). Los
/// importes son los que se fijaron al encolar (H2): el pedido sólo suma el número, y nada se calcula de nuevo en cada intento.
/// </summary>
internal static class ArcaVoucherRequestPolicy
{
    public const string Pesos = "PES";

    // Strict: a field, a value or a missing part this version does not know is not read (H2).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public static string ToJson(ArcaVoucherPayload payload) => JsonSerializer.Serialize(payload, Json);

    /// <summary>
    /// Lo que se fijó al encolar, o null si lo escribió una versión con otra forma (H2): otra versión, un dato o un valor que ésta
    /// no conoce. Nadie lo arma de nuevo adivinando: el worker lo deja esperando y la respuesta va sin el receptor.
    /// </summary>
    public static ArcaVoucherPayload? Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ArcaVoucherPayload>(json, Json) is { Version: ArcaVoucherPayload.CurrentVersion } payload ? payload : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static ArcaVoucherRequest Build(ArcaVoucher voucher, ArcaVoucherPayload payload, long number) => new(
        voucher.PointOfSale,
        voucher.VoucherType,
        number,
        voucher.Concept,
        payload.Receiver.DocumentType,
        payload.Receiver.DocumentNumber,
        voucher.RequestedOn,
        payload.Amounts,
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

    /// <summary>Los importes de una clase con sus renglones, al encolar. En la A y la B cada renglón trae su IVA.</summary>
    public static ArcaVoucherAmounts Amounts(ArcaVoucherClass voucherClass, IReadOnlyList<ArcaVoucherLine> lines, bool pricesIncludeVat)
    {
        if (voucherClass == ArcaVoucherClass.C)
        {
            var total = lines.Sum(line => line.Amount);
            return new ArcaVoucherAmounts(total, 0m, total, 0m, 0m, 0m, []);
        }

        var breakdown = ArcaVatBreakdown.Calculate(lines.Select(line => new ArcaVatLine(line.Amount,
            line.Vat?.ToTreatment() ?? throw new InvalidOperationException("A line of an A or B voucher has no valid VAT."))), pricesIncludeVat);
        return new ArcaVoucherAmounts(breakdown.Total, breakdown.NotTaxed, breakdown.NetTaxed, breakdown.Exempt, 0m, breakdown.Vat,
            [.. breakdown.Rates.Select(rate => new ArcaVatAmount(rate.RateId, rate.Base, rate.Vat))]);
    }

    private static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Date(DateOnly? value) => value?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? string.Empty;
}
