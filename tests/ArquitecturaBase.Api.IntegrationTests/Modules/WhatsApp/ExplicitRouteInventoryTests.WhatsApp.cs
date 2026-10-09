namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// La parte del módulo WhatsApp de ExplicitRouteInventoryTests: sus doce combinaciones verbo/ruta. Lleva el namespace de
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
        "GET /api/whatsapp/chats",
        "GET /api/whatsapp/chats/{id:guid}/messages",
        "PUT /api/whatsapp/chats/{id:guid}/read",
        "PUT /api/whatsapp/chats/{id:guid}/mode",
        "POST /api/whatsapp/chats/{id:guid}/messages",
        "GET /api/whatsapp/chats/{id:guid}/outgoing/{requestid:guid}",
        "POST /api/whatsapp/chats/{id:guid}/outgoing/{requestid:guid}/review",
    ];

    [Fact]
    public void The_whatsapp_module_adds_its_twelve_routes()
    {
        List<string> routes = [];

        AddModuleRoutes(routes);

        Assert.Equal(12, WhatsAppRoutes.Length);
        Assert.Equal(WhatsAppRoutes, routes.Where(WhatsAppRoutes.Contains));
    }

}
