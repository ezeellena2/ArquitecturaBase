# Settings: ajustes funcionales

Define la entidad de ajustes del sistema, el modo de registro y sus errores. Es la política persistida que consume la aplicación.

## Al modificar

- `SystemSettings` tiene una fila singleton: conservá su Id estable y modificá sus valores mediante sus métodos.
- Una opción inicial de configuración solo siembra la fila ausente; las lecturas posteriores toman la política de la base.
- Un modo nuevo impacta validación, seed, permisos de configuración y el contrato del front; mantené los nombres del enum estables.

## Referencias

- [Política de configuración](../../../docs/features/administracion.md).
- [Pruebas de ajustes](../../../tests/ArquitecturaBase.Domain.UnitTests/Settings).
