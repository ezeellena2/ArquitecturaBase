using ArquitecturaBase.Application.Channels;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Channels;

/// <summary>
/// El canal telefónico del núcleo, sin ningún módulo que lo dé: apagado, sin países ni número, y acepta un número de
/// cualquier país (sin canal no hay tarifa por país; decisión 4 de la Etapa 6).
/// </summary>
public sealed class DisabledPhoneChannelTests
{
    [Fact]
    public void It_is_off_with_neither_countries_nor_a_number()
    {
        var channel = new DisabledPhoneChannel();

        Assert.False(channel.IsEnabled);
        Assert.Empty(channel.Countries);
        Assert.Null(channel.DisplayNumber);
    }

    [Theory]
    [InlineData("+59899123456")]
    [InlineData("+5493515550101")]
    public void It_accepts_a_number_from_any_country(string phone)
    {
        Assert.True(new DisabledPhoneChannel().EnsureCanSendTo(PhoneNumber.Create(phone).Value).IsSuccess);
    }
}
