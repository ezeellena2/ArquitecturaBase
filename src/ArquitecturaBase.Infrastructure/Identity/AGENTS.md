# Identity: adaptación de cuentas y permisos

Implementa los puertos de cuentas, roles, sesión y consulta de permisos con ASP.NET Identity. Mantiene entidades técnicas, stores, seguridad de cookies y permisos compartidos; el protocolo vive en `OpenIddict`.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- UserManager y RoleManager usan el contexto scoped del límite. Los lectores y repositorios encapsulan consultas de negocio.
- `PermissionService` (scoped) lee los permisos de la base sin caché, una consulta por persona y por pedido con el contexto del pedido, y los memoriza en ese scope ([ADR 0032](../../../docs/decisions/0032-sin-redis-la-app-sigue.md)). No agregarle caché ni otra conexión: adentro de un límite lee en su transacción.
- No registrar claims sensibles, códigos ni tokens. Una falla del proveedor conserva el tratamiento técnico del borde.
- Los cambios de sesión requieren probar revocación, bloqueo, deshabilitado y cambios de roles.

## Verificación y ejemplos

- [Pruebas del adaptador](../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity).
- [Pruebas de sesión](../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth).
- [Permisos sin caché](../../../docs/features/identidad.md).
