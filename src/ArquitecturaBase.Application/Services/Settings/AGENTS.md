# Settings: ajustes del sistema

`SystemSettingsService` consulta y modifica la política persistida del sistema.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).

## Al modificar

- La validación se ejecuta antes del límite; la escritura usa el repositorio y un único commit.
- El descarte del caché se hace después de confirmar. Una falla de Redis no convierte el cambio confirmado en rollback.
- Los valores de configuración inicial son del seed, no una segunda fuente para lecturas de negocio.

## Verificación y ejemplos

- [Pruebas de ajustes](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Settings).
- [Pruebas HTTP y persistencia](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Settings).
