# BusinessForms/Interfaces/Persistence: repositorios y lectores

Contratos que implementa `Infrastructure/Modules/BusinessForms/Persistence`. Los repositorios devuelven entidades seguidas y dan de alta sin guardar; los lectores proyectan sin seguimiento. Hay uno o dos por área: borrador y definición, publicación, ciclo de vida, versiones sin uso, catálogos y filtros, registros, recibos y archivos, planilla, reglas, acciones, aprobaciones, PDF, selección del asistente, sugerencias y formularios que faltan.

## Datos de personas

`IFormPersonDataRepository` comparte lock de definiciones y agrega atributos/persona; no guarda. `IFormPersonDataReader` proyecta atributos/usos y datos con etiquetas fuente por lote, sin correo. Falta de fila devuelve valores vacíos y revisión null. La tabla administrativa incluye cuentas inactivas y omite borradas; opciones sólo id/text activos, búsqueda y paginado SQL. Datos históricos inactivos siguen persistidos.

## Al modificar

- Nombres por lo que devuelven ([ADR 0008](../../../../../../docs/decisions/0008-nombres-de-repositorios-y-lectores.md)); una proyección nueva de un lector termina en `Row` ([Response y Row](../../../../../../docs/architecture/backend.md#modelos-response-y-row)).
- Lock de definiciones: `LockDefinitionsAsync` de `IFormDraftRepository` o de `IFormRecordRepository` y `IFormApprovalRepository.LockAsync` toman la misma clave. La comparten borrador, publicación, ciclo de vida, reglas, diseño PDF, sugerencias, acciones, aprobaciones, selección del canal, retiro de versiones y numeración.
- Orden: `LockRequestAsync(actor, requestId)` o `LockSourceReceiptAsync` antes del lock de definiciones; después, `LockCatalogAsync` de los catálogos Manual elegidos, ordenados. La edición de un catálogo usa `IFormCatalogRepository.LockAsync`; los formularios que faltan, `IFormMissingRequestRepository.LockAsync`. Las claves viven en Infrastructure.
- Lectura por lote, sin consultas por fila: `ListFactsAsync` y la evidencia del canal van una vez por página; `ListByReceiptIdsAsync` y `ListExportIdsAsync` reciben la definición ya leída para reutilizar sus versiones; `FindBatchAsync` acepta las versiones ya cargadas.
- `FindBatchAsync` devuelve filas y metadata de versiones por separado; `Version` en los DTO internos es `JsonIgnore`. No agregués esa metadata a `FormRecordStoredRow` ni a copias de acciones o PDF.
- `FindRecordNavigationAsync` recibe la definición ya leída, aplica filtro/orden completos y proyecta sólo IDs públicos de registro, nunca SourceReceiptId ni valores. La fila devuelve vecinos, posición desde 1 y total, sin paginar ni hidratar respuestas.
- `IFormUnusedVersionReader` devuelve sólo pares pedidos del mismo formulario, con operación terminal Published o Failed, sin publicación abierta y con la definición dada de baja, borrada o con otra versión activa. Un par desconocido o ajeno nunca vuelve; los borrados se leen sólo desactivando el filtro nombrado de soft-delete.
- `FindContinuationAsync` devuelve el DTO de tareas autorizadas del mismo formulario, con ancla abierta o resuelta, posiciones actuales y total de una instantánea; no pagina por offset ni hidrata valores.
- `IFormCatalogReader.ListAllItemsAsync` devuelve todos los ítems Manual, incluidos inactivos, por Position+Id y sin truncar. Es lectura interna de condiciones; `ListOfferItemsAsync` mantiene 201 filas activas para detectar el exceso del canal. `CountActiveItemsAsync` cuenta activos en SQL sin materializar ítems; el chequeo de varias elecciones Manual lo usa una vez por catálogo compartido.
- Eliminar es `Remove` sobre la definición seguida: el interceptor completa el borrado lógico.
- `FindCatalogFieldValuesAsync` proyecta de una vez campos y valores de los ítems elegidos bajo los locks existentes; una segunda lectura del mismo puerto resuelve textos de opciones fuente. `ListOfferItemsAsync` acepta contención JSON canónica opcional y la aplica antes de ordenar y limitar. `FindFieldUsageAsync` conserva borrador y activa; `ExistsSourceUsageAsync` bloquea fuentes usadas por campos activos de otros catálogos.

## Referencias

- [Locks](../../../../../../docs/architecture/backend.md#locks).
- [Implementación y SQL](../../../../../ArquitecturaBase.Infrastructure/Modules/BusinessForms/AGENTS.md).
- [Repositorio de referencia](IFormDraftRepository.cs).

- FindMaximumOfferSizeAsync devuelve máximo por combinación de campos de persona, tras contención fija y activos; no hidrata opciones. FindRecipientsWithoutDataAsync cuenta usuarios activos con RecordsCreate que carecen de cualquiera de los atributos, sin duplicar y con hasta5 nombres. Ambos son SQL fijo parametrizado y de sólo lectura.
