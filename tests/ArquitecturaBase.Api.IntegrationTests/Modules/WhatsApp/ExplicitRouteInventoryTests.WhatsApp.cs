using ArquitecturaBase.Api.IntegrationTests.TestFeatures;

namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// La parte del módulo WhatsApp de ExplicitRouteInventoryTests: sus veintisiete combinaciones verbo/ruta, siete de ellas sólo
/// con el módulo de formularios. Lleva el namespace de la clase del núcleo, no el de su carpeta, para ser otra parte de la
/// misma clase.
/// </summary>
public sealed partial class ExplicitRouteInventoryTests
{
    internal static readonly string[] WhatsAppRoutes =
    [
        "POST /account/login-code/whatsapp",
        "GET /api/me/whatsapp",
        "POST /api/me/whatsapp/code",
        "PUT /api/me/whatsapp",
        "GET /webhooks/whatsapp",
        "POST /webhooks/whatsapp",
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions",
        "GET /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}",
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}/discard",
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}/send",
        "GET /api/whatsapp/chats",
        "GET /api/whatsapp/chats/{id:guid}/messages",
        "POST /api/whatsapp/chats/{contactid:guid}/messages/{messageid:guid}/audio-transcription",
        "GET /api/whatsapp/chats/{contactid:guid}/messages/{messageid:guid}/audio",
        "GET /api/whatsapp/chats/{id:guid}/forms",
        "POST /api/whatsapp/chats/{id:guid}/form-messages",
        "GET /api/whatsapp/chats/{id:guid}/forms/{definitionid:guid}/versions/{versionid:guid}/preparation",
        "PUT /api/whatsapp/chats/{id:guid}/read",
        "PUT /api/whatsapp/chats/{id:guid}/mode",
        "POST /api/whatsapp/chats/{id:guid}/messages",
        "GET /api/whatsapp/chats/{id:guid}/outgoing/{requestid:guid}",
        "POST /api/whatsapp/chats/{id:guid}/outgoing/{requestid:guid}/review",
        "GET /api/whatsapp/assistant-agent",
        "PUT /api/whatsapp/assistant-agent",
        "POST /api/whatsapp/assistant-agent/tests",
        "GET /api/whatsapp/assistant-agent/tests/{requestid:guid}",
        "GET /api/whatsapp/assistant-agent/test-recipients",
    ];

    /// <summary>
    /// Las de <c>WhatsAppRouteFeature.Forms</c>: el módulo las mapea sólo con formularios instalado. Su señal en los tests es
    /// el publicador opcional, el mismo que declaran con <c>Assert.SkipUnless</c> las composiciones que lo necesitan.
    /// </summary>
    internal static readonly string[] WhatsAppFormRoutes =
    [
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions",
        "GET /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}",
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}/discard",
        "POST /api/whatsapp/chats/{id:guid}/assistant-suggestions/{requestid:guid}/send",
        "GET /api/whatsapp/chats/{id:guid}/forms",
        "POST /api/whatsapp/chats/{id:guid}/form-messages",
        "GET /api/whatsapp/chats/{id:guid}/forms/{definitionid:guid}/versions/{versionid:guid}/preparation",
    ];

    internal static IReadOnlyCollection<string> InstalledWhatsAppRoutes => typeof(ExplicitRouteInventoryTests).Assembly.GetTypes()
        .Any(type => type is { IsClass: true, IsAbstract: false } && typeof(IStructuredFormPublicationDriver).IsAssignableFrom(type))
        ? WhatsAppRoutes
        : [.. WhatsAppRoutes.Except(WhatsAppFormRoutes)];

    [Fact]
    public void The_whatsapp_module_adds_its_twenty_seven_routes_and_the_form_ones_only_with_the_forms_publisher()
    {
        List<string> routes = [];

        AddModuleRoutes(routes);

        Assert.Equal(27, WhatsAppRoutes.Length);
        Assert.Subset(WhatsAppRoutes.ToHashSet(), WhatsAppFormRoutes.ToHashSet());
        Assert.Equal(InstalledWhatsAppRoutes, routes.Where(WhatsAppRoutes.Contains));
    }

}
