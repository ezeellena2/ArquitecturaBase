# Identity: puertos de cuenta y sesión

Expresa operaciones de cuentas, roles, sesión, origen público y login externo, y la consulta de permisos.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../../../docs/features/identidad.md).

## Al modificar

- Los contratos usan datos de Application y resultados de Domain, sin tipos de UserManager, cookies ni protocolo HTTP.
- Las operaciones de guardado técnico de Identity participan del límite del llamador; el puerto no introduce un caso de uso nuevo.
- `IPermissionService` sólo consulta permisos y puede usarse desde capacidades de lectura. No tiene caché: un cambio confirmado vale desde el pedido siguiente, sin invalidar nada ([ADR 0032](../../../../../docs/decisions/0032-sin-redis-la-app-sigue.md)).

## Verificación y ejemplos

- [Integraciones y guardado técnico](../../../../../docs/architecture/backend.md).
- [Pruebas de adaptadores](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity).
