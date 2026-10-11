# Formularios: borradores localizados y chequeo local

**Decisión humana D5, 2026-10-10:** BusinessForms no fue commiteado ni desplegado; no hay borradores/comprobantes reales que preservar. Se cambia directamente el contrato a name mapa es/en, sin Asp.Versioning/ADR, nameByCulture ni una variante legada ficticia. Se regenera la inicial AddBusinessFormDrafts no commiteada, sin migración extra ni update de bases locales. [Contrato de etapa1](../plans/formularios-etapa-1-contrato.md) y [contrato/evidencia D5](../reviews/2026-10-10-formularios-d5-contrato.md) fijan ejemplos y verificaciones. La propuesta técnica previa v1/v2 está reemplazada.

BusinessForms es módulo opcional independiente de IA y WhatsApp. Implementa crear completo, editar, lista/facetas, obtener/recuperar intento propio, chequeo y aceptación durable de publicación, coordinación por puerto neutral, activación y preview de versiones publicadas. El chequeo sigue siendo una consulta pura. También envía formularios por WhatsApp con una sesión durable fijada a la versión aceptada; recepción y captura permanecen pendientes. La calificación real con Meta sigue bloqueada por la salud de la cuenta.

## Persistencia y responsabilidad

BusinessFormDefinition y BusinessFormDraftChange heredan Entity e IAuditable; sólo interceptores completan auditoría. AddBusinessFormDrafts agrega FormDefinitions y FormDraftChanges. NameJson guarda el mapa de nombre autoritativo; Name deriva de su idioma original en el mismo commit y nunca es otro nombre editable. DisplayNameEs/En son columnas calculadas de ese mapa, con Name original como referencia de autoría si falta la traducción; no son fuentes de contenido ni caché.

Cultura, nombre y botón son metadata de la definición, fuera de SchemaJson. SchemaJson contiene format=1 y fields de preguntas únicamente; ledger de identidades retiradas separado. QuestionCount se deriva del snapshot en el mismo commit para SQL. No tablas de traducciones. Fields y versiones se crean al aceptar publicación; no hay registros de captura.

El borrador usa fields; el schema sellado de versión usa questions. Decimal/date/boolean son tipos de pregunta, sin captura. La futura representación decimal es string exacto .NET y la fecha DateOnly.

## Identidad y autoría por idioma

Sólo servidor genera FieldId/ChoiceId v7; key es field_ + FieldId formato N y permanece con la identidad, nunca label. UUID recibido se busca en el borrador de la definición y su padre. LocalId correlaciona intento/mapa protegido, sin entrar en SchemaJson.

Renombrar, traducir y reordenar conserva IDs. Cambio de tipo exige replacesFieldId y otra identidad/configuración. Omitir retira campo/opción; ledger impide resurrección. Opciones no se mueven de padre. Helpers puros de Domain conservan mapas copiados/Frozen y colecciones readOnly; no son codec de persistencia ni autoridad alternativa del borrador productivo.

Name, labels, opciones y submitLabel son mapas es/en en HTTP/Application/persistencia/recibos. Original draft.culture es inmutable y válido; sus textos siempre no vacíos. Secundarios pueden faltar o ser "" al guardar; whitespace secundario se canoniza a "". Nombre se recorta en ambos idiomas. Se preservan vacíos explícitos/ausencias sin inventar traducciones. "" retira deliberadamente una secundaria; omitir una traducción no vacía existente falla TranslationsWouldBeLost. Una clave ya vacía puede omitirse. Sin merge de concurrencia ni editedCulture como autoridad.

Botón omitido al crear tiene default Enviar/Submit sólo original; no rellena la traducción de un botón personalizado. Help input admite strings nullable por cultura: vacío/null explícito retira esa entrada conservando las demás. Canonical help es null si no queda texto, o mapa de strings no vacíos parcial/completo. Omisión accidental de ayuda secundaria existente falla. No inventar ayuda.

Guardar exige original válido y admite cero preguntas. Choice exige ≥1 opción e IDs compartidos; duplicados se rechazan sólo en el original por NFC+Trim+OrdinalIgnoreCase, señalando la segunda fila. No nueva restricción secundaria por inferencia. Claves ajenas es/en, null en nombre/labels/opciones/botón y configuraciones desconocidas se rechazan.

