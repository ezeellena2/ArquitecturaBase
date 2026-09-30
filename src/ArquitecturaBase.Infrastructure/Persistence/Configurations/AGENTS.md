# Configurations: modelo EF

Cada entidad tiene un `IEntityTypeConfiguration<T>` con columnas, límites, conversiones y restricciones. El contexto descubre las configuraciones desde el ensamblado.

## Al modificar

- Reutilizá constantes del modelo para largos y nombres. Cada entidad propia debe tener su configuración.
- La comprobación de unicidad del servicio y el índice deben comparar igual. Indexá el valor normalizado; filtrá el índice cuando el borrado lógico debe liberar el nombre.
- Las configuraciones de módulos viven dentro del módulo. No acoplar el modelo del núcleo a uno opcional.
- Un cambio del modelo requiere migración y revisar el snapshot; probá las restricciones con PostgreSQL.

## Referencias

- [Modelo e índices únicos](../../../../docs/guides/agregar-un-area.md).
- [Generar y verificar migración](../../../../docs/guides/migracion.md).
- [Configuración por entidad](../../../../tests/ArquitecturaBase.ArchitectureTests/EntityConfigurationTests.cs).
