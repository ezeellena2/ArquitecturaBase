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
}