Budgets de autoría revisables e independientes de Meta: cada nombre hasta120 UTF-16; prefijo ASCII normalizado 1..12 y único; labels/opciones/botón hasta160 elementos Unicode y ayuda hasta1000; 40 preguntas/50 opciones; cuerpo512KiB y resultado protegido1MiB. Short/long maxLength default80/600, rango1..10000. No truncado.

## Listado, búsqueda y facetas coherentes

Respuesta name es mapa completo. El SPA muestra la cultura UI; si falta su nombre, usa original como referencia de autoría y puede indicar traducción pendiente. Búsqueda, orden name y facetas usan **esa misma proyección en SQL**, según cultura UI resuelta por Accept-Language o DefaultCulture. No orden/paginación en memoria, N+1 ni búsqueda en otro texto oculto. Prefix sigue searchable; ApplySort desempata por Id y ToPagedResultAsync pagina. Nombre original no es identidad ni permiso; originales en/es funcionan simétricamente.

Esta referencia de autoría del listado/preview no permite fallback de texto en Flow publicado. La cultura futura del empleado procede de cuenta vinculada activa, no UI del operador; envío aún no implementado.

## Un guardado y recuperación exacta

Create/Update validan permisos/entrada y abren una UoW OnSuccess. Dentro: lock actor/UUID, intento previo, lock global de definiciones/prefijos, revisión/identidades/prefijo, definición/ledger y resultado protegido. Helpers no guardan; retención tiene caso de uso técnico propio. Medir contención antes de dividir lock global.

Mismo actor/UUID/cuerpo canónico devuelve snapshot/mapa de su commit con201/200, sin avanzar revisión. Otro kind/definición/cuerpo da RequestConflict. Huella cubre todos los nombres/textos por cultura, vaciados explícitos, IDs/localIds/orden/revisión y defaults; mapas ordenados por clave. Recuperación devuelve el resultado original aunque currentRevision sea mayor; committedAtUtc viene de auditoría confirmada. Exacto se refiere al contenido del snapshot, no bytes HTTP/escape Unicode. 404 en vuelo no demuestra rollback; conservar UUID/cuerpo.

DataProtection usa ArquitecturaBase.BusinessForms.DraftChanges.v1, ligado a fila/actor/UUID/definición/kind; codec cerrado format=1 ahora contiene el contrato localizado directamente. Sin v1/v2 artificial ni reescritura de datos reales. SHA256 normalizado vive sólo en payload protegido; ningún hash privado/cuerpo/resultado en columna/log en claro. Restore incluye anillo protegido y certificados de la plantilla, sin credenciales IA/Meta.

Resultado recuperable30d; luego GET/replay410 antes de abrir payload. Purga seguida100 al guardar y cada15min; tombstone conserva actor/UUID/kind/definición/revisiones/expiración/auditoría contra reutilización. Sin ExecuteUpdate/Delete ni retención nueva por D5.

## HTTP, permisos y composición

Las seis rutas MVC de autoría bajo /api/forms son: GET definitions/facets/detail, POST definitions, PUT definitions/{id}/draft y GET draft-changes/{requestId}. Controllers sólo IFormDraftService; contratos propios mapeados a mano, cerrados y redactados. name es mapa también en fila/list/detail/result; no propiedad nameByCulture. ProblemDetails traducido code/traceId/errors; paths name.es/name.en; 409 conflictos y410 expiración.

Read siempre; Manage adicional al escribir. Recuperación Read + actor propio, sin Manage. Catálogo opcional aporta publish y formrecords.create/read para recorridos, sin nuevos grants/ACL por formulario. Seed Admin/composición/Application/Infrastructure/Api/Program existentes. No versionado ni endpoints nuevos por D5.

## Chequeo local de publicación — incremento 2a, 2026-10-10

POST definitions/{id}/publication-checks: MVC → IBusinessFormPublicationService → lector → IStructuredFormChannel. Read+Publish, sin Manage. Mismos512KiB y forms-write30/min por actor. Nombre mapa/ExpectedRevision/Prefix/Draft guardados, LocalId correlación y FieldId/ChoiceId definitivos. Normaliza y compara todo el canónico, incluidos mapas de nombre; revisión obsoleta409 RevisionConflict, contenido/IDs nuevos/ajenos/reemplazos409 DraftMustBeSaved. No IDs nuevos, escritura, lock/UoW ni Meta.

