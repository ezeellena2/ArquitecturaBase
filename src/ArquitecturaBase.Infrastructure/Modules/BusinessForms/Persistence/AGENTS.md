# Persistence: modelo y consultas de formularios

Configuraciones EF, repositorios, lectores y seed del módulo, sobre `Set<T>()` del contexto único. Rigen además las guías de [persistencia del núcleo](../../../Persistence/AGENTS.md).

- `Configurations/`: tablas, índices, CHECK, columnas calculadas y tokens de concurrencia ([guía](Configurations/AGENTS.md)).
- `Repositories/`: entidades seguidas para modificar, altas, bajas y locks ([guía](Repositories/AGENTS.md)).
- `Readers/`: proyecciones sin seguimiento y SQL fija parametrizada ([guía](Readers/AGENTS.md)).
- `Seed/FormVersionPresentationSeeder` (completa desde el manifiesto la presentación y las culturas publicadas que faltan) y `FormAdvisoryLockKeys` no tienen guía propia: sus reglas están abajo; las de las culturas publicadas, en «Respuestas» del detalle.
- [Detalle de la persistencia](../../../../../docs/features/formularios-persistencia.md): por tema, lo que cruza subcarpetas y lo que no entra en sus guías. Leé el tema antes de tocar su código.

Por área:

- **Definición y ciclo de vida:** `FormDefinitions` (borrador, `OfferWhen`, baja y borrado lógico), `FormDraftChanges` (comprobantes por actor/UUID) y `BusinessFormRetiredIdentities` (ledger contra resucitar FieldId/ChoiceId); `FormDraftRepository`, `FormDefinitionReader` y `FormLifecycleReader`.
- **Publicación:** `BusinessFormFields`, `BusinessFormVersions` (esquema y presentación sellados) y `BusinessFormPublicationOperations` (manifiesto protegido, lease, fence); `FormPublicationRepository/Reader`, `FormRecordReader.Versions`, `FormVersionPresentationQuery`, `FormUnusedVersionReader` y `Seed/FormVersionPresentationSeeder`.
- **Qué se puede ofrecer:** `FormOfferableVersionQuery`, filtro único del selector, el asistente, las sugerencias y el ciclo de vida.
- **Catálogos:** catálogos Manual/Filters con propuesta del asistente e ítems; `FormCatalogRepository`, `FormCatalogReader` y `FormCatalogFilterReader`, que comparte con la carga desde la app el SQL de los catálogos Filters (`FormCatalogFilterSql`).
- **Datos de personas:** `BusinessFormPersonAttributes` y `BusinessFormPersonData`, con revisiones independientes; `FormPersonDataRepository/Reader`.
- **Respuestas:** registros, valores tipados, recibos de captura, referencias de catálogo y archivos; `FormRecordRepository`, `FormRecordReader`, `FormOverviewReader` (tabla de formularios), `FormEntryReader` (carga desde la app), `FormResponseReader` (planilla, facetas, export y cadena de origen), `FormCaptureReceiptReader` y `FormFileReader`.
- **Reglas y efectos:** revisiones de reglas, runs, tareas de aprobación, documentos y diseños PDF; `FormRule*`, `FormActionRun*`, `FormApproval*`, `FormPdfDesign*`, `FormDocumentRepository` y `FormActionRecipientReader`.
- **Sugerencias y pedidos faltantes:** `BusinessFormSuggestions`, temas y pedidos; `FormSuggestion*` y `FormMissingRequest*`.

## Al modificar

- Locks sólo con `FormAdvisoryLockKeys` (`forms:`): actor/UUID antes de `forms:definitions`, el lock global que serializa borrador, publicación, reglas, diseño, sugerencias, aprobación, baja y numeración. `forms:catalog:{id}` cubre la edición del catálogo y la captura Manual, antes de numerar; `forms:capture:` y `forms:missing-requests` cubren captura y pedidos.
- La pertenencia es por FK compuestas `(DefinitionId, Id)`: punteros activos, versión, campo, regla y diseño. Las revisiones (reglas, PDF, sugerencias) son inmutables con índice único definición/revisión; punteros, `Revision` y fences son tokens de concurrencia.
- Una columna nueva no reescribe versiones, manifiestos, hashes, bindings ni capturas.
- La presentación histórica abre manifiestos por lote con su contexto exacto; si falta o no coincide, falla. El seeder sólo completa presentación ausente desde el manifiesto de su operación, sin guardar ni tocar esquema, hash ni operaciones.

## Referencias

- [Persistencia y responsabilidad](../../../../../docs/features/formularios.md#persistencia-y-responsabilidad), [listado y facetas](../../../../../docs/features/formularios.md#listado-búsqueda-y-facetas-coherentes), [planilla](../../../../../docs/features/formularios.md#planilla-detalle-y-excel--etapa-3) y [baja y eliminación](../../../../../docs/features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21).
- [Nombres de lectores y repositorios](../../../../../docs/architecture/backend.md#nombres-de-repositorios-y-lectores) y [locks](../../../../../docs/architecture/backend.md#locks).
- Pruebas: [selección](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormSelectionQueryTests.cs) y [planilla](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormResponseQueryTests.cs).

Etapa 2 de ARCA: `BusinessFormRecordResults` (una fila por run, única por `RunId`, FK compuesta a la respuesta en cascada y al run restrictiva; `FormRecordResultRepository` y `FormRecordResultReader`, que lee por lote las de una página y el último resultado de cada fuente de un formulario) y las columnas `StandardKey`, `StandardVersion` y `StandardBindingsJson` de `FormDefinitions` (`FormLibraryReader` cuenta las copias de cada estándar y busca catálogos activos por `Key`). Migraciones `AddFormRecordResults` y `AddFormDefinitionStandard`.
