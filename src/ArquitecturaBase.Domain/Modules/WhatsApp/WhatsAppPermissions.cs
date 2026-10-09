namespace ArquitecturaBase.Domain.Modules.WhatsApp;

public static class WhatsAppPermissions
{
    public const string Read = "whatsapp.read";
    public const string Send = "whatsapp.send";
    public const string Manage = "whatsapp.manage";
    public static IReadOnlyCollection<string> All { get; } = [Read, Send, Manage];
}
