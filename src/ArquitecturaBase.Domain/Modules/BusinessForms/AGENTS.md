# BusinessForms: modelo e identidades

Leer [formularios](../../../../docs/features/formularios.md) y el [resumen de la etapa en curso](../../../../docs/plans/formularios/README.md) antes de modificar.
- Domain usa BCL y Result. Entidades Definition/DraftChange heredan Entity e IAuditable; auditoría exclusivamente por interceptores.
- UUID v7 canónicos creados por servidor. Mismo tipo/renombre/orden conserva identidad; cambio de tipo/copia crea otra. Ledger retirado no se reinicia al reconstituir.
- Las fábricas de identidad usadas por FormDraftPolicy permanecen en Domain. El guardado real resuelve IDs y revisiones contra la definición en Application; sus garantías se prueban por HTTP y persistencia. No mantener otro modelo de borrador sin consumidor productivo.
- Un comprobante protege el resultado por 30 días. Expirar limpia payload, conserva metadata contra reutilización del UUID. El módulo no depende de IA ni WhatsApp.

FormPublicationErrors declara los conflictos del chequeo y la ausencia del canal. Declara también conflictos de aceptación/estado; no importa tipos del canal opcional.

Contenido puro de preguntas/opciones: mapas localizados copiados/Frozen, sin compartir diccionarios mutables del llamador. Reglas de original/completitud del contrato tienen consumidores en Application; identidad/ledger no cambia al traducir.

BusinessFormField registra identidad/tipo al aceptar. BusinessFormVersion sella SchemaJson una vez. PublicationOperation conserva recibo/snapshot, lease/fence y transiciones; resultados tardíos no completan otra reserva. Definition fija prefix en la aceptación y activa sólo con baseActiveVersionId coincidente, sin cambiar el borrador nuevo. Auditoría siempre por interceptores.

Version inmutable/Field canónico se crean al aceptar; PublicationOperation conserva base activa/lease/fence/manifiesto protegido. Activar no cambia revisión ni borrador; CAS rechazado deja incertidumbre, completado confirmado idempotente. Campos/versiones/operación tienen pertenencia y constraints en Infrastructure.

BusinessFormRecord, BusinessFormRecordValue y BusinessFormCaptureReceipt conservan capturas autorizadas, campos tipados y replay idempotente. No almacenan texto no persistible; la validación compartida está en Domain/Common/StorableText.

Version sella NameJson/SubmitLabelJson/Culture/QuestionCount aparte de SchemaJson/hash. Presentación aceptada inmutable; nunca reconstruirla desde un borrador posterior.

BusinessFormRecord y BusinessFormRecordValue conservan origen, versión, actor, instante UTC, número y un único valor tipado. BusinessFormCaptureReceipt hace idempotente la sesión de origen y conserva el rechazo estable; no contiene payload bruto de Meta.

BusinessFormRecordReference es un hecho inmutable asociado al registro: catálogo/revisión/ítem y presentación ofrecida. El recibo rechazado conserva sólo FieldId de la pregunta, sin respuesta ni número. Auditoría del registro por los interceptores existentes; sin una tabla nueva de auditoría.

F1 agrega identidades Photo y Document; cambiar el tipo conserva la regla de identidad nueva. Las versiones ya publicadas no se reescriben.

La planilla reutiliza registros, valores, referencias y archivos existentes. `formrecords.export` es independiente y adicional a `formrecords.read`; no hay permisos por formulario ni entidades de exportación. Los errores FilterInvalid, ExportTooLarge y ExportBudgetExceeded son de Domain.

Etapa 4, G1: BusinessFormRuleSet conserva revisiones inmutables y HasApproval. Definition.ActiveRuleSetId es independiente de la revisión del borrador y de la versión publicada; sólo se mueve bajo el lock de definiciones. formrules.manage controla reglas y diseño, sin conceder lectura de valores.

G5: Record fija RuleSetId al capturar y transita de Received a PendingApproval una vez. ActionRun conserva snapshot, evento/regla/acción, dependencia, lease y fence; Completed y NotApplicable no se repiten. Task abierta guarda autor confirmado y rol. Document relaciona respuesta/ejecución/cultura con StoredFile sólo al confirmar un PDF completo bajo el fence vigente.

