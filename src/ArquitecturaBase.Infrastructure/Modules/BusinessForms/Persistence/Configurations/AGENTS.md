# Configurations: esquema EF de formularios

Un `IEntityTypeConfiguration<T>` por entidad del módulo, con su tabla (`ToTable`), columnas, índices, CHECK, columnas calculadas y tokens de concurrencia; el esquema se migra en la cadena única del núcleo. La pertenencia por FK compuestas y las columnas nuevas están en la [guía de Persistence](../AGENTS.md). Las reglas de esquema que también tocan lectores o repositorios están por tema en el [detalle](../../../../../../docs/features/formularios-persistencia.md): nombres, prefijo y baja, operaciones, catálogos, propuestas, datos de personas, corregir y anular, tareas abiertas, runs y pedidos faltantes.

## Al modificar

- `(CatalogId, Position)` y `(CatalogId, Code)` tienen claves únicas diferidas creadas en SQL, fuera del modelo EF: no las conviertas en índices o claves alternas de EF ni renumeres datos en una migración. Code sigue reservado para ítems inactivos. CHECK de `Kind`/`FiltersJson` y de propuesta (Pending o Discarded nunca activos); `GetByProposalAsync` sigue la entidad para el replay bajo lock.
- `OriginalCulture` de catálogos y atributos de personas tiene largo 2 y default `es` sólo para las filas previas y la imagen anterior; el código siempre lo fija.
- `Catalog.ValueType` es Text/Integer/Decimal/Date, con default Text para existentes y Filters sólo Text. `Item.Value` es nullable de 64; la referencia sella Value (64) y ValueType (16), nulos conjuntamente. La migración no reescribe JSON, manifiestos, hashes ni capturas.
- LastImportSha256 (character(64)) y LastImportAtUtc son nullable conjuntamente por CHECK, sin defaults ni backfill. Aplicar importación fija la marca en el mismo commit de la revisión; exportar no la cambia.
- Cada fila de valor contiene una sola columna tipada (CHECK). Las elecciones múltiples agregan filas ordenadas por Position 0–19, con default 0 para las anteriores e índice único RecordId/FieldId/Position; las referencias son únicas por RecordId/FieldId/Row/ItemId (el mismo ítem del catálogo puede ir en dos renglones de un bloque). Los escalares conservan Position 0. Las referencias guardan `ItemId` opaco (Filters no apunta a ítems Manual) y `TranslationsJson`; los archivos, posición 0–9 y únicos por `FileId` y respuesta (una corrección vuelve a enlazar los que siguen); `OriginRecordId` es FK restrictiva. No se reconstruyen capturas anteriores.

## Referencias

- [Modelo EF del núcleo](../../../../Persistence/Configurations/AGENTS.md) y [migraciones](../../../../../../docs/guides/migracion.md).
- Pruebas: [migraciones del módulo](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormMigrationsTests.cs) y [códigos únicos diferidos](../../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormCatalogCodeUniquenessMigrationTests.cs).
