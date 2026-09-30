# Identity: adaptación de cuentas y permisos

Implementa los puertos de cuentas, roles y sesión con ASP.NET Identity. Mantiene entidades técnicas, stores, seguridad de cookies y permisos compartidos; el protocolo vive en `OpenIddict`.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../docs/features/identidad.md).

## Al modificar

- UserManager y RoleManager usan el contexto scoped del límite. Los lectores y repositorios encapsulan consultas de negocio.
- Una actualización de permisos preserva `ConcurrencyStamp`. El servicio lee membresías y revisión confirmadas; el caché pertenece a esa versión.
- No registrar claims sensibles, códigos ni tokens. Una falla del proveedor conserva el tratamiento técnico del borde.
- Los cambios de sesión requieren probar revocación, bloqueo, deshabilitado y cambios de roles.

## Verificación y ejemplos

- [Pruebas del adaptador](../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity).
- [Pruebas de sesión](../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth).
- [Caché de permisos](../../../docs/guides/agregar-cache.md).