FormPublicationPolicy comprueba **es/en completos** en name, submitLabel, labels y opciones; help completo si existe cualquiera. Issue200 con Forms.Publication.MissingTranslation, culture faltante, path preciso name.en / draft.submitLabel.en / draft.fields[i].label.en/help.en/options[j].label.en y FieldId/ChoiceId cuando aplican. ChannelId null identifica regla funcional previa al canal. Incompleto queda Blocked sin compilación parcial ni fallback. E puede mostrar original debajo/en cursiva sólo para orientar al autor.

Response200: definitionId/draftRevision, compatibility, canAccept, acceptanceAvailability, channels, issues y diff. Compatibility LocallyValidated/Blocked/Unavailable; ausencia de canal explícita, IDs de canal únicos/no vacíos. Sin versión activa, diff.baseVersionId=null y added son las preguntas guardadas. Con activa, se leen preguntas selladas y se comparan added/changed/removed por ID. En el corte 2a no existía aceptación; en 2b canAccept y acceptanceAvailability consideran configuración y operación abierta; ExternalEvidence=Pending separada, sin bloquear sólo por piloto pendiente. No JSON Flow/mapping/hash privado/Meta ID/credencial/preview en HTTP.

Puerto neutral Interfaces/Channels y DTOs Models/StructuredData; sin tipos de módulos/HTTP/EF. WhatsApp aporta phone con TryAddEnumerable incluso si transporte apagado, porque consulta pura. Quitar módulo produce ChannelUnavailable. Compilador/codec/JSON/mapping/perfil pertenecen al canal; defensa de textos por cultura exacta, sin importar BusinessForms.

Perfil flow-static-7.3-r3/JSON7.3, seis tipos, complete terminal/una pantalla, sin endpoint. Sí/No sin default, false no ausencia; decimal exacto textual; DateOnly CalendarPicker single/required. Ayuda/config/textos no representables se bloquean por identidad/cultura, sin recortar/dividir. El botón de apertura es Abrir formulario/Open form y queda separado de submitLabel.