La aprobación de un nivel conserva revisión, autor confirmado, rol y decisión. `formapprovals.decide` no concede lectura de valores o PDF; la decisión revalida rol y permiso, prohíbe al autor y confirma tarea, estado y runs juntos. El rechazo exige motivo de 1–500 caracteres.

G2 guarda diseño PDF por revisión propia y `ActivePdfDesignId`, sin cambiar borrador/publicación. El run fija diseño, autor, decisión, culturas completas y nombre del sistema; los datos inmutables se leen por Id. La emisión usa creación condicional por run/cultura, consulta primero y conserva bytes ya subidos; metadata se confirma sólo con fence vigente. El preview usa ejemplos y no emite ni guarda. Documentos privados sólo por API con `formrecords.read` antes y después de abrir el stream.

Revisión de avisos: sólo NotifyAuthor/NotifyRole inciertos aceptan una decisión humana. El pedido queda durable para el worker; decisión, actor y fecha sobreviven al resultado. Retry sólo parte de Failed+CanRetry; consultar o esperar no autoriza reenviar.
La revisión humana fija destinatario y Fence esperado y admite replay idéntico sin reabrir después del consumo. Una decisión de otro intento da conflicto.

La referencia histórica conserva opcionalmente TranslationsJson, con textos/detalles/nombres por idioma presentes en la oferta aceptada. No consulta ni copia cambios posteriores del catálogo.

Etapa 5: Definition.OfferWhen conserva instrucciones monolingües opcionales bajo la misma revisión del borrador. Guardar o vaciar este texto no cambia ActiveVersionId, SchemaJson ni versiones selladas; la publicación no lo incorpora.

Etapa 5, s2: BusinessFormSuggestionSet conserva revisiones inmutables propias y versiones observadas para metadata histórica; Definition.ActiveSuggestionSetId no cambia borrador ni publicación. Cada sugerencia conserva identidad, orden, activación, condición y mapeos. RecordNumber es metadata con FieldId ausente, nunca una pregunta ficticia; no agrega pasos ni estados de proceso.

Etapa 5, H4: BusinessFormCatalog conserva propuesta y evidencia original inmutables, decisión y autor/fecha. La fábrica de propuesta Filters comienza inactiva en revisión1; Aprobar guarda edición y activación en una revisión. Pending y Discarded no se activan por UpdateFilters ni SetActive. La creación normal conserva su comportamiento.

Etapa 5: el registro conserva CaptureOriginJson nullable y los enlaces OriginRecordId/OriginSuggestionId al confirmar una captura real. SetCaptureOrigin sella una sola vez con contenido obligatorio y pareja de IDs de sugerencia completa y no vacía. La ausencia histórica permanece desconocida; sugerir o enviar un formulario no crea un registro. No se reconstruye procedencia con la configuración de IA vigente.

H7: BusinessFormMissingTopic conserva nombre/frase/cultura originales y revisión; Discard fija actor/UTC y un pedido nuevo vuelve a Pending sin borrar ese hecho. BusinessFormMissingRequest guarda sólo recibo opaco único, tema, persona e instante original; no guarda mensajes ni nombres de quien pide. No se agrega un estado de resolución al crear un formulario.

Review24: Record conserva PresentationCulture nullable para registros históricos reales. Create fija null/es/en y no la modifica en un replay; no se reconstruye desde la cuenta o traducciones actuales. La cultura capturada del autor no cambia la cultura propia de otros destinatarios del rol.

La baja de Definition exige una versión activa publicada y revisión vigente; registra actor/UTC e incrementa esa misma revisión sin alterar borrador, ActiveVersionId, punteros de configuración ni LastRecordNumber. Guardar o aceptar una publicación no limpia la baja: sólo ActivateVersion con una versión nueva y CAS correcto la retira. Reactivar no reutiliza la versión anterior.

Definition implementa ISoftDeletable: sus campos de borrado son privados y los fija exclusivamente el interceptor al remover la entidad. La baja y el borrado siguen siendo hechos distintos; el prefijo, versiones, configuraciones y numeración no se reinician.
