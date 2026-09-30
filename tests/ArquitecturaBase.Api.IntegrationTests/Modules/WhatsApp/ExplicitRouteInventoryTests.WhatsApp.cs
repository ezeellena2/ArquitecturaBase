namespace ArquitecturaBase.Api.IntegrationTests.Contracts;

/// <summary>
/// La parte del módulo WhatsApp de ExplicitRouteInventoryTests: sus cinco combinaciones verbo/ruta. Lleva el namespace de
/// la clase del núcleo, no el de su carpeta, para ser otra parte de la misma clase.
/// </summary>
public sealed partial class ExplicitRouteInventoryTests
{
    private static readonly string[] WhatsAppRoutes =
    [
        "POST /account/login-code/whatsapp",
        "POST /api/me/whatsapp/code",
        "PUT /api/me/whatsapp",
        "GET /webhooks/whatsapp",
        "POST /webhooks/whatsapp",
    ];

    [Fact]
    public void The_whatsapp_module_adds_its_five_routes()
    {
        List<string> routes = [];

        AddModuleRoutes(routes);

        Assert.Equal(5, WhatsAppRoutes.Length);
        Assert.Equal(WhatsAppRoutes, routes);
    }

    static partial void AddModuleRoutes(List<string> routes) => routes.AddRange(WhatsAppRoutes);
}
