namespace ArquitecturaBase.Api.Routing;

public enum WhatsAppRouteFeature
{
    Messaging,
    Webhook,
}

/// <summary>
/// Marks an MVC action or controller whose routes exist only when the corresponding WhatsApp feature is available.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class WhatsAppRouteAttribute(WhatsAppRouteFeature feature) : Attribute
{
    public WhatsAppRouteFeature Feature { get; } = feature;
}
