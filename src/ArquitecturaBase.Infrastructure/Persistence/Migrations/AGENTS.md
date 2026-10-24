# Migrations: evolución versionada del esquema

Contiene migraciones EF, sus metadatos y el snapshot. Representa la secuencia que debe poder aplicarse tanto en una base nueva como en un despliegue.

## Al modificar

- Seguí la guía de migración para generar, revisar y comprobar cambios pendientes. Revisá pérdidas de datos, índices, filtros y `Up`/`Down`.
- No cambiar una migración ya aplicada para resolver un cambio nuevo; creá la siguiente. Un ajuste manual del SQL generado debe quedar coherente con el modelo y el snapshot.
- Portabilidad: filtros del modelo en una sola línea; SQL multilínea de migraciones usa `.ReplaceLineEndings("\n")` en el argumento. `MigrationSqlPortabilityTests` inspecciona valores efectivos del snapshot, Designers y operaciones Up/Down, y compara el modelo sin conectarse a la base.
- EF construye el host de diseño: necesita configuración de `appdb` y `cache`; crear el bundle no requiere conectarse a Redis.
- Production aplica el bundle antes de la imagen y la Api valida el estado; no habilitar migraciones automáticas en producción.

La migración de sugerencias y audio conserva Conversation y bytea vacío como defaults para el intervalo bundle→imagen. Su Down toma locks de las tablas del canal y rechaza hechos Assistant o QuickReplies antes de cualquier borrado; no inventa operadores ni elimina respuestas confirmadas. Sólo el audio representable vuelve a Media sin conservar su transcripción como cuerpo original. Las pruebas de migración verifican datos e historia antes/después, no sólo el esquema vacío.

AddCatalogValueType agrega el tipo cerrado con default Text y valores nullable de ítems/referencias; Filters permanece Text y la referencia exige valor/tipo juntos. No rellena valores desde etiquetas ni cambia JSON, manifiestos, hashes o recibos históricos. Down toma locks de las tres tablas y rechaza tipos distintos de Text o cualquier valor almacenado antes de quitar columnas; nunca descarta información para volver atrás. FormCatalogValueMigrationTests prepara Up/Down/Up, checks reales, historia conservada y un intercambio HTTP de códigos sobre la base migrada.

AddCatalogImportMarker agrega sólo las dos columnas nullable de la última planilla aplicada y su CHECK de pareja. Los catálogos existentes comienzan sin marca; no se reescriben ítems, respuestas ni snapshots. Down quita esa metadata y conserva las columnas históricas, verificadas por proyección explícita en el test Up/Down/Up preparado.

AddFormMultipleChoiceValues agrega Position con default 0 y CHECK 0–19, y cambia sólo los índices únicos de valores y referencias para admitir varias elecciones. Conserva JSON, versiones, hashes y recibos. Down toma locks de definiciones, campos, valores y referencias y rechaza multiChoice en el borrador SchemaJson.fields, aun sin versión publicada o con activa escalar, o una identidad BusinessFormFields.Type=multiChoice incluso sin valores o con una sola elegida en Position 0, cualquier Position mayor que 0 o varias referencias por pregunta antes de cualquier DDL; nunca adapta ni borra hechos para volver atrás.

AddCatalogFieldsAndPresentation agrega FieldsJson/PresentationJson nullable sólo para Manual, FieldValuesJson de ítems con default `{}` e índice GIN `jsonb_path_ops`, y snapshot nullable en referencias. No reescribe historia. Down toma locks de las tres tablas y rechaza metadata o valores E2 antes de cualquier DDL. FormCatalogFieldsMigrationTests prepara conservación Up/Down/Up, default, CHECK, índice y rechazo sin pérdida por cada columna; PostgreSQL sólo se ejecuta al cierre conjunto.

AddFormPersonData crea las tablas de atributos y valores con revisiones independientes, Key y UserId únicos, CHECK de fuente y FK restrictiva al catálogo, sin FK de UserId al núcleo. Conserva íntegros catálogos y capturas anteriores. Down bloquea ambas tablas y rechaza cualquier fila antes del DDL; las pruebas preparan Up/Down/Up y rechazo de cada tabla.

AddCatalogAndPersonAttributeOriginalCulture agrega `OriginalCulture` con default `es` a catálogos y atributos de personas y toma el de cada propuesta H4 de su evidencia. Down bloquea ambas tablas y rechaza, antes del DDL, un catálogo no propuesto o un atributo con original distinto de `es`. FormCatalogOriginalCultureMigrationTests prepara Up/Down/Up con historia conservada y ese rechazo.

AllowCatalogItemsWithoutSpanishText vuelve nullable `TextEs` de las opciones, sin reescribir filas; la imagen anterior siempre lo escribe. Down bloquea la tabla y rechaza, antes del DDL, una opción sin texto en español; no inventa uno ni deja un default. FormCatalogItemSpanishTextMigrationTests prepara Up/Down/Up con historia conservada y ese rechazo.

## Referencias

- [Comandos y revisión obligatoria](../../../../docs/guides/migracion.md).
- [Bundle antes de imagen](../../../../docs/guides/despliegue.md).
- [Pruebas de migraciones](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Persistence/MigrationsTests.cs).
