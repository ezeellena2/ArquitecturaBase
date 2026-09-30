# Identity: datos independientes del proveedor

Describe cuenta, proveedores de ingreso externo y proyecciones de permisos sin depender de ASP.NET Identity.

## Lectura obligatoria

El ingreso externo (`ExternalLogin`) sigue [`docs/features/identidad.md`](../../../../docs/features/identidad.md).

## Al modificar

- Conservá nulabilidad de correo y teléfono y el significado de verificaciones y medios de ingreso.
- `RolePermissionsRow` une la revisión confirmada del rol con sus permisos; no completar esos valores desde lecturas o transacciones diferentes.
- No exponer entidades de Infrastructure ni tipos del SDK por estos modelos.

## Verificación y ejemplos

- [Permisos y revisiones](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity/PermissionServiceTests.cs).