[Perfil del canal](whatsapp.md#perfil-local-de-formularios--2026-10-10). [Evidencia r1](../reviews/2026-10-10-formularios-incremento-2a.md) es anterior a D5 y no acredita completitud nueva; [contrato/evidencia D5](../reviews/2026-10-10-formularios-d5-contrato.md) registra pruebas actuales. [Revisión durable](../reviews/2026-10-10-formularios-publicacion-contrato-durable.md) conserva la procedencia de propuestas 2b; las reglas implementadas actuales se consolidan abajo.

## Aceptación durable — primer corte 2b, 2026-10-10

Check devuelve checkToken/checkExpiresAtUtc (10 minutos) sólo si la capacidad local del canal admite publicación y no hay operación abierta. Compatibilidad y evidencia externa siguen separadas. POST definitions/{id}/publications recibe RequestId/ExpectedRevision/CheckToken y el mismo Name mapa/Prefix/Draft; Read+Publish, sin Manage, 512 KiB y 30/min. Un solo OnSuccess, locks actor/UUID → global, permisos actuales y snapshot guardado. Replay por actor/UUID/payload normalizado ocurre antes de expiración/revisión/perfil; nunca crea otra versión.

El commit registra identidades en BusinessFormFields, versión inmutable SchemaJson jsonb de format/questions (ContentHash sólo del esquema) y BusinessFormPublicationOperations con manifiesto protegido de presentación/perfil/huellas. Fija PrefixLocked y conserva el borrador; no activa ni llama al proveedor. Recibo 202 con Location hacia GET definitions/{id}/publications/{requestId}; lectura propia Read+Publish (120/min), 404 ajena/inexistente. Historia y deduplicación se conservan mientras exista la versión; no heredan los 30 días de DraftChanges ni reutilizan su outbox.

La migración aditiva 20261010141855_AddBusinessFormPublications incluye tres tablas y ActiveVersionId, con FK compuesta para pertenencia, número único por definición y una operación abierta. Dominio ya verifica lease/fence y activación por base activa sin sobreescribir un borrador más reciente. Ese primer corte sólo demostró aceptación local con un canal de TestFeatures; el siguiente apartado describe la coordinación externa implementada.

Evidencia de este corte: build Release sin advertencias; Application 733/733, Domain 289/289, Architecture 139/139; integración focalizada 34/34 con PostgreSQL/Redis, rollback, actor/token/permisos, expiración, replay y dos hosts. EF sin cambios pendientes. Suite completa 2450/2450 corresponde al cierre D5 anterior; no se atribuye a este corte 2b.

## Publicación y preview — implementación 2b, 2026-10-10

El usuario autorizó 2b y adoptar las propuestas técnicas existentes; confirmó agregar WhatsApp:WabaId en la conexión actual y verificar pertenencia. No se requirió otra conexión ni cambiar la validación de inicio del envío de texto. El chequeo local emite capacidad según configuración conocida; no consulta la cuenta ni atribuye una aprobación Meta a un 202.

FormPublicationDispatchService reserva y completa en límites OnSuccess propios con el lock global existente, y devuelve pedidos neutrales por perfil armados desde el manifiesto/schema sellados. FormPublicationChannelDispatchService invoca Accept/GetPublication fuera del límite y transforma indisponibilidad en progreso incierto; el worker sólo coordina scopes separados de reserva, despacho y completado. El canal acepta idempotentemente por OperationId y huella y guarda su propia operación/proyecciones; nunca se llama a un escritor del canal desde un límite BusinessForms. Si la aceptación devuelve un error, BusinessForms conserva la operación como Uncertain y reintenta con los mismos IDs después de la espera, sin consumir otra versión ni cerrar la operación como fallo terminal. CompleteAsync decide estado y activación en Application. Reinicio después del commit del canal recupera el mismo ID desde otro host. El manifiesto contiene es/en y sólo puede activar cuando todos los resultados son Published verificados.

La activación exige pertenencia, fence vigente, permisos actuales y BaseActiveVersionId. No exige que siga vigente DraftRevision: un borrador posterior se conserva. El completado ya confirmado es idempotente; fences distintos fallan. Un conflicto de base activa queda Uncertain/ActivationConflict, sin sobrescribir otra versión. La revocación impide nuevos efectos; resultados y recursos conocidos se conservan para revisión. No se desbloquea Prefix ni se reemplaza la activa anterior por un fallo. CanRetry=false: no hay acción HTTP que repita efectos inciertos; recuperación técnica conserva la misma operación.

GET de operación propia mantiene Read+Publish y agrega issues traducidos por campo/cultura provenientes del canal; HTTP no contiene JSON, hash de compilación, Meta ID, WABA, token ni mensajes brutos. Lista/facetas filtran Published por existencia de activa y Drafts por ausencia, con conteos SQL coherentes; QuestionCount sigue contando el borrador. ActiveVersionNumber procede de la versión activa. El diff compara snapshots sellados por identidades, no etiquetas.

GET definitions/{id}/versions/{versionId}/preview exige Read vigente y pertenencia de versión publicada. Cultura explícita es/en en query, independiente de la cultura UI; consulta de canal sin UoW/locks. Devuelve URL HTTPS oficial y ExpiresAtUtc exacto con Cache-Control: no-store. El canal verifica owner, perfil, bytes del asset, estado y health; no acepta URL suministrada por cliente. El DTO redacta la URL portadora. No crea sesión/token de captura. CSP/iframe/dispositivos y round-trip real quedan para calificación.

[WhatsApp](whatsapp.md#publicación-durable-de-flows--2b-2026-10-10) conserva perfil, bindings, transporte y recuperación. La migración aditiva 20261010150042_AddWhatsAppFlowPublications sólo agrega sus dos tablas, sin FK/joins a BusinessForms. Esta implementación no envía formularios a contactos ni recibe respuestas de captura.

Verificación focalizada: 38/38 casos HTTP/durable de formularios con PostgreSQL/Redis y 7/7 del adaptador HTTP Flows sin red. Build Release sin advertencias. Suite completa posterior de 2b: 2482/2482, cero omitidos, con integración Docker (12m23s). EF no informa cambios pendientes; migraciones desde base vacía y Up/Down desde el esquema previo en PostgreSQL. La primera pasada tuvo un falso positivo de privacidad: el ID ficticio 703 coincidía con parte de un UUID; la aserción ahora compara el valor JSON completo y la repetición completa quedó verde. Revisión: fronteras de módulo, un límite por escritor, scopes sin transacción durante HTTP, fences/permisos/CAS, recursos conservados y DTOs redactados. Comprobación real adicional: WABA de la conexión verificado por lectura Graph y guardado en user-secrets; no acredita creación/publicación/preview/dispositivos ni round-trip reales, que siguen pendientes.

## Recarga de publicación y cultura de preview — mínimo 2

GET definitions/{id} conserva definition/capabilities/audit y agrega publication. activeVersion contiene versionId/versionNumber/draftRevision o null; currentOperation es la última aceptación por número de versión, con versionId/versionNumber/draftRevision/state/stage/requiresReview/canRetry=false y requestId/recoveryPath nullable. Sólo el actor propio con Publish vigente recibe esos dos datos de recuperación; otros lectores autorizados ven el agregado seguro, sin manifiesto, ID externo, huellas ni UUID de otro actor. capabilities agrega canPublish (permiso actual) y canPreview (existe activa), sin sustituir el chequeo de aceptación.

publication.displayState y displayState del listado reflejan la última operación, o Draft cuando no hay ninguna. Se conserva ActiveVersionNumber y el filtro/facetas Published por ActiveVersionId, aunque la nueva operación esté pendiente o Failed. El badge no define el filtro ni borra la activa anterior. Las consultas son SQL mediante el lector existente; sin N+1, caché ni límites de escritura para una lectura.

GET de preview exige ?culture=es o ?culture=en; ausente u otra cultura responde400 Forms.Preview.CultureInvalid con errors.culture, siempre no-store. No modifica CurrentUICulture ni el idioma de una cuenta. El canal WhatsApp rechaza un vencimiento explícito con WhatsApp.Flow.PreviewUnavailable sin URL; el guard neutral conserva Forms.Preview.Expired si otro canal devuelve un resultado ya vencido. Pedir nuevamente la preview es una consulta explícita, nunca una renovación automática ni otro intento de publicación. URL y ExpiresAtUtc siguen procediendo del proveedor.

### Presentación sellada de versión — base del mínimo 3, 2026-10-10

BusinessFormVersion conserva NameJson, SubmitLabelJson, Culture y QuestionCount inmutables, separados del SchemaJson de preguntas y su ContentHash. La aceptación los sella del mismo snapshot validado; editar el borrador no modifica esa presentación. DisplayNameEs/En son columnas SQL calculadas desde el mapa de la versión, sin fallback al borrador actual. La migración AddFormVersionPresentationAndContactLinkRevision agrega las columnas, sin reescribir migraciones ya aplicadas.

Para versiones anteriores, FormVersionPresentationSeeder participa del límite técnico existente de DatabaseSeeder y su lock de seed. Sólo restaura la presentación ausente desde el manifiesto protegido de esa misma operación/versión y cuenta las preguntas del esquema sellado; verifica pertenencia/hash y falla ante inconsistencias. No abre otro límite, guarda por su cuenta, llama a Meta ni modifica SchemaJson, ContentHash, identidades, snapshot, operación o bindings. Repetir el seed conserva la presentación ya sellada. El núcleo conoce únicamente IDatabaseSeedParticipant de Infrastructure, sin nombrar el módulo. La auditoría la completan los interceptores. El selector y el envío durable están implementados; la recepción/captura del mínimo 3 sigue pendiente.

El consumidor de selección real usa IStructuredFormSource neutral: núcleo TryAdd apagado devuelve página vacía/cancelable y BusinessForms Replace scoped lo sustituye de manera idempotente en ambos órdenes de registro. El adaptador verifica employeeRecords.create del destinatario y el lector usa sólo versión activa con operación Published y presentación sellada completa; nunca nombre del borrador ni fallback por idioma. La composición del núcleo no referencia módulos. Selección, sesión y salida durable implementadas; la recepción/captura continúa pendiente.

## Envío durable de formularios — incremento 3

WhatsApp lista sólo versiones publicadas elegibles para la cuenta explícitamente vinculada al contacto. Encolar revalida vínculo, estado, idioma, permiso del destinatario y revisión del contexto; el operador conserva permisos separados. La selección usa la versión activa, mientras una sesión ya aceptada mantiene su versión y presentación aunque luego se publique otra.

La sesión fija contacto, cuenta, operador, definición, versión, idioma, binding, expiración y huella del destinatario. Token aleatorio y payload sensible se protegen con Data Protection; la correlación usa hash fijo en comparación de tiempo constante. El mensaje Flow entra en la cola común de WhatsApp en el mismo commit que la sesión. El worker verifica WABA/PhoneNumberId y vuelve a autorizar antes de enviar; nunca publica desde el chat ni reenvía un despacho incierto automáticamente. Su POST real a Meta sigue sin calificación, y el bloqueo de salud 141006 impide acreditar un piloto.
