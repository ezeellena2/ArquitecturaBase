# BusinessForms: autoría de borradores

Leer [formularios](../../../../docs/features/formularios.md) y el [resumen de la etapa en curso](../../../../docs/plans/formularios/README.md).
- Servicios públicos consumen puertos. Create/Update abren un único OnSuccess; helpers no guardan. Retención abre su propio caso de uso técnico.
- Validar estructura antes; resolver identidad, revisión y prefix único dentro del límite. Consultar comprobante propio antes del conflicto de revisión.
- Preservar traducciones; canonizar defaults shortText=80/longText=600 y mapas ordenados. LocalId sólo correlación del intento. Número futuro string decimal; fecha futura DateOnly.
- SchemaJson contiene format/fields de preguntas únicamente. Cultura/botón y ledger separados. Campos y versiones se crean sólo al aceptar publicación, nunca al guardar/chequear. Captura valores exclusivamente desde el puerto neutral StructuredRecord; no agregar referencias a WhatsApp, IA o Meta. El chequeo consulta el puerto neutral de canales.
- Catálogo y capacidad forms se componen en el registro real del módulo; recursos bilingües.

- BusinessFormPublicationService.CheckAsync exige Read+Publish, reutiliza validación estructural y FindAsync. Compara entrada normalizada con revisión/IDs canónicos guardados, sin Resolve, nuevos UUID, UoW ni locks.
- Consume IStructuredFormChannel y DTOs StructuredData, sin importar otro módulo. Colección ordenada por ChannelId; ausencia explícita y duplicados/vacíos fallan configuración.
- Compatibility local, AcceptanceAvailability y ExternalEvidence son distintas. La aceptación depende de capacidad instalada/configurada, operación abierta y chequeo sellado; evidencia Pending; no bloquear sólo por no tener piloto. HTTP no lleva JSON/hash/ID externo.
- D5: objetivos es/en completos, nombre/textos por cultura exacta sin fallback ni autotraducción; guardar secundarios pendientes. El primer diff añade las preguntas guardadas; diff histórico requiere consumir versiones selladas.

D5 directa: Name mapa es/en; original válido, secundarios pendientes/vaciado explícito, omitir no vacíos falla. FormPublicationPolicy comprueba ambos idiomas antes del canal. Listado/facetas usan cultura UI resuelta por servidor, no un idioma suministrado en query.

2b mínimo: Check es consulta y emite token protegido por 10 minutos sólo con capacidad. FormPublicationAcceptanceService abre un único OnSuccess, revalida permisos, toma actor/UUID → global, resuelve replay antes de token/revisión y sella Fields/Version/Operation/prefix. FormPublicationStatusService sólo lee comprobante propio con Read+Publish. Snapshot de preguntas SchemaJson jsonb; manifiesto/huellas privados protegidos por propósito propio. Transporte fuera del límite, sin escritor de otro módulo.

2b: DispatchService reserva/completa con límites propios y devuelve requests neutrales construidos desde el snapshot/manifiesto sellados. FormPublicationChannelDispatchService acepta y reconcilia por puerto entre scopes, fuera de UoW. El worker sólo coordina los scopes de reserva/despacho/completado; Complete decide estados y activa por BaseActiveVersionId/fence/permisos, preservando borrador posterior. Status sólo propio; issues traducidos y seguros. PreviewService sólo lee Read/version publicada/cultura explícita es/en, sin UoW. Diff consume schema sellado. CanRetry=false; recuperación mantiene operación, sin efectos inciertos repetidos.

Detalle proyecta publication seguro y canPublish/canPreview actuales. RequestId/ruta de recuperación sólo del actor con Publish; status propio conserva su permiso. El listado muestra última operación sin quitar activa/facetas. Query culture no cambia UI ni cuentas.

BusinessFormSource implementa IStructuredFormSource neutral para selección por destinatario: employeeRecords.create vigente, idioma es/en y lector de versión activa sellada. Replace scoped idempotente, cualquier orden con núcleo; sin referencia WhatsApp ni límite de escritura.

