namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// La parte del módulo WhatsApp de ExplicitRouteInventoryTests: sus veintiuna combinaciones verbo/ruta. Lleva el namespace de
/// la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase.
/// </summary>
public sealed partial class ExplicitRouteInventoryTests
{
    internal static readonly string[] WhatsAppRoutes =
    [
        "POST /account/login-code/whatsapp",
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
    ];

    [Fact]
    public void The_whatsapp_module_adds_its_twenty_one_routes()
    {
        List<string> routes = [];

        AddModuleRoutes(routes);

        Assert.Equal(21, WhatsAppRoutes.Length);
        Assert.Equal(WhatsAppRoutes, routes.Where(WhatsAppRoutes.Contains));
    }

}
