using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence;

/// <summary>
/// H16 (ADR 0030): the content of the chat is kept encrypted with Data Protection, under its own purpose and not bound to its row.
/// EF builds the model once per process, so its converters cannot take the protector from DI: the copy fixes it when it starts,
/// the first one and before any worker (<see cref="WhatsAppChatContentStartup"/>); all the copies share the key ring in the
/// database. Data Protection never encrypts the same way twice, so the content is never searched or compared in SQL, only asked
/// whether it is null. Content the key ring no longer opens (lost keys, a database restored elsewhere) reads as empty, as if the
/// retention had emptied it, instead of breaking the load of its row.
/// </summary>
internal static class WhatsAppChatContent
{
    public const string Purpose = "ArquitecturaBase.WhatsApp.ChatContent.v1";

    private static IDataProtector? protector;

    // EF never hands a null to a converter: a null stays null in the column.
    public static ValueConverter<string?, byte[]> Text { get; } = new(value => Protect(value!), value => Unprotect(value));

    // Six decimals, as the numeric(9,6) column of before kept a point.
    public static ValueConverter<decimal?, byte[]> Number { get; } = new(
        value => Protect(value!.Value.ToString("F6", CultureInfo.InvariantCulture)), value => ParseNumber(Unprotect(value)));

    public static void Use(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        Interlocked.CompareExchange(ref protector, provider.CreateProtector(Purpose), null);
    }

    private static IDataProtector Protector =>
        Volatile.Read(ref protector) ?? throw new InvalidOperationException("The WhatsApp chat content was used before its protector was set.");

    private static byte[] Protect(string value) => Protector.Protect(Encoding.UTF8.GetBytes(value));

    private static string? Unprotect(byte[] value)
    {
        try
        {
            return Encoding.UTF8.GetString(Protector.Unprotect(value));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static decimal? ParseNumber(string? value) => value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
}
