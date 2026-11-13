using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAccessReplyPolicyTests
{
    [Theory]
    [InlineData("es", "WANT_TO_ENTER", "login", "Iniciar sesión")]
    [InlineData("en", "HAVE_ACCOUNT", "login", "Sign in")]
    [InlineData("es", "CREATE_ACCOUNT", "registro", "Crear cuenta")]
    [InlineData("en", "CREATE_ACCOUNT", "registro", "Create account")]
    public async Task Public_orientation_uses_installation_language_and_contains_no_credentials(string culture, string action, string path, string label)
    {
        var settings = new FakeSystemSettingsReader();
        settings.Presentation = settings.Presentation with { DefaultCulture = culture };
        var policy = new WhatsAppAccessReplyPolicy(new FakePublicOrigin(new Uri("https://app.test/")), settings);
        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(await policy.DecideAsync(
            PhoneNumber.Create("+5493413654813").Value, action, TestContext.Current.CancellationToken));
        Assert.Equal("https://app.test/" + path, reply.Url);
        Assert.Equal(label, reply.ButtonText);
        Assert.DoesNotContain("https://", reply.SafeSummary, StringComparison.Ordinal);
        Assert.Equal(WhatsAppMessagePurpose.Access, reply.Purpose);
        Assert.Equal(WhatsAppAccessEventKind.AccessInstructions, reply.AccessEventKind);
    }

    /// <summary>
    /// H18: the button of a new invitation carries its language, so the link goes out in the language of the invitation without
    /// reading accounts; a button already sent, without it, keeps the language of the installation.
    /// </summary>
    [Theory]
    [InlineData("es", "WANT_TO_ENTER:en", "Sign in", "To sign in, open the website and request a code through WhatsApp. Then enter it on the website.")]
    [InlineData("en", "WANT_TO_ENTER:es", "Iniciar sesión", "Para ingresar, abrí la web y pedí un código por WhatsApp. Después ingresalo en la web.")]
    public async Task The_button_of_a_new_invitation_answers_in_the_language_it_carries(string installation, string action, string label, string text)
    {
        var settings = new FakeSystemSettingsReader();
        settings.Presentation = settings.Presentation with { DefaultCulture = installation };
        var policy = new WhatsAppAccessReplyPolicy(new FakePublicOrigin(new Uri("https://app.test/")), settings);

        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(await policy.DecideAsync(
            PhoneNumber.Create("+5493413654813").Value, action, TestContext.Current.CancellationToken));

        Assert.Equal("https://app.test/login", reply.Url);
        Assert.Equal(label, reply.ButtonText);
        Assert.Equal(text, reply.Body);
    }

    [Theory]
    [InlineData("WANT_TO_ENTER:fr")]
    [InlineData("HAVE_ACCOUNT:en")]
    [InlineData("DID_NOT_REQUEST_CODE")]
    public async Task A_payload_that_does_not_ask_for_instructions_is_not_answered(string action)
    {
        var policy = new WhatsAppAccessReplyPolicy(new FakePublicOrigin(new Uri("https://app.test/")), new FakeSystemSettingsReader());

        await Assert.ThrowsAsync<ArgumentException>(() => policy.DecideAsync(
            PhoneNumber.Create("+5493413654813").Value, action, TestContext.Current.CancellationToken));
    }
}
