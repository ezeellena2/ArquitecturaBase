# Settings: ajustes funcionales

Define la entidad de ajustes del sistema, el modo de registro y sus errores. Es la política persistida que consume la aplicación.

## Al modificar

- `SystemSettings` tiene una fila singleton: conservá su Id estable y modificá sus valores mediante sus métodos.
- `Update` aplica un conjunto de ajustes y avanza una revisión; las reglas puras se comprueban antes de modificar cualquier valor. Application valida zona IANA y enum.
- Una opción inicial de configuración solo siembra la fila ausente; las lecturas posteriores toman la política de la base.
- Un modo nuevo impacta validación, seed, permisos de configuración y el contrato del front; mantené los nombres del enum estables.

## Referencias

- [Política de configuración](../../../docs/features/administracion.md).
- [Pruebas de ajustes](../../../tests/ArquitecturaBase.Domain.UnitTests/Settings).

- Apariencia guarda cinco colores #RRGGBB y la letra como un grupo con una sola revisión.
  Los colores iniciales Campo están en AppearancePalette; migrar defaults anteriores conserva grupos personalizados.
  Validar antes de mutar; normalizar a mayúsculas. El acento del menú (ME3-B) da 4,5:1 contra el menú; nulo es «el
  derivado del menú» y `EffectiveMenuAccentColor` lo resuelve. La letra es un código de `AppearanceFont` (ME4).
- El nombre y el remitente (`SystemBranding`) y los plazos del ingreso (`IdentityRetentionPeriods`) van como grupos completos
  en el mismo `Update`, con sus rangos en el valor; lo guardado antes lee los iniciales (RC6-B, RC1-A).
- El logo y el ícono (ME2) son claves de archivo que arma `BrandImage` (una por subida) y fija `SetBrandImage`, con su propia
  revisión; `BrandImage.Allows` dice la forma y el tamaño de cada uno ([administración](../../../docs/features/administracion.md#marca-por-empresa-me-2026-11-27)).
