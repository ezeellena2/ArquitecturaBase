using System.Text;
using System.Text.Json.Nodes;

namespace ArquitecturaBase.Application.Modules.Arca.Services.Invoicing;

/// <summary>
/// El texto del QR obligatorio (RG 4892, especificación de ARCA): <c>https://www.arca.gob.ar/fe/qr/?p=</c> más el JSON versión 1
/// en Base64, con la fecha, la CUIT, el punto de venta, el tipo, el número, el importe, la moneda y su cotización, el documento
/// del receptor si lo hay y el CAE («E»).
/// </summary>
internal static class ArcaFiscalQrPolicy
{
    public const string BaseUrl = "https://www.arca.gob.ar/fe/qr/?p=";
    private const int UnidentifiedDocument = 99;

    public static string Url(DateOnly issuedOn, string cuit, int pointOfSale, int voucherType, long number, decimal total, string currency,
        decimal rate, int documentType, long documentNumber, string cae)
    {
        var json = new JsonObject
        {
            ["ver"] = 1,
            ["fecha"] = issuedOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["cuit"] = long.Parse(cuit, System.Globalization.CultureInfo.InvariantCulture),
            ["ptoVta"] = pointOfSale,
            ["tipoCmp"] = voucherType,
            ["nroCmp"] = number,
            ["importe"] = total,
            ["moneda"] = currency,
            ["ctz"] = rate,
        };
        if (documentType != UnidentifiedDocument)
        {
            json["tipoDocRec"] = documentType;
            json["nroDocRec"] = documentNumber;
        }

        json["tipoCodAut"] = "E";
        json["codAut"] = long.Parse(cae, System.Globalization.CultureInfo.InvariantCulture);
        return BaseUrl + Convert.ToBase64String(Encoding.UTF8.GetBytes(json.ToJsonString()));
    }
}
