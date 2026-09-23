using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.UnitTests.Authentication;

public sealed class LoginCodeDestinationTests
{
    [Fact]
    public void An_email_destination_keeps_the_normalized_email()
    {
        var destination = LoginCodeDestination.ForEmail(Email.Create(" Ana@Example.com ").Value);

        Assert.Equal(LoginCodeChannel.Email, destination.Channel);
        Assert.Equal("ana@example.com", destination.Value);
    }

    [Fact]
    public void A_phone_destination_goes_by_whatsapp_with_the_international_number()
    {
        var destination = LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456789").Value);

        Assert.Equal(LoginCodeChannel.WhatsApp, destination.Channel);
        Assert.Equal("+5491123456789", destination.Value);
    }

    [Fact]
    public void Destinations_are_equal_when_channel_and_value_match()
    {
        var email = LoginCodeDestination.ForEmail(Email.Create("ana@example.com").Value);

        Assert.Equal(email, LoginCodeDestination.ForEmail(Email.Create("ANA@example.com").Value));
        Assert.NotEqual(email, LoginCodeDestination.ForEmail(Email.Create("beto@example.com").Value));
        Assert.NotEqual(
            LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456789").Value),
            LoginCodeDestination.ForPhone(PhoneNumber.Create("+5491123456780").Value));
    }

    [Fact]
    public void Destinations_require_a_value()
    {
        Assert.Throws<ArgumentNullException>(() => LoginCodeDestination.ForEmail(null!));
        Assert.Throws<ArgumentNullException>(() => LoginCodeDestination.ForPhone(null!));
    }
}
