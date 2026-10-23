# BusinessForms/Services: casos de uso y helpers

Casos de uso (`*Service`, con contrato en `Interfaces/Services`) y helpers. Cada grupo enlaza su regla y conserva sus límites técnicos. Adaptadores en [Channels](../Channels/AGENTS.md); locks y lectores en [Persistence](../Interfaces/Persistence/AGENTS.md).

## Al modificar

- Ejemplo (`FormLifecycleService`): guard → validador → `OnSuccess` sólo si escribe, con lock, permisos vigentes y revisión adentro. Ninguna lectura abre UoW.

### Borradores y autoría

[Regla](../../../../../docs/features/formularios.md#un-guardado-y-recuperación-exacta).

- Read, más Manage al escribir. El comprobante propio se consulta antes que la revisión; la purga es `FormDraftRetentionService`.
- `Normalize` canoniza mapas, longitudes por defecto y `OfferWhen` (vacío es null); `Resolve` asigna identidades y falla si se pierde una traducción no vacía. `LocalId` sólo correlaciona el intento.
- `SchemaJson` lleva `format` y `fields`; cultura, botón, retiradas y `OfferWhen` van aparte. Guardar o chequear no crea campos ni versiones. MultiChoice requiere Selection: 0 ≤ min ≤ max ≤ 20, max ≥ 1 y Required si y sólo si min > 0; opciones propias de 1 a 20, con mínimo no mayor que su cantidad. Cambiar tipo crea identidad nueva; cambiar el rango la conserva.
- Listado y facetas usan la cultura de UI del servidor. `FormDefinitionReadPolicy` da `recoveryActor` sólo con Publish y omite filas borradas en el medio sin repaginar; detalle y recibo de baja responden NotFound.

### Publicación

Reglas: [chequeo](../../../../../docs/features/formularios.md#chequeo-local-de-publicación--incremento-2a-2026-10-10) y [despacho](../../../../../docs/features/formularios.md#publicación-y-preview--implementación-2b-2026-10-10).

- Read+Publish. El chequeo es consulta: compara la entrada normalizada con lo guardado, sin `Resolve`, IDs nuevos, UoW ni locks.
- Canales ordenados por `ChannelId`: vacío o repetido lanza; ninguno da `ChannelUnavailable`.
- Sólo el original incompleto bloquea por idioma; al canal van las culturas completas. Un multiChoice de Manual bloquea con más de 20 ítems activos; el guard cuenta en SQL una vez por catálogo compartido, sin hidratar ítems. Choice y Filters conservan sus recorridos.
- Aceptación: lock actor/RequestId → replay, antes del token y la revisión → lock de definiciones → revalidación → sella campos, versión, culturas de los perfiles verificados, operación y manifiesto protegido y bloquea el prefijo, sin activar ni llamar al proveedor.
- Despacho: reservar y completar son límites separados y el pedido sale de lo sellado; el canal se llama fuera de toda UoW y un error queda `Uncertain` con los mismos IDs. Se activa por `BaseActiveVersionId` y fence sin pisar un borrador posterior; `CanRetry` es false.
- Status devuelve sólo la operación propia; preview consulta al canal sin UoW.

### Ciclo de vida: dar de baja, reactivar y eliminar

[Regla](../../../../../docs/features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21).

- Baja: Read+Publish; lock → permisos → revisión → sin publicación abierta → `Deactivate`, que conserva la versión activa. La vista previa lee y lista sugerencias entrantes activas.
- Reactivar es publicar el borrador; Domain reactiva al activar la versión nueva.
- Eliminar: Read+Manage; revisión, sin publicación abierta, `LastRecordNumber` cero y sin envíos abiertos ni replies pendientes (`IStructuredFormChannelStateSource`, sólo lectura). `Remove` deja el borrado lógico al interceptor.
- Lifecycle es metadata hermana, fuera de borrador, comprobante y manifiesto. Sus conteos exigen RecordsRead; `CanDelete` depende de Manage y no los revela.

### Catálogos y filtros

[Regla](../../../../../docs/features/formularios.md#catálogos-al-enviar-y-responder--k7-2026-10-12); propuestos en [Channels](../Channels/AGENTS.md).

- CatalogsRead, más CatalogsManage al escribir, con lock del catálogo; metadatos, estado e ítems comparten revisión, sin ruta ni protocolo durable para el estado.
- `FormCatalogValuePolicy` normaliza enteros de hasta 18 dígitos, Decimal sin redondeo y fecha ISO; el error queda en `items[i].value`, también en opciones inactivas. Importar texto usa la cultura de UI y rechaza separadores ambiguos; las celdas numéricas/fecha y la cabecera canónica del export conservan el valor exacto. El tipo se recibe sólo al crear. Mover conserva el valor.
- `FormCatalogItemRecorder`: núcleo PUT/importación sobre ítems seguidos, sin UoW/lock/revisión propios. Manual sin Position; moves usa todo Position+Id. A–Z ordena el original con es-AR y desempata Position+Id, sin moves. Conserva valores tipados; un error revierte todo.
- `FormCatalogFieldRecorder` resuelve identidades, claves, fuentes Manual activas, usos de borrador/activa y confirmaciones antes de cambiar configuración. `FormCatalogFieldPolicy` valida todos los valores, obligatoriedad y unicidad entre activos, conversión y JSON tipado bajo el mismo lock; los errores conservan paths y metadata acotada mediante FieldErrors.OnWithMetadata.
- El recorder común aplica Fields sólo cuando viene el mapa, conserva los inactivos y devuelve identidad más el conjunto final seguido. `FormCatalogFieldPresentation` arma metadata y chequea títulos con la política pura compartida; las fuentes se leen por catálogo y se indexan por opción, sin búsquedas por cada valor.
- Manual admite 10.000 filas en total, incluidas inactivas; un delta suma las existentes bajo el mismo lock. El duplicado señala `items[i].code`; `moves` conserva su tope de 500. Las condiciones leen todos los ítems una vez por catálogo, sin usar el paginado de pantalla ni el tope de oferta de Meta.
- Importar: CatalogsRead+CatalogsManage y Manual/revisión antes de parsear. Preview revalida revisión/permisos, sin UoW/archivo persistido. Apply relee afuera y compara SHA-256; bajo lock revalida, recompone el plan, exige confirmaciones y ejecuta recorder. MarkImported sube una revisión con huella/UTC en ese commit. No devuelve mapa local ni usa caché/replay durable. Fila inválida conserva ítem/posición.
- Filters: personas de ejemplo sólo con Manage; la vista previa no abre UoW ni guarda. `UsageCount` es null sin `formtemplates.read`. Al editar Manual o Filters se devuelve el conteo real; una escritura fallida o sin ese permiso no consulta el uso.
- `FormCatalogOptionsPolicy` y `FormConditionChoicePolicy` sólo leen, sin permisos de valores; `FormCatalogPreparationPolicy` toma los locks de catálogo, ordenados, sólo al enviar y en la UoW del canal.
- `FormCatalogProposalService` acepta fuera de UoW ajena, exige modo neutral Assistant con Actor igual a Subject, deduplica por `ProposalId` bajo lock y crea inactivo con la evidencia original. ChannelId sólo se compara como referencia opaca de la procedencia.

### Captura de respuestas y archivos

[Regla](../../../../../docs/features/formularios.md#recepción-y-captura-de-respuestas--mínimo-3-2026-10-11).

- Un recibo con la misma huella se devuelve antes de exigir permisos; después, RecordsCreate antes y dentro del límite.
- Los archivos se verifican y suben antes del límite, con clave por recibo, campo, posición y hash; el error lleva el `FieldId`.
- La UoW: lock del recibo → definiciones → versión publicada (activa o anterior) → valores, oferta, locks Manual ordenados y actividad del ítem → origen → número → registro, valores, referencias, archivos, runs y recibo juntos. Un rechazo guarda sólo el recibo.
- Huella: suma catálogos, metadatos de archivos y origen sólo si existen, sin cambiar huellas anteriores. MultiChoice valida GUID D distintos/ofrecidos y rango sellado, sin ordenar/deduplicar. ValuesJson lleva un array por pregunta; cada elección crea valor con Position y referencia por ItemId. Omitida sin entrada; [] explícito sin filas. Manual revalida sólo actividad de elegidas bajo los locks existentes; presentación/valor/tipo salen de la oferta.
- Origen: `OriginRecordId` y `OriginSuggestionId` de registros reales de la misma persona, validados contra la sugerencia activa del destino (no su versión); de un catálogo, sólo la opción propuesta con su presentación histórica.
- `FormFileService`: RecordsRead antes y después de abrir el stream, pertenencia y nunca URL ni SAS.

### Planilla, detalle y Excel

[Regla](../../../../../docs/features/formularios.md#planilla-detalle-y-excel--etapa-3).

- RecordsRead revalidado antes de devolver, sin UoW ni caché. Esquemas leídos una vez por versión; origen por `IStructuredRecordOriginSource`.
- `FindRecordNavigationAsync`: mismo pedido/snapshot de planilla, sólo IDs, posición global desde 1 y total sin paginar. Ancla ajena, excluida o ausente da NotFound; revocar descarta también esa ausencia.
- Overview: RecordsRead o ApprovalsDecide, revalidando dimensiones; publicados no eliminados, nombre sellado y LastRecordNumber. Pendientes por roles vigentes y sin propias. Sólo aprobar conserva segmento sin respuestas; PendingOnly explícito con ambos permisos incluye bajas. Facetas/paginado SQL.
- Celdas: `NotAsked` sin pregunta; `Unanswered` vacía, también multiChoice []. El filtro `unanswered` incluye ambas en SQL/facetas/reglas. MultiChoice conserva orden capturado, usa anyOf por intersección y no admite orden por pregunta.
- Excel: RecordsRead+RecordsExport por lote y máximo inicial. MultiChoice une etiquetas con '; '; su columna tipada adicional también es texto canónico, aun vacía/una elección. Choice conserva número/fecha y los headers existen sin filas.

### Reglas, acciones y aprobaciones

[Regla](../../../../../docs/features/formularios.md#motor-de-acciones-y-aprobación--etapa-4-g5-y-g3g4).

- Reglas: RulesManage, revisión inmutable con 409, sin tocar preguntas ni publicar. El historial descarta sus JSON internos sin permiso.
- `FormActionRunRecorder` agrega runs dentro del límite de captura o decisión, con destinatarios, texto y cultura fijados, sin copiar respuestas ni esquemas; un cuerpo largo falla sin recortar.
- Despacho: reservar y completar separados, efecto afuera; un Uncertain se consulta antes de repetir y un Accepted no se reenvía. RequestApproval revalida el rol al reservar: si no existe, `Forms.Rule.RoleUnavailable`, sin tarea.
- Workflow: RecordsRead antes y después de observar el canal por lote, sin efectos ni retry si todos aceptaron. Retry, Consult y revisión humana suman RulesManage dentro del límite.
- Aprobaciones: cuenta activa, permiso y rol vigentes, nunca propias. Continuar revalida permiso tras leer y admite ancla resuelta del mismo formulario; decidir confirma tarea, estado y runs en un commit.

### PDF

[Regla](../../../../../docs/features/formularios.md#diseño-y-copia-fija-del-pdf--etapa-4-g2).

- Diseño: RulesManage, revisión propia y `ActivePdfDesignId`. El preview usa ejemplos (foto ficticia) y no guarda.
- Un idioma está completo con nombre del formulario y del diseño, pie, preguntas y opciones elegidas.
- Preparar fija diseño, culturas y `AppName`; ejecutar relee el registro por Id, arma un `FormPdfRenderRequest` efímero y usa `WriteOnceAsync` en `FormPdfStorageKey.Create` tras consultar.
- `FormPdfDocumentRecorder` registra una vez un resultado completo, bajo el fence.

### Asistente y sugerencias

Reglas: [prellenado](../../../../../docs/features/formularios.md#propuestas-de-prellenado--etapa-5) y [sugerencias](../../../../../docs/features/formularios.md#configuración-de-qué-sugerir-después--etapa-5).

- Candidatos: versión activa publicada con `OfferWhen` y revisión actuales; sin RecordsCreate, lista vacía.
- Propuesta: ordinal/revisión de la última lectura del turno/destinatario. MultiChoice se excluye de preguntas, sourceValues y mapeos sin retirar formularios mixtos. Escalar mal formado se omite; identidad/tipo/GUID ajeno rechaza.
- Validar prellenado o aclaración no escribe; la vista previa no toma locks y el envío los toma en la UoW del canal. La aclaración usa pregunta y motivo fijos de resx, la elegida vuelve sin prellenado y una opción inválida invalida la pregunta.
- `CanMap` une escalares del mismo tipo y elecciones del mismo catálogo explícito; el error va en `mappings[i].source`.
- Always no usa el motor y pide IDs explícitos; Prepare completa IDs omitidos sólo desde la propuesta auténtica. `FormSuggestionHistoryAccessGuard` exige RecordsRead antes y después, salvo al modo neutral Assistant con Actor igual a Subject.

### Formularios que faltan

[Regla](../../../../../docs/features/formularios.md#formularios-que-faltan--etapa-5).

- Sólo una lectura completa justifica un pedido. La privacidad se revisa al proponer y al capturar, antes y bajo el lock.
- Captura por `SourceReceiptId` opaco y modo neutral Assistant o Manual; Assistant exige Actor igual a Subject. ChannelId conserva procedencia opaca, sin interpretar IDs de otro módulo. El replay no suma ni reabre. Tablero y descarte: Manage y revisión.

### Idiomas

[Regla](../../../../../docs/features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19).

- Antes de ofrecer o enviar, `FormPresentationCulturePolicy` elige una cultura común; texto y detalle de un ítem sin traducción usan su original sin cambiarla.
- Las acciones recomprueban con lo capturado; quien respondió en el original no pasa al secundario.
- Las lecturas históricas usan una cultura por versión según su manifiesto; la UI, el idioma actual.

## Referencias

- [Pruebas unitarias](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md).
