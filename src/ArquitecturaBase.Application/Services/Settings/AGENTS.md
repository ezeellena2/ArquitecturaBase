# Settings: ajustes del sistema

`SystemSettingsService` consulta y modifica la política persistida del sistema.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).

## Al modificar

- La validación se ejecuta antes del límite; la escritura usa el repositorio y un único commit.
- El PATCH admite uno o más ajustes: valida todo el pedido antes de modificar y aplica el conjunto con una sola revisión. Conservá el lock y expectedRevision, sin guardar por campo.
- Un cambio del modo de registro deja su historia con `AdministrativeChangeRecorder` en el mismo límite, sólo si el cambio pasó y el modo es otro (N13, [administración](../../../../docs/features/administracion.md)).
- El descarte del caché se hace después de confirmar. Una falla de Redis no convierte el cambio confirmado en rollback.
- Los valores de configuración inicial son del seed, no una segunda fuente para lecturas de negocio.

## Verificación y ejemplos

- [Pruebas de ajustes](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Settings).
- [Pruebas HTTP y persistencia](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Settings).

- Apariencia guarda cinco colores #RRGGBB y la letra como un grupo con una sola revisión.
  Validar antes de mutar; normalizar a mayúsculas. La proyección pública expone la paleta, con el acento efectivo,
  para todo el SPA, con la invalidación de caché existente después del commit.
- `branding` y `retention` siguen la misma regla. Quien los usa los lee por `ISystemSettingsReader` en cada uso (correos,
  remitente, retención del ingreso), nunca de opciones. La proyección pública sólo publica el nombre y las versiones del logo y
  del ícono (`brand`, ME7); el remitente y los plazos, no.
- `BrandImageService` sube y quita el logo y el ícono (ME2b): valida el pedido, revisa los bytes con `IImageInspector`, escribe
  el archivo con clave nueva antes del límite y guarda la clave con el lock, la revisión y un commit; descarta el caché
  después. Abrirlos para servirlos lee la fila por el repositorio.
