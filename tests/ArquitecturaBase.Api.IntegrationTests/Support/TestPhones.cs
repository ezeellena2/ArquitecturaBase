using System.Globalization;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Api.IntegrationTests.Support;

internal static class TestPhones
{
    /// <summary>
    /// Un celular de Córdoba distinto por test, ya en formato internacional: todos los tests comparten la base y el
    /// número tiene índice único. Los últimos 7 dígitos son al azar y alcanzan para buscarlo en el listado.
    /// </summary>
    public static PhoneNumber Unique() =>
        PhoneNumber.Create("+549351" + Random.Shared.Next(1_000_000, 10_000_000).ToString(CultureInfo.InvariantCulture)).Value;

    /// <summary>Los 7 dígitos al azar de <see cref="Unique"/>.</summary>
    public static string LocalPart(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return phone.Value[^7..];
    }
}
