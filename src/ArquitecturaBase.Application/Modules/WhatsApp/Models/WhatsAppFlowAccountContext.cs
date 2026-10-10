namespace ArquitecturaBase.Application.Modules.WhatsApp.Models;

public sealed record WhatsAppFlowAccountContext(string WabaId, string PhoneNumberId, string GraphApiVersion, string ConfigurationFingerprint)
{
    public bool HasSameIdentity(WhatsAppFlowAccountContext other) =>
        WabaId == other.WabaId && PhoneNumberId == other.PhoneNumberId && GraphApiVersion == other.GraphApiVersion;

    public override string ToString() => "WhatsAppFlowAccountContext { Content = [REDACTED] }";
}
