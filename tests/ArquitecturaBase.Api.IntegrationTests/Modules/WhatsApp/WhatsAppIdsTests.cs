using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Infrastructure.Phones;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

/// <summary>
/// De unidad, sin base: el <c>wa_id</c> que manda Meta, leído con el parser real (libphonenumber) y las mismas reglas que
/// un número que tipea una persona. Eran casos de LibPhoneNumberParserTests, cuando el parser del núcleo sabía leer un
/// <c>wa_id</c>.
/// </summary>
public sealed class WhatsAppIdsTests
{
    private static readonly LibPhoneNumberParser Parser = new();

    [Theory]
    [InlineData("5493413654813", "+5493413654813")]
    [InlineData("543413654813", "+5493413654813")]
    [InlineData(" 5493413654813 ", "+5493413654813")]
    public void WhatsApp_ids_are_read_with_the_same_rules(string waId, string expected)
    {
        var result = WhatsAppIds.Parse(Parser, waId);

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
        var result = WhatsAppIds.Parse(Parser, waId);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PhoneInvalidCode, result.Error.Code);
    }
}
