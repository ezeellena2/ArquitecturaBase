using System.Globalization;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class TestPhones
{
    /// <summary>
    /// Un celular de Córdoba distinto por test, ya en formato internacional: todos los tests comparten la base y el
    /// número tiene índice único. Los últimos 7 dígitos son al azar y alcanzan para buscarlo en el listado. Empiezan
    /// de 2 a 9, como un celular de verdad: los tests del ingreso con WhatsApp los pasan por el parser, que rechaza
    /// uno que empiece con 1.
    /// </summary>
    public static PhoneNumber Unique() =>
        PhoneNumber.Create("+549351" + Random.Shared.Next(2_000_000, 10_000_000).ToString(CultureInfo.InvariantCulture)).Value;

    /// <summary>
    /// El número como lo escribe una persona en Córdoba: con el 0, el código de área, el 15 y un guion. El parser lo
    /// lleva a <paramref name="phone"/>.
    /// </summary>
    public static string AsTypedLocally(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var local = LocalPart(phone);

        return $"0351 15 {local[..3]}-{local[3..]}";
    }

    /// <summary>Los 7 dígitos al azar de <see cref="Unique"/>.</summary>
    public static string LocalPart(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return phone.Value[^7..];
    }
}
