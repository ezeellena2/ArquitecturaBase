using System.Diagnostics.CodeAnalysis;
using ArquitecturaBase.Application.Abstractions.Phones;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using PhoneNumbers;
using ParsedPhoneNumber = PhoneNumbers.PhoneNumber;
using PhoneNumber = ArquitecturaBase.Domain.ValueObjects.PhoneNumber;

namespace ArquitecturaBase.Infrastructure.Phones;

/// <summary>
/// <see cref="IPhoneNumberParser"/> sobre libphonenumber, que trae las reglas de numeración de cada país:
/// el 0 y el 15, las longitudes y qué rangos son celulares.
/// </summary>
internal sealed class LibPhoneNumberParser : IPhoneNumberParser
{
    private const int ArgentinaCountryCode = 54;
    private const string ArgentineMobileToken = "9";
    private const int MinWhatsAppIdDigits = 8;
    private const int MaxWhatsAppIdDigits = 15;
    private const int VisibleDigits = 4;
    private const string HiddenDigits = "••••";
    private const string UnknownRegion = "ZZ";

    private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

    public Result<PhoneNumber> Parse(string? country, string? number)
    {
        var input = number?.Trim();

        // Con tres letras o más, la librería las lee como un número "vanity" y las cambia por su dígito del teclado:
        // una O en lugar de un cero daría el celular válido de otra persona.
        if (string.IsNullOrEmpty(input) || input.Any(char.IsLetter))
        {
            return UserErrors.PhoneInvalid;
        }

        if (input.StartsWith('+'))
        {
            return Interpret(input, region: null);
        }

        // Un número sin "+" es nacional, y el país dice cómo leerlo. La librería solo conoce las regiones en mayúsculas.
        var region = country?.Trim().ToUpperInvariant();

        if (string.IsNullOrEmpty(region) || !Util.GetSupportedRegions().Contains(region))
        {
            return UserErrors.PhoneInvalid;
        }

        return Interpret(input, region);
    }

    public Result<PhoneNumber> FromWhatsAppId(string? waId)
    {
        var digits = waId?.Trim();

        if (string.IsNullOrEmpty(digits)
            || digits.Length is < MinWhatsAppIdDigits or > MaxWhatsAppIdDigits
            || digits.AsSpan().ContainsAnyExceptInRange('0', '9'))
        {
            return UserErrors.PhoneInvalid;
        }

        return Interpret("+" + digits, region: null);
    }

    public string Mask(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        var lastDigits = phone.Value[^VisibleDigits..];

        // Un PhoneNumber válido para Domain puede tener un código de país que la librería no conoce.
        if (!TryParse(phone.Value, region: null, out var number))
        {
            return $"{HiddenDigits} {lastDigits}";
        }

        var countryCode = $"+{number.CountryCode}";
        var destinationCodeLength = Util.GetLengthOfNationalDestinationCode(number);

        // Si el código de área y los últimos dígitos ya son todo el número, no quedaría nada tapado.
        if (destinationCodeLength == 0
            || destinationCodeLength + VisibleDigits >= Util.GetNationalSignificantNumber(number).Length)
        {
            return $"{countryCode} {HiddenDigits} {lastDigits}";
        }

        return $"{countryCode} {FormattedDestinationCode(number, destinationCodeLength)} {HiddenDigits} {lastDigits}";
    }

    public string FormatInternational(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        return TryParse(phone.Value, region: null, out var number)
            ? Util.Format(number, PhoneNumberFormat.INTERNATIONAL)
            : phone.Value;
    }

    public string? RegionOf(PhoneNumber phone)
    {
        ArgumentNullException.ThrowIfNull(phone);

        if (!TryParse(phone.Value, region: null, out var number))
        {
            return null;
        }

        // La librería devuelve "ZZ" si no sabe de qué país es, y "001" para los códigos que no son de ningún país
        // (+800, +882): ninguno de los dos es un país al que se le pueda permitir o no mandar códigos.
        var region = Util.GetRegionCodeForNumber(number);

        return region is { Length: 2 } && region != UnknownRegion ? region : null;
    }

    private static Result<PhoneNumber> Interpret(string input, string? region)
    {
        if (!TryParse(input, region, out var number))
        {
            return UserErrors.PhoneInvalid;
        }

        if (number.CountryCode == ArgentinaCountryCode && ValidType(number) != PhoneNumberType.MOBILE)
        {
            number = WithArgentineMobileToken(number) ?? number;
        }

        // Un número de WhatsApp es un celular. Donde fijos y celulares no se distinguen (Estados Unidos) vale también.
        if (ValidType(number) is not (PhoneNumberType.MOBILE or PhoneNumberType.FIXED_LINE_OR_MOBILE))
        {
            return UserErrors.PhoneInvalid;
        }

        return PhoneNumber.Create(Util.Format(number, PhoneNumberFormat.E164));
    }

    /// <summary>
    /// Mucha gente escribe el celular argentino sin el 9, y a veces sin el 15, y así la librería lo lee como un fijo.
    /// Si con el 9 adelante es un celular válido, es ese: así llega de WhatsApp.
    /// </summary>
    private static ParsedPhoneNumber? WithArgentineMobileToken(ParsedPhoneNumber number)
    {
        var candidate = $"+{ArgentinaCountryCode}{ArgentineMobileToken}{Util.GetNationalSignificantNumber(number)}";

        return TryParse(candidate, region: null, out var mobile) && ValidType(mobile) == PhoneNumberType.MOBILE
            ? mobile
            : null;
    }

    /// <summary>El tipo de un número válido, o UNKNOWN si no lo es.</summary>
    private static PhoneNumberType ValidType(ParsedPhoneNumber number) =>
        Util.IsValidNumber(number) ? Util.GetNumberType(number) : PhoneNumberType.UNKNOWN;

    /// <summary>
    /// El código de destino tal como lo agrupa el formato internacional: "9 11" en "+54 9 11 2345-6789".
    /// Para los celulares argentinos, la librería cuenta el 9 dentro del código.
    /// </summary>
    private static string FormattedDestinationCode(ParsedPhoneNumber number, int length)
    {
        var international = Util.Format(number, PhoneNumberFormat.INTERNATIONAL);
        var national = international.AsSpan($"+{number.CountryCode}".Length).TrimStart();

        var end = 0;

        for (var digits = 0; end < national.Length && digits < length; end++)
        {
            if (char.IsAsciiDigit(national[end]))
            {
                digits++;
            }
        }

        return national[..end].ToString();
    }

    private static bool TryParse(string input, string? region, [NotNullWhen(true)] out ParsedPhoneNumber? number)
    {
        try
        {
            number = Util.Parse(input, region);
            return true;
        }
        catch (NumberParseException)
        {
            number = null;
            return false;
        }
    }
}
