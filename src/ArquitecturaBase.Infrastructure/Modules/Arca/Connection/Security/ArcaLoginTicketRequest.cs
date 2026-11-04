using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

/// <summary>
/// El pedido de ticket (TRA) del WSAA: diez minutos antes y después de ahora, en hora argentina, y un uniqueId al azar
/// para que dos pedidos seguidos no choquen.
/// </summary>
internal static class ArcaLoginTicketRequest
{
    private static readonly TimeSpan Argentina = TimeSpan.FromHours(-3);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    public static byte[] Build(string service, DateTimeOffset nowUtc)
    {
        var document = new XElement("loginTicketRequest", new XAttribute("version", "1.0"),
            new XElement("header",
                new XElement("uniqueId", RandomNumberGenerator.GetInt32(int.MaxValue)),
                new XElement("generationTime", Format(nowUtc - Window)),
                new XElement("expirationTime", Format(nowUtc + Window))),
            new XElement("service", service));
        return Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + document.ToString(SaveOptions.DisableFormatting));
    }

    private static string Format(DateTimeOffset value) =>
        value.ToOffset(Argentina).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
}
