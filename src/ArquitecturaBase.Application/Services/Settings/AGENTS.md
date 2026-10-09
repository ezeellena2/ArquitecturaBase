# Settings: ajustes del sistema

`SystemSettingsService` consulta y modifica la política persistida del sistema.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).

## Al modificar

- La validación se ejecuta antes del límite; la escritura usa el repositorio y un único commit.
- El PATCH admite uno o más ajustes: valida todo el pedido antes de modificar y aplica el conjunto con una sola revisión. Conservá el lock y expectedRevision, sin guardar por campo.
- El descarte del caché se hace después de confirmar. Una falla de Redis no convierte el cambio confirmado en rollback.
- Los valores de configuración inicial son del seed, no una segunda fuente para lecturas de negocio.

## Verificación y ejemplos

- [Pruebas de ajustes](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Settings).
- [Pruebas HTTP y persistencia](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Settings).

- Apariencia guarda cuatro colores #RRGGBB como un grupo con una sola revisión.
  Validar antes de mutar; normalizar a mayúsculas. La proyección pública expone la paleta
  para todo el SPA, con la invalidación de caché existente después del commit.
