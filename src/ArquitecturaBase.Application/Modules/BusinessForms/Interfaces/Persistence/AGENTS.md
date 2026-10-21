# BusinessForms/Interfaces/Persistence: repositorios y lectores

Contratos que implementa `Infrastructure/Modules/BusinessForms/Persistence`. Los repositorios devuelven entidades seguidas y dan de alta sin guardar; los lectores proyectan sin seguimiento. Hay uno o dos por área: borrador y definición, publicación, ciclo de vida, versiones sin uso, catálogos y filtros, registros, recibos y archivos, planilla, reglas, acciones, aprobaciones, PDF, selección del asistente, sugerencias y formularios que faltan.

## Al modificar

- Nombres por lo que devuelven ([ADR 0008](../../../../../../docs/decisions/0008-nombres-de-repositorios-y-lectores.md)); una proyección nueva de un lector termina en `Row` ([Response y Row](../../../../../../docs/architecture/backend.md#modelos-response-y-row)).
- Lock de definiciones: `LockDefinitionsAsync` de `IFormDraftRepository` o de `IFormRecordRepository` y `IFormApprovalRepository.LockAsync` toman la misma clave. La comparten borrador, publicación, ciclo de vida, reglas, diseño PDF, sugerencias, acciones, aprobaciones, selección del canal, retiro de versiones y numeración.
- Orden: `LockRequestAsync(actor, requestId)` o `LockSourceReceiptAsync` antes del lock de definiciones; después, `LockCatalogAsync` de los catálogos Manual elegidos, ordenados. La edición de un catálogo usa `IFormCatalogRepository.LockAsync`; los formularios que faltan, `IFormMissingRequestRepository.LockAsync`. Las claves viven en Infrastructure.
- Lectura por lote, sin consultas por fila: `ListFactsAsync` y la evidencia del canal van una vez por página; `ListByReceiptIdsAsync` y `ListExportIdsAsync` reciben la definición ya leída para reutilizar sus versiones; `FindBatchAsync` acepta las versiones ya cargadas.
- `FindBatchAsync` devuelve filas y metadata de versiones por separado; `Version` en los DTO internos es `JsonIgnore`. No agregués esa metadata a `FormRecordStoredRow` ni a copias de acciones o PDF.
- `FindRecordNavigationAsync` recibe la definición ya leída, aplica filtro/orden completos y proyecta sólo IDs públicos de registro, nunca SourceReceiptId ni valores. La fila devuelve vecinos, posición desde 1 y total, sin paginar ni hidratar respuestas.
- `IFormUnusedVersionReader` devuelve sólo pares pedidos del mismo formulario, con operación terminal Published o Failed, sin publicación abierta y con la definición dada de baja, borrada o con otra versión activa. Un par desconocido o ajeno nunca vuelve; los borrados se leen sólo desactivando el filtro nombrado de soft-delete.
- Eliminar es `Remove` sobre la definición seguida: el interceptor completa el borrado lógico.

## Referencias

- [Locks](../../../../../../docs/architecture/backend.md#locks).
- [Implementación y SQL](../../../../../ArquitecturaBase.Infrastructure/Modules/BusinessForms/AGENTS.md).
- [Repositorio de referencia](IFormDraftRepository.cs).
