# Identity: puertos de cuenta y sesión

Expresa operaciones de cuentas, roles, sesión, origen público y login externo, con puertos separados de consulta e invalidación de permisos.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../../../docs/features/identidad.md).

## Al modificar

- Los contratos usan datos de Application y resultados de Domain, sin tipos de UserManager, cookies ni protocolo HTTP.
- Las operaciones de guardado técnico de Identity participan del límite del llamador; el puerto no introduce un caso de uso nuevo.
- `IPermissionService` sólo consulta permisos y puede usarse desde capacidades de lectura. `IPermissionCacheInvalidator.InvalidateRoleAsync` se invoca después del commit; las membresías y revisión confirmada preservan permisos actuales. Los puertos no se heredan entre sí.

## Verificación y ejemplos

- [Integraciones y guardado técnico](../../../../../docs/architecture/backend.md).
- [Pruebas de adaptadores](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Identity).
