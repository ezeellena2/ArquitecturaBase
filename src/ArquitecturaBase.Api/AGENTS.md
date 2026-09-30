# Api: composición y borde HTTP

`Program.cs` compone capas y ordena el pipeline. `Controllers` adapta casos de uso; `Contracts` define entradas HTTP; `RequestContext` adapta la petición; `Authentication` y `Authorization` conectan sesión y permisos.

## Al modificar

- Los controllers consumen interfaces de servicios de Application. Infrastructure solo se referencia desde `Program.cs` para registrar.
- `Json`, `Localization`, `RateLimiting` y `OpenApi` son componentes del borde: sus opciones comunes se registran en `DependencyInjection.cs`. MVC y HTTP comparten la configuración JSON.
- `ErrorHandling` traduce resultados y errores del framework; `Hosting` conserva el origen único, los headers y el fallback SPA. Leé su guía antes de cambiar el orden del pipeline.
- Un contrato nuevo preserva cuerpos, status, permisos, rate limit y esquemas OpenAPI; agregá la combinación verbo/ruta al inventario y sus pruebas.
- Los endpoints de OpenIddict y Aspire mantienen sus protocolos. Las rutas de negocio usan controllers MVC.

## Referencias

- [Borde HTTP y pipeline](../../docs/architecture/backend.md).
- [Inventario y contratos HTTP](../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts).
- [Pruebas OpenAPI](../../tests/ArquitecturaBase.Api.IntegrationTests/OpenApiTests.cs).
