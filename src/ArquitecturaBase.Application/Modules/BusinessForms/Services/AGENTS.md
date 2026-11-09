# BusinessForms/Services: casos de uso y helpers

Casos de uso y helpers; contratos en `Interfaces/Services`. Leer [formularios](../../../../../docs/features/formularios.md), [Channels](../Channels/AGENTS.md) y [Persistence](../Interfaces/Persistence/AGENTS.md).

## Al modificar

- Esta guía es el índice: antes de tocar un tema, leé su sección en el [detalle de los servicios](../../../../../docs/features/formularios-servicios.md) y, si cambia una regla, actualizala ahí en el mismo cambio.
- Ejemplo (`FormLifecycleService`): guard → validador → `OnSuccess` sólo si escribe, con lock, permisos vigentes y revisión adentro. Ninguna lectura abre UoW.
- El replay de un pedido repetido va antes de mirar versión, revisión o token; en la captura, antes de los permisos.
- Publicación y acciones reservan y completan el despacho en límites separados; un resultado incierto queda `Uncertain` y no se repite a ciegas.
- El idioma original de un catálogo o un dato de las personas se fija al crear y es el único obligatorio; lo que falta en un idioma se muestra en el otro, sin suponer el español.
- El asistente y las sugerencias no prellenan ni mapean multiChoice, las preguntas del bloque de renglones ni las que eligen primero por un campo.

## Temas

- [Borradores y autoría](../../../../../docs/features/formularios-servicios.md#borradores-y-autoría): guardar y chequear el borrador (`FormDraftService`, `FormDraftPolicy`), multiChoice, bloque de renglones y títulos de sección. Guardar o chequear no crea campos ni versiones; cambiar el tipo o entrar o salir del bloque es identidad nueva.
- [Publicación](../../../../../docs/features/formularios-servicios.md#publicación): chequeo, aceptación, despacho, estado y vista previa (`FormPublication*`). El chequeo sólo consulta; la aceptación sella sin activar ni llamar al proveedor, y el despacho activa sin pisar un borrador posterior.
- [Ciclo de vida: dar de baja, reactivar y eliminar](../../../../../docs/features/formularios-servicios.md#ciclo-de-vida-dar-de-baja-reactivar-y-eliminar): `FormLifecycleService`. La baja conserva la versión activa, reactivar es publicar el borrador y eliminar exige `LastRecordNumber` cero y ningún envío abierto.
- [Datos de personas](../../../../../docs/features/formularios-servicios.md#datos-de-personas): atributos, tabla, valores y opciones de `FormPersonDataService`. Un atributo usado no cambia tipo, fuente ni actividad, y cambiar tipo o fuente con valores cargados exige `ConfirmValueRemoval`.
- [Catálogos y filtros](../../../../../docs/features/formularios-servicios.md#catálogos-y-filtros): catálogos Manual y Filters, opciones, campos, eliminar opciones, importación y exportación (`FormCatalogService` y sus recorders). Se escriben bajo el lock del catálogo con una revisión compartida, y `ValidateItems` es el único escritor de `FieldValuesJson`.
- [Captura de respuestas y archivos](../../../../../docs/features/formularios-servicios.md#captura-de-respuestas-y-archivos): `BusinessFormRecordRecorder`, respuestas y renglones (`FormRecordAnswerPolicy`), origen, campos extra y `FormFileService`. Los archivos se suben antes del límite, un rechazo guarda sólo el recibo y las huellas anteriores no cambian.
- [Carga desde la app](../../../../../docs/features/formularios-servicios.md#carga-desde-la-app): `FormEntryService`: describir, opciones, envío, listas dependientes, renglones y elegir primero por un campo. `FormEntryAccessGuard` va antes de leer y dentro del límite, y una versión que no es la activa da 409.
- [Planilla, detalle y Excel](../../../../../docs/features/formularios-servicios.md#planilla-detalle-y-excel): planilla, detalle, navegación, resumen, corregir y anular, renglones y Excel. RecordsRead se revalida antes de devolver, sin UoW ni caché; con sólo RecordsCreate se ve lo propio (`FormRecordAccessGuard`).
- [Reglas, acciones y aprobaciones](../../../../../docs/features/formularios-servicios.md#reglas-acciones-y-aprobaciones): reglas, sus condiciones (un mismo ítem del bloque, como «Filtros») y su mensaje (el bloque, un ítem por línea), runs y despacho de acciones, workflow y aprobaciones. `FormActionRunRecorder` fija destinatarios, texto y cultura dentro del límite de captura o decisión, y nadie aprueba lo propio.
- [PDF](../../../../../docs/features/formularios-servicios.md#pdf): diseño, vista previa y documento (`FormPdf*`). Preparar fija diseño, culturas y `AppName`; ejecutar relee el registro y escribe una sola vez (`WriteOnceAsync`) tras consultar.
- [Asistente y sugerencias](../../../../../docs/features/formularios-servicios.md#asistente-y-sugerencias): candidatos, propuesta, prellenado, aclaración y mapeo (`FormAssistant*`, `FormSuggestion*`). Vista previa, prellenado y aclaración no escriben ni bloquean; el envío toma los locks en la UoW del canal.
- [Formularios que faltan](../../../../../docs/features/formularios-servicios.md#formularios-que-faltan): `FormMissingRequestService`. Sólo una lectura completa justifica un pedido, y la privacidad se revisa al proponer y al capturar.
- [Idiomas](../../../../../docs/features/formularios-servicios.md#idiomas): cultura común, nombres y opciones mostrados y lecturas históricas. Antes de ofrecer o enviar, `FormPresentationCulturePolicy` elige una cultura común; quien respondió en el original no pasa al secundario.
- [Preguntas filtradas y presentación](../../../../../docs/features/formularios-servicios.md#preguntas-filtradas-y-presentación): filtros (`FormCatalogMatchPolicy`), presentación, vista previa por persona y máximo por persona. Aviso, bloqueo y máximo del editor salen de `FormCatalogRecipientMaximumPolicy`; los avisos nunca frenan aceptar.
- [Crear desde una propuesta del asistente](../../../../../docs/features/formularios-servicios.md#crear-desde-una-propuesta-del-asistente): `FormDraftProposalService` y `FormCatalogAssistantProposalService`, desde Inicio o un canal. Piden Manage del área y buscan lo ya creado antes de leer la propuesta; el borrador usa el `ProposalId` como `RequestId`. `FormAttachmentPreparationPolicy` prepara la planilla adjunta al asistente en 12 KiB.

## Referencias

- [Detalle de los servicios por tema](../../../../../docs/features/formularios-servicios.md).
- [Pruebas unitarias](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/BusinessForms/AGENTS.md).
