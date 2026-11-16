using System.Net;
using System.Net.Sockets;

namespace ArquitecturaBase.Application.Common.RateLimiting;

/// <summary>
/// La partición de una dirección para un límite por dirección (N20): una IPv4 tal cual, también si llega mapeada en IPv6,
/// y una IPv6 por su /64, que es lo que un proveedor le da a una casa o a un celular; así rotar adentro de esa red no
/// esquiva el límite. Agrupar por /64 no alcanza contra quien tiene un /48, y cada límite en memoria sigue siendo de su
/// copia. La usan los límites HTTP por IP del núcleo, el tope de códigos por dirección de WhatsApp y el de los códigos por
/// correo (N7), que guarda la partición con cada código y cuenta en la base.
/// </summary>
public static class AddressPartition
{
    /// <summary>La partición de los pedidos sin dirección conocida: la comparten todos.</summary>
    public const string Unknown = "unknown";

    public static string Of(IPAddress? address)
    {
        if (address is null)
        {
            return Unknown;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return address.ToString();
        }

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);

        return new IPAddress(bytes) + "/64";
    }

    /// <summary>La de una dirección escrita; lo que no es una dirección cuenta tal como vino.</summary>
    public static string Of(string? address) =>
        string.IsNullOrEmpty(address) ? Unknown
        : IPAddress.TryParse(address, out var parsed) ? Of(parsed)
        : address;

    /// <summary>
    /// La de la dirección de un pedido, para guardarla con lo que pidió o contar lo que pidió (N7: los códigos por correo):
    /// null si no se conoce o no es una dirección, que no comparte su lugar con nadie.
    /// </summary>
    public static string? OfKnown(string? address) =>
        IPAddress.TryParse(address, out var parsed) ? Of(parsed) : null;
}
