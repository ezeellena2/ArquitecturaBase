# Controllers: adaptación de casos de uso

Recibe contratos HTTP, mapea pedidos y devuelve resultados de los servicios de Application.

## Lectura obligatoria

Antes de tocar un controller, leé el [borde HTTP](../../../docs/architecture/backend.md#borde-http) y el documento de su área:

- `LoginCode`, `LoginLink`, `LoginMethods`, `ExternalLogin` y `Connect` (`/account` y `/connect`): [`docs/features/identidad.md`](../../../docs/features/identidad.md).
- `Me` (el perfil): [`identidad.md`](../../../docs/features/identidad.md) para el correo y sus códigos, y [`administracion.md`](../../../docs/features/administracion.md) para desvincular el número (`DELETE /api/me/whatsapp`, del núcleo, sin cerrar sesiones).
- Los controllers de WhatsApp (el webhook, el código de ingreso por WhatsApp y el pedido de código y la confirmación del número del perfil, `POST /api/me/whatsapp/code` y `PUT /api/me/whatsapp`) están en `Api/Modules/WhatsApp/Controllers`, con lo demás del módulo: [`docs/features/whatsapp.md`](../../../docs/features/whatsapp.md).
- `Users`, `Roles`, `Settings` y `Permissions`: [`docs/features/administracion.md`](../../../docs/features/administracion.md); el número de una cuenta sigue además `whatsapp.md`.

## Al modificar

- Sin EF, repositorios, lectores ni helpers de negocio inyectados; las acciones consumen interfaces de servicios.
- Usá `ToActionResult`, `ToAcceptedResult` o `ToCreatedResult`; declarás permisos y ProblemDetails según el contrato.
- No abrir transacciones ni duplicar validación funcional. Los endpoints de protocolo conservan su framework.
- Una ruta nueva amplía inventario, casos 401/403, cuerpos, errores y OpenAPI.

## Verificación y ejemplos

- [Inventario explícito](../../../tests/ArquitecturaBase.Api.IntegrationTests/Contracts/ExplicitRouteInventoryTests.cs).
- [Controller de referencia](../../../docs/guides/agregar-un-area.md).
