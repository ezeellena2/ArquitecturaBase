namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppPermissions
{
    public const string Read = "whatsapp.read";
    public const string Send = "whatsapp.send";
    public const string ConfigureAssistant = "whatsapp.configureassistant";
    public static IReadOnlyCollection<string> All { get; } = [Read, Send, ConfigureAssistant];
    /// <summary>Lo que incluye cada permiso que no sirve sin el «Ver» de su área (como <c>Permissions.Includes</c>).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Includes { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal) { [Send] = [Read] }.AsReadOnly();
}
