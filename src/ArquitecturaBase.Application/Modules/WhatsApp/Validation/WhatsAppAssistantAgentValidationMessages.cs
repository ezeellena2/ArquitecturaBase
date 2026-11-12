using System.Globalization;
using System.Resources;
using ArquitecturaBase.Application.Common.Formatting;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Validation;

internal static class WhatsAppAssistantAgentValidationMessages
{
    internal static ResourceManager ResourceManager { get; } = new("ArquitecturaBase.Application.Modules.WhatsApp.Resources.AssistantAgentValidation",
        typeof(WhatsAppAssistantAgentValidationMessages).Assembly);

    public static string TooMany(int maximum) => DisplayNumbers.Format(CultureInfo.CurrentUICulture.Name, Get(nameof(TooMany)), maximum);
    public static string OutOfRange(int minimum, int maximum) => DisplayNumbers.Format(CultureInfo.CurrentUICulture.Name, Get(nameof(OutOfRange)), minimum, maximum);
    public static string Repeated => Get(nameof(Repeated));
    public static string ActionNotAllowed => Get(nameof(ActionNotAllowed));
    public static string ActionsRequired => Get(nameof(ActionsRequired));
    public static string ButtonsRequired => Get(nameof(ButtonsRequired));
    public static string FormRequired => Get(nameof(FormRequired));
    public static string Invalid => Get(nameof(Invalid));
    public static string TooLong => Get(nameof(TooLong));

    private static string Get(string key) => ResourceManager.GetString(key, CultureInfo.CurrentUICulture)
        ?? throw new InvalidOperationException("The assistant agent validation text is missing.");
}
