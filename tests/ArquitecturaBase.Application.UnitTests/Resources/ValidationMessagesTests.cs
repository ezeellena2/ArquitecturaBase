using ArquitecturaBase.Application.Resources;

namespace ArquitecturaBase.Application.UnitTests.Resources;

public sealed class ValidationMessagesTests
{
    [Fact]
    public void Messages_are_in_spanish_by_default()
    {
        using var scope = new CultureScope("es");

        Assert.Equal("Este campo es obligatorio.", ValidationMessages.Required);
        Assert.Equal("Ingresá un correo válido.", ValidationMessages.EmailInvalid);
    }

    [Fact]
    public void Messages_are_translated_to_english()
    {
        using var scope = new CultureScope("en");

        Assert.Equal("This field is required.", ValidationMessages.Required);
        Assert.Equal("Enter a valid email address.", ValidationMessages.EmailInvalid);
    }
}
