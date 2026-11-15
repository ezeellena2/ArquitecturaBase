using System.Net;
using System.Net.Sockets;

namespace ArquitecturaBase.Application.Common.RateLimiting;

/// <summary>
/// La partición de una dirección para un límite por dirección (N20): una IPv4 tal cual, también si llega mapeada en IPv6,
/// y una IPv6 por su /64, que es lo que un proveedor le da a una casa o a un celular; así rotar adentro de esa red no
/// esquiva el límite. Agrupar por /64 no alcanza contra quien tiene un /48, y cada límite sigue siendo de su copia. La
/// usan los límites HTTP por IP del núcleo y el tope de códigos por dirección de WhatsApp.
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
}
