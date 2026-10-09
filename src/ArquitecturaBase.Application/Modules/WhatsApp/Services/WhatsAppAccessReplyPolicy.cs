using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>Public instructions only. Identity is verified by the explicit web flow.</summary>
internal sealed class WhatsAppAccessReplyPolicy(IPublicOrigin publicOrigin, ISystemSettingsReader settings)
{
    public async Task<WhatsAppOutboundMessage> DecideAsync(PhoneNumber phone, string replyId, CancellationToken cancellationToken)
    {
        if (replyId is not (WhatsAppAccessActions.WantToEnter or WhatsAppAccessActions.HaveAccount or WhatsAppAccessActions.CreateAccount))
        {
            throw new ArgumentException("The action does not request public access instructions.", nameof(replyId));
        }

        var culture = CultureInfo.GetCultureInfo((await settings.FindPresentationAsync(cancellationToken)).DefaultCulture);
        var register = replyId == WhatsAppAccessActions.CreateAccount;
        var origin = publicOrigin.Value ?? throw new InvalidOperationException("Public access instructions require a configured public origin.");
        return new WhatsAppLinkButtonMessage(phone,
            AccessTexts.Get(register ? "RegisterInstructions" : "SignInInstructions", culture),
            AccessTexts.Get(register ? "RegisterButton" : "SignInButton", culture),
            new Uri(origin, register ? "registro" : "login").AbsoluteUri);
    }
}