BusinessFormRecordRecorder implementa IBusinessFormRecordCaptureService y abre la UoW del caso de uso. BusinessFormRecordCaptureSink adapta el puerto neutral IStructuredRecordSink sin guardar. La captura valida permisos y snapshot publicado, registra valor tipado/recibo/número y deduplica por SourceReceiptId+huella; BusinessFormRecordSource implementa IStructuredRecordSource y relee permiso antes de proyectar respuestas históricas. Los lectores/repositorios técnicos permanecen en Infrastructure.

Catálogo Manual: Application asigna altas al final y no recibe Position. Update aplica la secuencia acotada de movimientos por ID/dirección al orden completo Position+Id, normaliza una sola vez y guarda junto con metadatos bajo un único lock/UoW. Destino inexistente o movimiento fuera del límite revierte todo. UsageCount es nullable: lista y detalle cuentan definiciones actuales distintas que referencian el catálogo; sin permiso de lectura, ambos devuelven null.

K4 Filters: `FormCatalogFiltersService` entrega metadata y personas de ejemplo, ejecuta la vista previa sin UoW y crea/actualiza `FiltersJson` con una sola UoW y revisión. Los valores de condición `role/has` son IDs de rol; el front muestra el nombre actual de metadata. La lista cuenta `ByKind` en el conjunto filtrado por búsqueda/estado antes de aplicar origen o página. La fuente es un SELECT fijo del lector sobre Identity, por lo que options y preview no exponen disponibilidad de una vista. `Unavailable` y su motivo quedan para un catálogo inválido, no para un rol que ya no exista. `sampleUserId` sólo pertenece a la vista previa, nunca al catálogo guardado. La conexión sigue siendo la normal.

K6: las preguntas `choice` usan opciones manuales o `source.catalog`, nunca ambas. Cambiar el origen de una identidad confirmada la reemplaza. El chequeo bloquea catálogos inexistentes o inactivos; UsageCount cuenta definiciones actuales distintas cuyo borrador referencia el catálogo y conserva orden/paginado SQL en la lista.

K7: PrepareAsync del puerto neutral resuelve la oferta por destinatario/cultura de cuenta. El envío serializa por catálogo en la UoW del canal; no guarda listas en la versión. La captura valida oferta, toma los mismos locks para Manual y comprueba actividad antes de numerar. Filters no consulta de nuevo su origen. Registro, valores, referencias y recibo se confirman juntos; el recibo conserva FieldId de rechazo y el source histórico usa texto/detalle/nombre de catálogo guardados, sin lookup actual.

Cierre de catálogos: los PUT admiten isActive opcional, conservado al omitirlo, junto con expectedRevision. Metadatos, estado y delta comparten una sola revisión/UoW. Un catálogo dado de baja conserva opciones y referencias; su configuración permanece editable. No agregar una ruta ni un protocolo durable para cambiar su estado.

F1 archivos: `files` conserva mínimo, máximo y origen de foto en el snapshot; no lleva tamaños por pregunta. Guardar permite presupuestos superiores a diez, publicar no. Chequeo y aceptación exigen almacenamiento privado disponible; la capacidad `form-files` sólo se aporta cuando está configurado. Las preguntas sin archivos omiten `files` al serializar, conservando sus snapshots.

El tope de instalación `BusinessForms:MaxFileSizeMiB` admite 1–25 (25 por defecto); lo sella el manifiesto aceptado, fuera de las preguntas, y Dispatch conserva ese valor al reconstruir el request neutral.

F3 prepara contenido y sube con claves por recibo/campo/posición/hash fuera de la transacción; sólo después captura registro, valores, StoredFiles, referencias y recibo juntos. Una falla de verificación rechaza sin número ni archivos parciales en PostgreSQL. Replay incluye metadatos, nunca bytes. Source entrega metadatos históricos por lote sólo con RecordsRead, sin claves de almacenamiento.
