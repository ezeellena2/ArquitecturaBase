using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Infrastructure.Phones;

namespace ArquitecturaBase.Api.IntegrationTests.Phones;

/// <summary>De unidad, sin base: prueban las reglas del parser contra los datos reales de libphonenumber.</summary>
public sealed class LibPhoneNumberParserTests
{
    private static readonly LibPhoneNumberParser Parser = new();

    [Theory]
    [InlineData("AR", "11 2345-6789", "+5491123456789")]
    [InlineData("AR", "011 15 2345-6789", "+5491123456789")]
    [InlineData("AR", "+54 9 11 2345 6789", "+5491123456789")]
    [InlineData("AR", "+54 11 2345 6789", "+5491123456789")]
    [InlineData("AR", "3416020069", "+5493416020069")]
    [InlineData("AR", "341 15 602 0069", "+5493416020069")]
    [InlineData("AR", "0341 15-602-0069", "+5493416020069")]
    [InlineData("AR", "  11 2345-6789  ", "+5491123456789")]
    public void Argentine_mobiles_are_stored_with_the_9(string country, string number, string expected)
    {
        var result = Parser.Parse(country, number);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Fact]
    public void The_country_is_not_case_sensitive()
    {
        var result = Parser.Parse("ar", "11 2345-6789");

        Assert.True(result.IsSuccess);
        Assert.Equal("+5491123456789", result.Value.Value);
    }

    [Fact]
    public void Numbers_from_other_countries_keep_their_own_rules()
    {
        var result = Parser.Parse("UY", "099 123 456");

        Assert.True(result.IsSuccess);
        Assert.Equal("+59899123456", result.Value.Value);
    }

    [Fact]
    public void An_international_number_does_not_need_a_country()
    {
        // En Estados Unidos no se distingue un fijo de un celular: FIXED_LINE_OR_MOBILE también se acepta.
        var result = Parser.Parse(null, "+1 650 253 0000");

        Assert.True(result.IsSuccess);
        Assert.Equal("+16502530000", result.Value.Value);
    }

    [Theory]
    [InlineData("AR", "123")]
    [InlineData("AR", "abc")]
    [InlineData("AR", null)]
    [InlineData("AR", "")]
    [InlineData("AR", "   ")]
    [InlineData(null, "11 2345-6789")]
    [InlineData("", "11 2345-6789")]
    [InlineData("  ", "11 2345-6789")]
    [InlineData("ZZ", "11 2345-6789")]
    [InlineData("UY", "2915 1234")]
    [InlineData(null, "+54 12345678901234567")]
    [InlineData("AR", "0800 123 4567")]
    [InlineData("AR", "0810 123 4567")]
    public void Anything_that_is_not_a_valid_mobile_is_rejected(string? country, string? number)
    {
        var result = Parser.Parse(country, number);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }

    [Theory]
    [InlineData("BR", "(11) 3456-7890")]
    [InlineData(null, "+55 11 3456 7890")]
    public void The_9_is_only_added_to_argentine_numbers(string? country, string number)
    {
        // Un fijo de San Pablo con "+549" adelante es un celular válido de Buenos Aires: el de otra persona.
        var result = Parser.Parse(country, number);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }

    [Theory]
    [InlineData("AR", "0341 15 6O2 OO69")]
    [InlineData("AR", "3416O2OO69")]
    [InlineData("AR", "+54 9 341 6O2 OO69")]
    [InlineData("AR", "11 2345 6OOO")]
    [InlineData("AR", "341 FLOWERS")]
    public void A_number_with_letters_is_rejected_instead_of_read_as_keypad_digits(string country, string number)
    {
        // Con tres letras o más, la librería las cambia por su dígito del teclado (O = 6): una O en lugar de un cero
        // daría el celular válido de otra persona.
        var result = Parser.Parse(country, number);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }

    [Theory]
    [InlineData("5493413654813", "+5493413654813")]
    [InlineData("543413654813", "+5493413654813")]
    [InlineData(" 5493413654813 ", "+5493413654813")]
    public void WhatsApp_ids_are_read_with_the_same_rules(string waId, string expected)
    {
        var result = Parser.FromWhatsAppId(waId);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData("54934136548ab")]
    [InlineData("+5493413654813")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("1234567")]
    [InlineData("5493413654813000")]
    public void Invalid_WhatsApp_ids_are_rejected(string? waId)
    {
        var result = Parser.FromWhatsAppId(waId);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }

    [Theory]
    [InlineData("+5491123456789", "+54 9 11 •••• 6789")]
    [InlineData("+5493413654813", "+54 9 341 •••• 4813")]
    [InlineData("+59899123456", "+598 99 •••• 3456")]
    [InlineData("+16502530000", "+1 650 •••• 0000")]
    public void Mask_keeps_the_area_code_and_the_last_four_digits(string phone, string expected)
    {
        Assert.Equal(expected, Parser.Mask(PhoneNumber.Create(phone).Value));
    }

    [Theory]
    [InlineData("+6591234567", "+65 •••• 4567")]
    [InlineData("+3725123456", "+372 •••• 3456")]
    [InlineData("+59171234567", "+591 •••• 4567")]
    public void Mask_keeps_only_the_country_code_when_the_area_code_would_show_the_whole_number(string phone, string expected)
    {
        // En Singapur y Estonia, el código de área y los últimos cuatro ya son todo el número. Bolivia no tiene código de área.
        Assert.Equal(expected, Parser.Mask(PhoneNumber.Create(phone).Value));
    }

    [Fact]
    public void Mask_does_not_fail_for_a_number_the_library_does_not_know()
    {
        // +999 no es un código de país asignado, pero es un PhoneNumber válido para Domain.
        var masked = Parser.Mask(PhoneNumber.Create("+99912345678").Value);

        Assert.EndsWith("5678", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("1234", masked, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatInternational_groups_the_number_for_reading()
    {
        Assert.Equal("+54 9 11 2345-6789", Parser.FormatInternational(PhoneNumber.Create("+5491123456789").Value));
    }

    [Fact]
    public void FormatInternational_falls_back_to_the_stored_value_for_a_number_the_library_does_not_know()
    {
        Assert.Equal("+99912345678", Parser.FormatInternational(PhoneNumber.Create("+99912345678").Value));
    }
}
