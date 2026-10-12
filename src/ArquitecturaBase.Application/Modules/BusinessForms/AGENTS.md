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

Catálogo Manual: Application asigna altas al final y no recibe Position. Update aplica la secuencia acotada de movimientos por ID/dirección al orden completo Position+Id, normaliza una sola vez y guarda junto con metadatos bajo un único lock/UoW. Destino inexistente o movimiento fuera del límite revierte todo. UsageCount es nullable y sólo vale cero cuando el permiso permite conocerlo.

K4 Filters: `FormCatalogFiltersService` entrega metadata y personas de ejemplo, ejecuta la vista previa sin UoW y crea/actualiza `FiltersJson` con una sola UoW y revisión. La lista usa `state=Active|Inactive|Unavailable` y cuenta `ByKind` en el conjunto filtrado por búsqueda/estado antes de aplicar origen o página. Si la vista versionada deja de cumplir el esquema, Filters queda `Unavailable` con motivo seguro; options lo informa y preview/guardar responden conflicto. `sampleUserId` sólo pertenece a la vista previa, nunca al catálogo guardado. La conexión sigue siendo la normal.
