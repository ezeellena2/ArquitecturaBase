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

G2 guarda diseño PDF por revisión propia y `ActivePdfDesignId`, sin cambiar borrador/publicación. El run fija diseño, datos, culturas completas y nombre del sistema. La emisión usa creación condicional por run/cultura, consulta primero y conserva bytes ya subidos; metadata se confirma sólo con fence vigente. El preview usa ejemplos y no emite ni guarda. Documentos privados sólo por API con `formrecords.read` antes y después de abrir el stream.

Revisión de avisos: sólo NotifyAuthor/NotifyRole inciertos aceptan una decisión humana. El pedido queda durable para el worker; decisión, actor y fecha sobreviven al resultado. Retry sólo parte de Failed+CanRetry; consultar o esperar no autoriza reenviar.
La revisión humana fija el Fence esperado y admite replay idéntico sin reabrir después del consumo. Una decisión de otro intento da conflicto.
