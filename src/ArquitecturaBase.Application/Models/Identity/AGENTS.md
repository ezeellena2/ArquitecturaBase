# Identity: datos independientes del proveedor

Describe cuenta y proveedores de ingreso externo sin depender de ASP.NET Identity. Los permisos no tienen proyección acá: `IPermissionReader` devuelve sus códigos ([ADR 0032](../../../../docs/decisions/0032-sin-redis-la-app-sigue.md)).

## Lectura obligatoria

El ingreso externo (`ExternalLogin`) sigue [`docs/features/identidad.md`](../../../../docs/features/identidad.md).

## Al modificar

- Conservá nulabilidad de correo y teléfono y el significado de verificaciones y medios de ingreso.
- No exponer entidades de Infrastructure ni tipos del SDK por estos modelos.

## Verificación y ejemplos

- [Cuentas y vínculos externos](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity/SignInServiceTests.cs).
