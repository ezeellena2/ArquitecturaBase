# Formularios: borradores localizados y chequeo local

**Decisión humana D5, 2026-10-10:** antes de los primeros commits del módulo, el usuario autorizó adaptar directamente el borrador y regenerar su migración inicial: nombre, preguntas, ayudas, opciones y botón son mapas es/en; se guarda incompleto y se publica con ambos idiomas completos. Sin Asp.Versioning/ADR ni convivencia legada ficticia. El contrato vigente está en DTO/OpenAPI y ejemplos HTTP exportados; el contrato Markdown anterior conserva únicamente procedencia. La exigencia de publicar ambos idiomas de ese corte fue sustituida por [original obligatorio y traducción opcional](#idioma-original-y-traducción-opcional--2026-10-19).

**Robustez desde la Etapa 2 ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md), 2026-10-11):** catálogos, reglas, diseño del PDF, sugerencias y cualquier otra configuración interna se guardan con un límite por método, control de revisión y 409, sin comprobantes recuperables, vencimientos de 30 días ni pruebas de varios hosts. La durabilidad ya implementada del borrador se conserva. Publicar en Meta, mandar formularios y mensajes, armar y subir el PDF y ejecutar acciones de reglas conservan la recuperación durable, porque tienen efecto fuera del sistema.

BusinessForms es módulo opcional independiente de IA y WhatsApp. Implementa autoría, publicación durable, coordinación por puertos neutrales, activación, preview y captura idempotente de respuestas de Flows. WhatsApp no lo referencia: traduce respuestas con su mapping sellado y usa `IStructuredRecordSink`/`IStructuredRecordSource`. El round-trip físico fue comprobado el 2026-10-11; alcance y pendientes en la verificación de cierre.

## Persistencia y responsabilidad

BusinessFormDefinition y BusinessFormDraftChange heredan Entity e IAuditable; sólo interceptores completan auditoría. AddBusinessFormDrafts agrega FormDefinitions y FormDraftChanges. NameJson guarda el mapa de nombre autoritativo; Name deriva de su idioma original en el mismo commit y nunca es otro nombre editable. DisplayNameEs/En son columnas calculadas de ese mapa, con Name original como referencia de autoría si falta la traducción; no son fuentes de contenido ni caché.

Cultura, nombre y botón son metadata de la definición, fuera de SchemaJson. SchemaJson contiene format=1 y fields de preguntas únicamente; ledger de identidades retiradas separado. QuestionCount se deriva del snapshot en el mismo commit para SQL. No tablas de traducciones. Fields y versiones se crean al aceptar publicación; los registros, valores tipados y recibos de captura se crean sólo al procesar una respuesta válida.

El borrador usa fields; el schema sellado de versión usa questions. La captura normaliza decimal exacto, fecha `DateOnly`, booleano y ChoiceId contra esa versión inmutable; la ausencia opcional queda distinta de `false` y cero.

**Numeración e inmutabilidad.** El número de una respuesta es `PREFIJO-NNNNNN` (seis dígitos). Se asigna dentro del commit de captura, bajo el lock global de definiciones, con índice único (definición, número) entre las respuestas originales: no se reutiliza ni se renumera, un replay devuelve el número original y un rechazo no consume número. Lo guardado no se modifica, venga de WhatsApp o de la app: corregir guarda otra respuesta que toma el número de la que reemplaza y la deja enlazada tal como se avisó, aprobó o imprimió, y anular sólo le agrega el estado «Anulada» con su autor, fecha y motivo ([Etapa 3](#corregir-y-anular--etapa-3-2026-11-05)). El tipo `group` es el bloque de renglones: uno por formulario y fuera de las 40 preguntas, con su mínimo y máximo (`rows`) y sus preguntas contiguas detrás, marcadas `repeated`; habilitarlo no reescribe esquemas `format=1`.

## Identidad y autoría por idioma

Sólo servidor genera FieldId/ChoiceId v7; key es field_ + FieldId formato N y permanece con la identidad, nunca label. UUID recibido se busca en el borrador de la definición y su padre. LocalId correlaciona intento/mapa protegido, sin entrar en SchemaJson.

Renombrar, traducir y reordenar conserva IDs. Cambio de tipo exige replacesFieldId y otra identidad/configuración. Omitir retira campo/opción; ledger impide resurrección. Opciones no se mueven de padre. Helpers puros de Domain conservan mapas copiados/Frozen y colecciones readOnly; no son codec de persistencia ni autoridad alternativa del borrador productivo.

Name, labels, opciones y submitLabel son mapas es/en en HTTP/Application/persistencia/recibos. Original draft.culture es inmutable y válido; sus textos siempre no vacíos. Secundarios pueden faltar o ser "" al guardar; whitespace secundario se canoniza a "". Nombre se recorta en ambos idiomas. Se preservan vacíos explícitos/ausencias sin inventar traducciones. "" retira deliberadamente una secundaria; omitir una traducción no vacía existente falla TranslationsWouldBeLost. Una clave ya vacía puede omitirse. Sin merge de concurrencia ni editedCulture como autoridad.

Botón omitido al crear tiene default Enviar/Submit sólo original; no rellena la traducción de un botón personalizado. Help input admite strings nullable por cultura: vacío/null explícito retira esa entrada conservando las demás. Canonical help es null si no queda texto, o mapa de strings no vacíos parcial/completo. Omisión accidental de ayuda secundaria existente falla. No inventar ayuda.

Guardar exige original válido y admite cero preguntas. Choice exige ≥1 opción e IDs compartidos; duplicados se rechazan sólo en el original por NFC+Trim+OrdinalIgnoreCase, señalando la segunda fila. No nueva restricción secundaria por inferencia. Claves ajenas es/en, null en nombre/labels/opciones/botón y configuraciones desconocidas se rechazan.

Budgets de autoría revisables e independientes de Meta: cada nombre hasta120 UTF-16; prefijo ASCII normalizado 1..12 y único; labels/opciones/botón hasta160 elementos Unicode y ayuda hasta1000; 40 preguntas/50 opciones; cuerpo512KiB y resultado protegido1MiB. Short/long maxLength default80/600, rango1..10000. No truncado.

## Idioma original y traducción opcional — 2026-10-19

El original inmutable `draft.culture` es el único idioma obligatorio para guardar y publicar. La traducción es opcional, sin autotraducción ni cambios en el idioma de la aplicación. El chequeo exige el original completo e informa por separado si el secundario está completo o qué le falta; esos faltantes no bloquean la publicación del original. A cada canal se le piden únicamente las culturas completas, y Meta recibe un Flow por cada una. Agregar una traducción completa o perderla en una publicación posterior crea una versión nueva.

`activeVersion.cultures` informa las culturas publicadas de la versión activa; `status.cultures` informa las capturadas por esa operación, incluso mientras sigue Accepted o Publishing. Ambas se leen de su manifiesto sellado, sin inferirlas del borrador actual ni de los mapas de una presentación parcial. `diff.addedCultures` y `diff.removedCultures` comparan las culturas completas comprobadas con las publicadas de la versión activa.

**Decisión del usuario, 2026-10-23:** nadie está obligado a escribir en español ni en inglés, y ninguna pantalla elige el idioma del contenido. Lo que se crea (un formulario, un catálogo con sus campos y opciones, un dato de las personas) se escribe en el idioma del menú: ese es su idioma original, no cambia después y es el único obligatorio. El otro idioma es una traducción opcional. En las pantallas, cada texto se muestra en el idioma del menú y, si falta, en el original; lo que viaja a WhatsApp sigue las reglas de abajo. Para traducir se cambia el idioma del menú y se edita lo mismo. La regla alcanza a los catálogos (nombre, descripción y opciones), a sus campos y a los datos de las personas, y retira la excepción bilingüe que conservaba el alta común de catálogos.

I1–I3 reemplazan la regla de idiomas de E/P1 en el [diseño elegido](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros), con el ajuste de esa decisión: el editor no tiene selector de idioma ni pares de campos por idioma. Cada texto se edita en un solo campo, en el idioma del menú, y un formulario nuevo nace con ese idioma como `draft.culture`. Con el menú en el idioma que no es el original, un texto sin traducción muestra el original como referencia, sin copiarlo ni guardarlo como traducción. La comprobación de publicación sigue avisando lo que falta del original, y un error de guardado en un texto que no está a la vista se muestra en la página. La vista previa orientativa usa una sola cultura para nombre, preguntas, ayudas, opciones, botón y textos de plataforma: el original entero mientras falte algo del idioma del menú, y ese idioma entero al completarse. Guardar conserva los mapas y vaciados explícitos; la preview no escribe traducciones. Desde el 2026-11-03 ([NI1-A y NI2-C](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#el-idioma-sólo-cuando-es-el-otro-ni1-a-ni2-c-y-ni3-b--2026-11-03)) un texto nombra su idioma sólo cuando no es el del menú, y un formulario escrito en el idioma del menú, con la traducción sin empezar, se publica sin hablar de idiomas.

La cultura de la sesión se fija al aceptar el envío. Usa la de la cuenta destinataria si la versión la tiene publicada y están completos los textos usados del formulario, los avisos de la revisión de reglas y el diseño PDF aplicable; si no, usa el original para todo el recorrido. Formulario, CTA, apertura predeterminada, avisos y PDF comparten esa elección. La apertura editada por el operador se conserva; `message:null` permite al servidor elegir su predeterminada, que [nombra el formulario](whatsapp.md#envío-durable-de-formularios) en esa misma cultura. La captura conserva la cultura recibida de la sesión, sin subir luego al autor al secundario por completar traducciones o cambiar su cuenta.

Los catálogos mantienen sus reglas de actividad, disponibilidad y metadata. Su sección «El catálogo» tiene sólo nombre y descripción: el origen (`kind`, «De dónde salen») y el tipo de valor (`valueType`) se eligen al crearlo, ningún PUT los recibe y el editor los muestra como un dato de solo lectura, sin caja ni explicación (decisión del 2026-10-23). La descripción es opcional en los dos idiomas, en Manual, Filters y al editar o aprobar una propuesta H4: se guarda sin entradas en blanco, el detalle la devuelve como `{}` y el listado como `null`; sólo la propuesta que arma el asistente la trae siempre.

Cada catálogo y cada dato de las personas guarda su idioma original, fijado al crearlo con el idioma del menú (H4, con el de la conversación) y sin cambios después; los campos y las opciones usan el del catálogo. El detalle del catálogo (Manual, Filters, propuesta e importación) y cada dato de las personas lo devuelven en `originalCulture`. Ese es el único idioma obligatorio, sin excepción para el alta común: al crear y al editar (Manual, Filters, propuesta H4 y datos de las personas) se exige en él el nombre, y en un catálogo también el de cada campo y el texto de cada opción; el otro idioma es una traducción opcional, y un texto `null` o en blanco equivale a no tenerla. Al crear se exige el idioma del menú y al editar el original guardado, aunque el menú esté en el otro idioma. Lo que falta del original responde 400 en `name.<original>`, `items[i].text.<original>` o `fields[i].name.<original>` con `Forms.Catalog.ContentInvalid`, y en los datos de las personas en `name.<original>` con `Forms.PersonAttribute.Invalid`. Como cada texto se edita en un solo campo, en el idioma del menú, ese error puede caer en un texto que no está a la vista: también en catálogos y datos de las personas se muestra en la página. Así, una opción de un catálogo con original inglés puede no tener texto en español.

El texto y el detalle de una opción se muestran en el idioma pedido y, si falta, en el otro, también en la búsqueda, el orden y los textos que leen en SQL los ítems del catálogo y las opciones de los datos de las personas. «Ordenar por…» Texto usa el texto original con las reglas de su idioma (es-AR o en-US). Al importar y exportar, «Texto» y «Detalle» son el original y el otro idioma tiene su columna de traducción opcional: con original español, «Texto en inglés» y «Detalle en inglés» (`textEn` y `detailEn`, como antes); con original inglés, «Texto en español» y «Detalle en español» (`textEs` y `detailEs`, que también nombran la columna en los problemas y cambios de la vista previa). Una columna de traducción en el idioma del original es una columna desconocida, y los textos repetidos se avisan por el texto original.

La oferta que viaja a WhatsApp, el texto de la opción fuente sellado al capturar y `fieldTexts` sólo traen los idiomas con texto. La excepción al respaldo de toda la presentación es el catálogo, que tiene su propio original: el nombre del catálogo y el texto y el detalle de cada ítem sin traducción se muestran en el original del catálogo sin cambiar por eso la cultura del resto, también en la cultura secundaria del formulario (el nombre del catálogo no viaja a WhatsApp). Ese respaldo no se guarda como una traducción ni modifica las referencias históricas, y una referencia capturada sin traducción no deja incompleto ningún idioma para avisos y PDF: se muestra en el original de su catálogo. Los nombres que se muestran del catálogo, de la fuente de un dato de las personas y del formulario que lo usa salen en el idioma del menú y, si falta, en el otro.

No se recompilan ni reescriben perfiles, proyecciones, versiones, sesiones o copias PDF ya selladas. La comprobación física ES solo → EN completo → faltante exclusivo de aviso o PDF (P24) sigue [pendiente](../plans/verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional); esta regla no acredita un piloto nuevo.

## Listado, búsqueda y facetas coherentes

Respuesta name es mapa completo. El SPA muestra la cultura UI; si falta su nombre, usa original como referencia de autoría y puede indicar traducción pendiente. Búsqueda, orden name y facetas usan **esa misma proyección en SQL**, según cultura UI resuelta por Accept-Language o DefaultCulture. No orden/paginación en memoria, N+1 ni búsqueda en otro texto oculto. Prefix sigue searchable; ApplySort desempata por Id y ToPagedResultAsync pagina. Nombre original no es identidad ni permiso; originales en/es funcionan simétricamente.

Esta referencia de autoría del listado/preview no permite fallback de texto en Flow publicado. La cultura futura del empleado procede de cuenta vinculada activa, no UI del operador; el envío está implementado con sesión sellada y revalidación del vínculo.

## Un guardado y recuperación exacta

Create/Update validan permisos/entrada y abren una UoW OnSuccess. Dentro: lock actor/UUID, intento previo, lock global de definiciones/prefijos, revisión/identidades/prefijo, definición/ledger y resultado protegido. Helpers no guardan; retención tiene caso de uso técnico propio. Medir contención antes de dividir lock global.

Mismo actor/UUID/cuerpo canónico devuelve snapshot/mapa de su commit con201/200, sin avanzar revisión. Otro kind/definición/cuerpo da RequestConflict. Huella cubre todos los nombres/textos por cultura, vaciados explícitos, IDs/localIds/orden/revisión y defaults; mapas ordenados por clave. Recuperación devuelve el resultado original aunque currentRevision sea mayor; committedAtUtc viene de auditoría confirmada. Exacto se refiere al contenido del snapshot, no bytes HTTP/escape Unicode. 404 en vuelo no demuestra rollback; conservar UUID/cuerpo.

DataProtection usa ArquitecturaBase.BusinessForms.DraftChanges.v1, ligado a fila/actor/UUID/definición/kind; codec cerrado format=1 ahora contiene el contrato localizado directamente. Sin v1/v2 artificial ni reescritura de datos reales. SHA256 normalizado vive sólo en payload protegido; ningún hash privado/cuerpo/resultado en columna/log en claro. Restore incluye anillo protegido y certificados de la plantilla, sin credenciales IA/Meta.

Resultado recuperable30d; luego GET/replay410 antes de abrir payload. Purga seguida100 al guardar y cada15min; tombstone conserva actor/UUID/kind/definición/revisiones/expiración/auditoría contra reutilización. Sin ExecuteUpdate/Delete ni retención nueva por D5. Los tombstones conservan su metadata sin plazo: no hay purga de UUID y retirarla requiere un protocolo propio.

## HTTP, permisos y composición

Las seis rutas MVC de autoría bajo /api/forms son: GET definitions/facets/detail, POST definitions, PUT definitions/{id}/draft y GET draft-changes/{requestId}. Controllers sólo IFormDraftService; contratos propios mapeados a mano, cerrados y redactados. name es mapa también en fila/list/detail/result; no propiedad nameByCulture. ProblemDetails traducido code/traceId/errors; paths name.es/name.en; 409 conflictos y410 expiración.

Read siempre; Manage adicional al escribir. Recuperación Read + actor propio, sin Manage. Catálogo opcional aporta publish y formrecords.create/read para recorridos, sin nuevos grants/ACL por formulario. Seed Admin/composición/Application/Infrastructure/Api/Program existentes. No versionado ni endpoints nuevos por D5.

## Chequeo local de publicación — incremento 2a, 2026-10-10

POST definitions/{id}/publication-checks: MVC → IBusinessFormPublicationService → lector → IStructuredFormChannel. Read+Publish, sin Manage. Mismos512KiB y forms-write30/min por actor. Nombre mapa/ExpectedRevision/Prefix/Draft guardados, LocalId correlación y FieldId/ChoiceId definitivos. Normaliza y compara todo el canónico, incluidos mapas de nombre; revisión obsoleta409 RevisionConflict, contenido/IDs nuevos/ajenos/reemplazos409 DraftMustBeSaved. No IDs nuevos, escritura, lock/UoW ni Meta.

FormPublicationPolicy exige el original completo en name, submitLabel, labels, opciones y ayudas presentes. `cultures` informa `isOriginal`, `isComplete` y `missing` por idioma; sólo los faltantes del original entran en `issues` bloqueantes. El secundario incompleto queda fuera de las culturas enviadas al canal, sin bloquear el original. Los diagnósticos usan Forms.Publication.MissingTranslation, cultura y path precisos como name.en / draft.submitLabel.en / draft.fields[i].label.en/help.en/options[j].label.en, con FieldId/ChoiceId cuando aplican. ChannelId null identifica la regla funcional previa al canal; el compilador no mezcla textos entre culturas.

Response200: definitionId/draftRevision, compatibility, canAccept, acceptanceAvailability, channels, cultures, issues y diff. Compatibility LocallyValidated/Blocked/Unavailable; ausencia de canal explícita, IDs de canal únicos/no vacíos. Sin versión activa, diff.baseVersionId=null y added son las preguntas guardadas. Con activa, se leen preguntas selladas y se comparan added/changed/removed por ID, además de addedCultures/removedCultures. En el corte 2a no existía aceptación; en 2b canAccept y acceptanceAvailability consideran configuración y operación abierta; ExternalEvidence=Pending separada, sin bloquear sólo por piloto pendiente. No JSON Flow/mapping/hash privado/Meta ID/credencial/preview en HTTP.

Puerto neutral Interfaces/Channels y DTOs Models/StructuredData; sin tipos de módulos/HTTP/EF. WhatsApp aporta phone con TryAddEnumerable incluso si transporte apagado, porque consulta pura. Sin un canal instalado, todo formulario se publica sólo en la app ([formularios sólo en la app](#formularios-sólo-en-la-app--sa1-a-a-sa5-2026-11-09)). Compilador/codec/JSON/mapping/perfil pertenecen al canal; defensa de textos por cultura exacta, sin importar BusinessForms.

Perfil flow-static-7.3-r4/JSON7.3 para publicaciones nuevas, seis tipos, complete terminal/una pantalla, sin endpoint. Sí/No sin default, false no ausencia; decimal exacto textual; DateOnly CalendarPicker single/required. Ayuda/config/textos no representables se bloquean por identidad/cultura, sin recortar/dividir. El botón de apertura es Abrir formulario/Open form y queda separado de submitLabel.

Un texto más largo que lo que muestra el componente (`WhatsApp.Flow.TextTooLong`) trae su `limit`. Desde el 2026-11-01 el diálogo del front lo dice en concreto («Hasta 20 caracteres; tiene 26.»), con el largo medido sobre el borrador guardado: el nombre sin los espacios de alrededor, como lo guarda el servidor, y los demás textos tal cual. El diálogo sigue el [tablero PU1-A](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#publicar-en-whatsapp-pu1-a--2026-11-01).

[Perfil del canal](whatsapp.md#perfil-local-de-formularios--2026-10-10). Las notas anteriores a D5/r3 se retiraron al consolidar este cierre: sus pruebas históricas no califican el perfil actual. Publicación implementada y alcance de la verificación se describen abajo.

## Aceptación durable — primer corte 2b, 2026-10-10

Check devuelve checkToken/checkExpiresAtUtc (10 minutos) sólo si la capacidad local del canal admite publicación y no hay operación abierta. Compatibilidad y evidencia externa siguen separadas. POST definitions/{id}/publications recibe RequestId/ExpectedRevision/CheckToken y el mismo Name mapa/Prefix/Draft; Read+Publish, sin Manage, 512 KiB y 30/min. Un solo OnSuccess, locks actor/UUID → global, permisos actuales y snapshot guardado. Replay por actor/UUID/payload normalizado ocurre antes de expiración/revisión/perfil; nunca crea otra versión.

El commit registra identidades en BusinessFormFields, versión inmutable SchemaJson jsonb de format/questions (ContentHash sólo del esquema) y BusinessFormPublicationOperations con manifiesto protegido de presentación/perfil/huellas. Fija PrefixLocked y conserva el borrador; no activa ni llama al proveedor. Recibo 202 sin Location; consulta GET definitions/{id}/publications/{requestId}; lectura propia Read+Publish (120/min), 404 ajena/inexistente. Historia y deduplicación se conservan mientras exista la versión; no heredan los 30 días de DraftChanges ni reutilizan su outbox.

La migración aditiva 20261010141855_AddBusinessFormPublications incluye tres tablas y ActiveVersionId, con FK compuesta para pertenencia, número único por definición y una operación abierta. Dominio ya verifica lease/fence y activación por base activa sin sobreescribir un borrador más reciente. Ese primer corte sólo demostró aceptación local con un canal de TestFeatures; el siguiente apartado describe la coordinación externa implementada.


## Publicación y preview — implementación 2b, 2026-10-10

El usuario autorizó 2b y adoptar las propuestas técnicas existentes; confirmó agregar WhatsApp:WabaId en la conexión actual y verificar pertenencia. No se requirió otra conexión ni cambiar la validación de inicio del envío de texto. El chequeo local emite capacidad según configuración conocida; no consulta la cuenta ni atribuye una aprobación Meta a un 202.

FormPublicationDispatchService reserva y completa en límites OnSuccess propios con el lock global existente, y devuelve pedidos neutrales por perfil armados desde el manifiesto/schema sellados. FormPublicationChannelDispatchService invoca Accept/GetPublication fuera del límite y transforma indisponibilidad en progreso incierto; el worker sólo coordina scopes separados de reserva, despacho y completado. El canal acepta idempotentemente por OperationId y huella y guarda su propia operación/proyecciones; nunca se llama a un escritor del canal desde un límite BusinessForms. Si la aceptación devuelve un error, BusinessForms conserva la operación como Uncertain y reintenta con los mismos IDs después de la espera, sin consumir otra versión ni cerrar la operación como fallo terminal. CompleteAsync decide estado y activación en Application. Reinicio después del commit del canal recupera el mismo ID desde otro host. El manifiesto fija las culturas completas aceptadas y sólo puede activar cuando todos sus resultados son Published verificados.

La activación exige pertenencia, fence vigente, permisos actuales y BaseActiveVersionId. No exige que siga vigente DraftRevision: un borrador posterior se conserva. El completado ya confirmado es idempotente; fences distintos fallan. Un conflicto de base activa queda Uncertain/ActivationConflict, sin sobrescribir otra versión. La revocación impide nuevos efectos; resultados y recursos conocidos se conservan para revisión. No se desbloquea Prefix ni se reemplaza la activa anterior por un fallo. CanRetry=false: no hay acción HTTP que repita efectos inciertos; recuperación técnica conserva la misma operación.

Estados de la operación: Accepted → Preparing → Publishing → Uncertain o ActivationPending → Published o Failed. ActivationPending significa que el canal ya confirmó PUBLISHED, asset y owner y falta el commit local; Failed conserva snapshot, prefijo y la versión activa anterior. Hay una sola operación abierta por definición (índice único filtrado; 409 `Forms.Publication.InProgress`).

GET de operación propia mantiene Read+Publish y agrega issues traducidos por campo/cultura provenientes del canal; HTTP no contiene JSON, hash de compilación, Meta ID, WABA, token ni mensajes brutos. Lista/facetas filtran Published por existencia de activa y Drafts por ausencia, con conteos SQL coherentes; QuestionCount sigue contando el borrador. ActiveVersionNumber procede de la versión activa. El diff compara snapshots sellados por identidades, no etiquetas.

GET definitions/{id}/versions/{versionId}/preview exige Read vigente y pertenencia de versión publicada. Cultura explícita es/en en query, independiente de la cultura UI y elegida entre las publicadas de esa versión; consulta de canal sin UoW/locks. Devuelve URL HTTPS oficial y ExpiresAtUtc exacto con Cache-Control: no-store. El canal verifica owner, perfil, bytes del asset, estado y health; no acepta URL suministrada por cliente. El DTO redacta la URL portadora. No crea sesión/token de captura. Para que la vista se recorra como en el celular (VO1-A, 2026-11-06), la consulta al canal lleva opciones de ejemplo de cada pregunta de catálogo de la versión sellada (`FormCatalogPreparationPolicy.PreviewAsync`): las primeras 20 activas, en orden y en la cultura pedida, sin filtros por persona ni por otra respuesta (los fijos siguen); una lista de personas es la que vería quien mira y un catálogo inactivo o inválido queda sin opciones. El piloto comprobó la apertura oficial y el round-trip; embeber por iframe/CSP y otras variantes físicas conservan su calificación pendiente.

[WhatsApp](whatsapp.md#publicación-durable-de-flows--2b-2026-10-10) conserva perfil, bindings, transporte y recuperación. La migración de captura agrega recibos/valores a BusinessForms y columnas de respuesta protegida a WhatsApp; no crea FK/joins entre módulos. El envío, recepción y round-trip real de Meta se comprobaron en el piloto del 2026-10-11, con el alcance del cierre.


## Recarga de publicación y cultura de preview — mínimo 2

GET definitions/{id} conserva definition/capabilities/audit y agrega publication. activeVersion contiene versionId/versionNumber/draftRevision o null; currentOperation es la última aceptación por número de versión, con versionId/versionNumber/draftRevision/state/stage/requiresReview/canRetry=false y requestId/recoveryPath nullable. Sólo el actor propio con Publish vigente recibe esos dos datos de recuperación; otros lectores autorizados ven el agregado seguro, sin manifiesto, ID externo, huellas ni UUID de otro actor. capabilities agrega canPublish (permiso actual) y canPreview (existe activa), sin sustituir el chequeo de aceptación.

publication.displayState y displayState del listado reflejan la última operación, o Draft cuando no hay ninguna. Se conserva ActiveVersionNumber y el filtro/facetas Published por ActiveVersionId, aunque la nueva operación esté pendiente o Failed. El badge no define el filtro ni borra la activa anterior. Las consultas son SQL mediante el lector existente; sin N+1, caché ni límites de escritura para una lectura.

GET de preview exige ?culture=es o ?culture=en; ausente u otra cultura responde400 Forms.Preview.CultureInvalid con errors.culture, siempre no-store. No modifica CurrentUICulture ni el idioma de una cuenta. El canal WhatsApp rechaza un vencimiento explícito con WhatsApp.Flow.PreviewUnavailable sin URL; el guard neutral conserva Forms.Preview.Expired si otro canal devuelve un resultado ya vencido. Pedir nuevamente la preview es una consulta explícita, nunca una renovación automática ni otro intento de publicación. URL y ExpiresAtUtc siguen procediendo del proveedor.

### Presentación sellada de versión — base del mínimo 3, 2026-10-10

BusinessFormVersion conserva NameJson, SubmitLabelJson, Culture y QuestionCount inmutables, separados del SchemaJson de preguntas y su ContentHash. La aceptación los sella del mismo snapshot validado; editar el borrador no modifica esa presentación. DisplayNameEs/En son columnas SQL calculadas desde el mapa de la versión, sin fallback al borrador actual. La migración AddFormVersionPresentationAndContactLinkRevision agrega las columnas, sin reescribir migraciones ya aplicadas.

Para versiones anteriores, FormVersionPresentationSeeder participa del límite técnico existente de DatabaseSeeder y su lock de seed. Sólo restaura la presentación ausente desde el manifiesto protegido de esa misma operación/versión y cuenta las preguntas del esquema sellado; verifica pertenencia/hash y falla ante inconsistencias. No abre otro límite, guarda por su cuenta, llama a Meta ni modifica SchemaJson, ContentHash, identidades, snapshot, operación o bindings. Repetir el seed conserva la presentación ya sellada. El núcleo conoce únicamente IDatabaseSeedParticipant de Infrastructure, sin nombrar el módulo. La auditoría la completan los interceptores. El selector, el envío durable y la captura de respuestas están implementados.

El consumidor de selección real usa IStructuredFormSource neutral: núcleo TryAdd apagado devuelve página vacía/cancelable y BusinessForms Replace scoped lo sustituye de manera idempotente en ambos órdenes de registro. El adaptador verifica formrecords.create del destinatario y el lector usa sólo versión activa con operación Published y presentación sellada completa; nunca nombre del borrador ni fallback por idioma. La composición del núcleo no referencia módulos. `IStructuredRecordSink` y `IStructuredRecordSource` también son neutrales; el sink revalida `formrecords.create`, valida contra la versión aceptada y escribe registro, valores tipados y recibo idempotente en una sola UoW. El source vuelve a comprobar `formrecords.read` después de leer; si se revoca durante la consulta, descarta las respuestas.

## Recepción y captura de respuestas — mínimo 3, 2026-10-11

El webhook firmado reconoce `interactive.nfm_reply`, valida el tamaño/forma de `response_json` y conserva sólo campos escalares acotados. La respuesta completa queda protegida con Data Protection ligada al mensaje/contacto; nunca se guarda en el cuerpo legible del chat ni se procesa como texto de negocio. Un worker independiente la retoma aunque el procesamiento de entrantes de texto esté apagado. `flow_token` se transforma a hash para buscar sesión; contacto, cuenta activa, expiración, versión, binding, cultura, proyección protegida y `LinkRevision` deben coincidir.

El mapping sellado del canal, no el JSON de Meta, determina `field_{Guid:N}`, tipo, obligatoriedad, longitud y opciones. El codec versionado transforma los seis tipos a su forma canónica antes de llamar al puerto neutral. Una captura nueva exige vínculo/cuenta vigentes, fecha firmada anterior al vencimiento y permiso vigente. Una respuesta desconocida, malformada o con mapping inválido no llega al sink; el texto con NUL o UTF-16 incompleto se rechaza antes de persistir y no consume numeración. `BusinessFormRecordRecorder` vuelve a autorizar `formrecords.create` antes y dentro de su límite; graba recibo, registro y valores tipados juntos. Un recibo confirmado se consulta antes de exigir permisos de creación actuales: el mismo contenido recupera exclusivamente su número/estado, incluso tras desvincular, revocar o vencer. La recuperación verifica sesión, actor, definición, versión y huella de respuestas; no crea un registro nuevo ni revela valores. Repetir la sesión y el mismo contenido devuelve el mismo registro/número; reutilizarla con contenido cambiado se rechaza sin crear otro registro ni consumir numeración.

`WhatsAppChatQueryService` lee los valores por `IStructuredRecordSource`, nunca consulta EF ni nombra BusinessForms. `formrecords.read` gobierna los valores: al perderlo, el historial puede conservar estado y número completado, pero no revela respuestas. Filas pendientes y no reconocidas no revelan mapping ni valores. La migración `AddBusinessFormRecordsAndFlowCapture`; la prueba con PostgreSQL/Redis reales cubre captura, replay/conflicto, tipos, número y FK de FieldId; otras pruebas recorren webhook y permisos con proveedores neutrales. Estas pruebas validan el sistema local; el piloto físico separado se resume en la verificación de cierre.

## Envío durable de formularios — incremento 3

WhatsApp lista sólo versiones publicadas elegibles para la cuenta explícitamente vinculada al contacto. Encolar revalida vínculo, estado, idioma, permiso del destinatario y revisión del contexto; el operador conserva permisos separados. La selección usa la versión activa, mientras una sesión ya aceptada mantiene su versión y presentación aunque luego se publique otra.

La sesión fija contacto, cuenta, operador, definición, versión, idioma, binding, expiración y huella del destinatario. Token aleatorio y payload sensible se protegen con Data Protection; la correlación usa hash fijo en comparación de tiempo constante. El mensaje Flow entra en la cola común de WhatsApp en el mismo commit que la sesión. El worker verifica WABA/PhoneNumberId y vuelve a autorizar antes de enviar; nunca publica desde el chat ni reenvía un despacho incierto automáticamente. El POST real a Meta y la devolución por webhook se comprobaron en el piloto del 2026-10-11; no persiste el bloqueo 141006 observado el día anterior.

## Verificación de cierre — 2026-10-11

Implementados los pasos 1–4: editor de seis tipos con textos es/en, guardar incompleto y publicar ambos idiomas completos; publicación/activación y preview oficial; selección y envío desde el chat con sesión y cola; captura tipada idempotente y lectura del historial con `formrecords.read` adicional. Cada entidad conserva las reglas de auditoría de la plantilla; no se agregó caché de formularios ni conexión Meta por usuario.

Piloto real: versión 2 de PRB1010, perfil flow-static-7.3-r3; ambos Flows publicados y disponibles, número disponible tras corregir el pago. El envío hecho desde el chat quedó Entregado a las 15:24 (Argentina); el usuario confirmó desde su celular a las 15:25. La respuesta creó PRB1010-000001 con los seis tipos y se conservó al recargar. No apareció 141006. Cantidad fue 100: la prueba física no califica decimales, ausencia opcional, sesión de versión anterior ni máximos de texto. Preview oficial aceptó 12.50 y rechazó 12,50. Por decisión humana, la ayuda de las compilaciones futuras indica punto; el receptor sigue aceptando ambos separadores y los assets de versiones ya selladas se conservan.

Backend final c4c08b1: build sin advertencias y suite completa con Docker 2685/2685, cero omitidos. ed1e50c prueba con PostgreSQL que un Flow r3 publicado sigue enviable/sin revisión, conserva bytes/hash y captura su sesión abierta después de publicar r4; c4c08b1 aísla el recibo propio ante pendientes de otros tests, manteniendo una captura única. Las correcciones del cierre incluyen duración, presupuesto, aislamiento de errores, retención, ownership, privacidad y recuperación idempotente del comprobante. La ayuda decimal nueva corresponde a r4 y sólo se usa en publicaciones futuras.

Front: suite general 1455/1455 (ejecución serial), lint 48/48 y build sin advertencias. Corrida final de npm run test:visual sobre 856e641: 292/294, exit 1, tres workers, cero omitidos/reintentos/flaky y sin actualizar snapshots. Fallaron las dos comprobaciones iniciales de Nombre en la prueba de idioma de contenido, español a 390/320 px; su causa no está probada. Los casos preparados de publicación, recuperación, sesión y WhatsApp pasaron; 32 capturas revisadas por imagen/hash. El arnés mantiene sus assertions originales y controles negativos. Esta deuda de validación y la intermitencia de Roles pasan al seguimiento del arnés en Etapa 2; no se presenta esta corrida como verde. Los datos de prueba de presupuesto usan respuestas HTTP es/en exportadas byte a byte del backend 48e05da. Recorrido móvil contra la API final: cuatro casos en español a 390/320 px, con/sin permiso y recarga, ocho capturas; los bundles productivos coinciden con el build final. API es/en verificada aparte. Ese arnés usa PostgreSQL/Redis reales, identidad y Meta simulados y puertos neutrales de formulario/registro: no demuestra por sí solo persistencia BusinessForms, cubierta por sus pruebas SQL y el piloto real.

Limpieza: 28 XML de diagnóstico no versionados retirados de la raíz. Las notas de revisión del 9–11 de octubre y sus fixtures históricos se consolidan aquí y en las fuentes funcionales; copia local recuperable con hashes en `.test-artifacts/forms-step5/provenance/reviews-before-cleanup.zip`, fuera de Git. No se atribuye a Git el contenido nunca commiteado. Procesos y túnel del piloto apagados, credenciales temporales del móvil eliminadas; PostgreSQL y el registro real se conservan.

Etapa 1 cerrada por instrucción del usuario del 2026-10-11, con implementación y piloto real completos, sin hallazgos altos. Los tres pendientes menores —presentación R3, prueba física de decimales y test intermitente de Roles— pasan a Etapa 2. La corrida visual roja y su diagnóstico pendiente quedan expresos como deuda del arnés; no se afirman pruebas aprobadas que no pasaron. No se escriben nuevos contratos o revisiones Markdown. Etapa 2: catálogos Manual/Filters, Usuarios como primer origen de base, traducción inglesa opcional de ítems y nuevas identidades al cambiar catálogo. El ADR 0019 está aceptado: no se admite SQL configurable por ahora; Query/K5 queda después, sólo si una lista no puede expresarse con Filters, y K2 no muestra su opción. Planilla, reglas/aprobación/PDF y asistente conservan las etapas 3–5.

El corte Manual K1–K3 implementa listado, alta y edición HTTP con revisión, ítems y paginado común. El cliente no envía `Position`: al crear, el servidor numera en el orden del request; al editar, agrega nuevas opciones al final. El PUT admite una lista ordenada `moves` de `{ itemId, direction: "up" | "down" }`, aplicada completa dentro del mismo lock/UoW; resuelve el orden global por Position+Id, incluso entre páginas, y normaliza a posiciones consecutivas. PostgreSQL impone unicidad con una clave diferida para admitir el swap transaccional. La clave se implementa en una migración SQL porque EF interpreta las claves alternas como identificadoras e impide modificar la posición; el snapshot no las representa para conservar actualizaciones auditadas por la UoW. La migración no renumera filas existentes. K6 habilita preguntas de catálogo y K7 prepara opciones al enviar y registra referencias al responder; K5 continúa diferida según ADR 0019.

K4 Filters obtiene Usuarios y Roles con un SELECT fijo del lector del módulo sobre `AspNetUsers`, `AspNetUserRoles` y `AspNetRoles`, usando la conexión normal. No crea una vista persistente sobre tablas de Identity. Devuelve una fila por persona con nombre, roles y actividad; excluye cuentas borradas y nombres vacíos. Los filtros por rol guardan el RoleId y muestran el nombre actual, e incluyen roles aunque todavía no tengan miembros. El catálogo conserva su gramática de condiciones y enlaza sus valores como parámetros. La vista previa requiere elegir explícitamente una persona activa de ejemplo y no guarda datos. El servidor pagina y busca esa muestra, y calcula los conteos de origen sobre el conjunto filtrado completo antes del filtro de tipo y la paginación del listado.

La metadata de `filter-options` no comunica disponibilidad: el SELECT fijo lee las tablas vigentes de Identity. El estado genérico `Unavailable` del catálogo queda reservado para una definición guardada inválida después de una migración; que un rol se haya dado de baja no vuelve inválido el catálogo ni cambia el RoleId persistido. El front conecta `CatalogFiltersEditor` y «Como si lo recibiera» con las rutas del backend, retiene el foco mientras busca muestras y consume los cuerpos HTTP exportados sin adaptadores de compatibilidad.

K6 permite que una pregunta `choice` use opciones manuales o una única referencia `source.catalog`. Cambiar el origen de una identidad guardada requiere reemplazarla. El chequeo de publicación bloquea referencias inexistentes o inactivas, en `draft.fields[i].source.catalog`. La lista, el detalle y la respuesta de editar un catálogo Manual/Filters muestran `UsageCount`: cantidad de definiciones distintas no eliminadas cuyo borrador actual referencia el catálogo; las dadas de baja siguen contando. El valor se oculta como `null` sin permiso de lectura de formularios. El Flow compilado declara para esas preguntas un Dropdown dinámico y el esquema de `screen.data`; la vista previa web conserva la pregunta sin opciones. La versión publicada guarda solo el catálogo: cambiar sus ítems no obliga a republicar; K7 prepara las opciones por destinatario y captura sus referencias.

Verificación K4 2026-10-12: backend build sin advertencias y focal de API/PostgreSQL/Redis 122/122. La única suite general terminó 2708/2710; fallaron dos assertions de tests (conteo OpenAPI y selección de muestra dependiente del orden compartido), corregidas en `8d9f292` y verificadas focalmente sin otra completa. Front: suite final 1577/1577, lint/arquitectura 48/48, build sin diagnósticos y consumidores de contratos 47/47. La visual completa quedó 303/312: seis fallos del selector de muestra se corrigieron con TDD; tres del asistente no tienen causa acreditada. Los nueve casos afectados pasaron 9/9 focales y se revisaron 42/42 capturas. La completa conserva su resultado rojo. El navegador usó transporte simulado; API/PostgreSQL/Redis se verificaron por separado, sin atribuir un recorrido SPA/API/BD real a K4. Los manifiestos automáticos acompañan ocho cuerpos Filters y quince Manual-current. No se usó Meta.

Verificación Manual 2026-10-12: build de `Api.IntegrationTests` sin advertencias; en la full con Docker, 2705/2706 pasaron y el único fallo fue el arnés de preview intentando iniciar sesión antes de escuchar. Se corrigió el orden y el test focal pasó 1/1. La corrida final del front completó 9/9 comprobaciones con API, PostgreSQL y Redis reales, en 1440/390/320 px: alta y recarga, edición, reactivación, movimientos y altas en una escritura, conflicto 409 con adopción explícita y permisos Read/Manage separados. No quedaron tokens en storage. El recorrido guardó ocho capturas; esta validación no cubre Meta.

Front Manual 2026-10-12: build y lint limpios; la suite completa pasó 1542/1542 con `maxWorkers: 1`, sin cambiar assertions ni el límite original. La visual completa terminó 300/306. Tres fallos del asistente coincidieron con la edición de `vite.config.ts` y errores de conexión; los otros tres de diseño no tienen causa acreditada. Los seis pasaron 6/6 focales con configuración estable. Manual pasó seis casos con treinta capturas revisadas, sin actualizar snapshots; el recorrido SPA/API/PostgreSQL/Redis real pasó 9/9 con ocho capturas. La completa conserva su resultado rojo junto a la evidencia focal.

## Catálogos al enviar y responder — K7, 2026-10-12

`IStructuredFormSource.PrepareAsync` resuelve Manual y Filters para la cuenta vinculada, con la cultura efectiva del [recorrido completo](#idioma-original-y-traducción-opcional--2026-10-19). El texto y el detalle de un ítem sin traducción, y el nombre del catálogo sin traducción, usan el original del catálogo sin cambiar la cultura del formulario. La versión guarda sólo `source.catalog`; la preparación no cambia su contenido. Al aceptar el envío se comprueban otra vez vínculo, permisos y versión, y se fija la oferta dentro del payload protegido de la sesión, en el mismo límite que la salida durable. El despacho usa esa oferta en `flow_action_payload.data`, sin consultar nuevamente el catálogo ni agregar un endpoint de intercambio. El tope es 200 opciones para elegir una y 20 para elegir varias; una pregunta obligatoria vacía o una opción incompatible con los límites del Flow bloquean el envío sin truncar.

La captura admite únicamente IDs ofrecidos. Manual comprueba la actividad bajo el mismo lock que su edición; una baja rechaza toda la respuesta antes de asignar número. Filters conserva la oferta sin volver a consultar Usuarios. `BusinessFormRecordReferences` guarda catálogo, revisión, ID, texto, detalle y nombre históricos junto al registro, valores y recibo, en una sola transacción. Una falla al insertar la referencia revierte también la numeración. El replay conserva el resultado y la pregunta del rechazo, incluso al recuperar dentro del límite.

El chat devuelve referencias históricas sólo con `formrecords.read`. El rechazo nombra la pregunta, nunca el valor elegido, y no presenta registro ni número. «Mandar de nuevo» abre el compositor existente; el operador elige y confirma otro envío con opciones actuales. No hay reenvío automático, conexión adicional, caché nuevo ni tabla de auditoría.

Verificación local: build sin advertencias y suite completa con PostgreSQL/Redis **2735/2735**, sin omitidos. Incluye oferta por destinatario, idiomas, pertenencia, baja, histórico tras renombrar, rollback y replay. El productor HTTP exporta cuerpos es/en y su procedencia automática; su puerto neutral se acredita por separado de la persistencia BusinessForms. La prueba física de estos catálogos con Meta queda para realizarla con el usuario; el piloto de Etapa 1 no acredita K7.

Cierre del front K7: suite unitaria **1614/1614**, consumidor HTTP **7/7**, build y lint/arquitectura **48/48**, visual completa **398/398** con referencias intactas. El navegador usa rutas y componentes reales con datos/transporte ficticios; la persistencia y el webhook se verifican en las pruebas del backend. Meta queda pendiente de la prueba conjunta, y la Etapa 2 conserva sus pendientes en el resumen.

## Correcciones de cierre de catálogos — 2026-10-13

Los PUT de Manual y Filters admiten `isActive` junto con `expectedRevision`: estado, metadatos e ítems comparten una revisión y una transacción. Omitir el estado lo conserva. La baja no borra las opciones ni las referencias históricas; su configuración sigue editable y se puede reactivar. En la pantalla, la confirmación prepara el cambio y Guardar lo aplica. Dar de baja Filters no exige persona de ejemplo ni vista previa; reactivar vuelve a probar la configuración.

El editor conserva el borrador ante red o 5xx y permite otro intento explícito con la misma revisión; sólo un 409 exige revisión. La búsqueda de personas espera 300 ms y conserva la metadata anterior durante carga y error, delimitada por actor, idioma y permisos. No reemplaza el editor ni pierde el foco.

Verificación final: backend **2746/2746** con PostgreSQL/Redis y build sin advertencias; front **1621/1621**, lint/arquitectura **48/48**, build limpio y visual completa **416/416**, sin cambiar referencias ni timeouts. Capturas representativas revisadas en es/en, escritorio y móvil. API/persistencia y navegador con transporte simulado se acreditan por separado; el piloto real de catálogo con Meta sigue pendiente de realizarse con el usuario.

## Preparación de catálogos: cierre del review K6–K7 — 2026-10-15

El operador recibe sólo campo, etiqueta y cantidad, junto con problemas seguros; las opciones completas se resuelven para el destinatario y quedan en la sesión. Manual valida texto de 30 y detalle de 300 caracteres por idioma al guardar; K4 conserva cada texto completo y marca si no entra. El bloqueo al enviar informa catálogo y cantidad incompatible, sin valores ni truncamiento. En Filters, el texto es siempre nombre; rol y actividad siguen disponibles para detalle, condiciones y orden.

Una pregunta de catálogo sin selección envía `null` y recibe validación en su campo. La preparación tiene dueño local, recuperación explícita y 409 como bloqueo; el reenvío depende de la misma guarda del compositor. La baja advierte que tampoco se pueden mandar los formularios publicados que usan el catálogo. Red/5xx de configuración conserva el borrador y permite volver a guardar; sólo el 409 exige revisión. El mensaje Flow omite `description` si no hay detalle. Las fallas técnicas de captura exigen cinco intentos y 15 minutos desde la primera falla persistida, sin depender de la edad del mensaje; cancelaciones y rechazos definitivos mantienen su tratamiento.

Validación final: backend **2772/2772** con PostgreSQL/Redis, sin omitidos y build sin advertencias; front **1643/1643**, lint/arquitectura **48/48**, build limpio y visual completa **422/422**. Se revisaron capturas en es/en, escritorio y celular; los consumidores usan cuerpos HTTP exportados de esa corrida. El transporte de esas pruebas automáticas está simulado. El piloto real del 2026-10-15 confirmó Manual con/sin detalle, Usuarios vacío/con opciones, referencias históricas y recarga; rechazo por baja sin registro ni número y reenvío manual sin salto (`PILK7-000001` a `000003`); y envío/respuesta de 200 opciones con detalle completo (`PIL200-000001`). El aviso y el reenvío se verificaron tras recargar. Se apagó Aspire y el túnel. No se inició 2B ni se agregaron topes de bytes sin una medición de Meta; el resumen de etapa conserva los pendientes heredados.

## Fotos y documentos — implementación local 2B, 2026-10-15

Foto y Documento conservan `files` (mínimo, máximo y origen de foto); mínimo mayor a cero determina obligatoriedad. Guardar conserva traducciones pendientes y admite presupuestos mayores a diez; publicar exige el original completo, almacenamiento disponible y suma de máximos hasta diez, y envía sólo las culturas completas al canal. `BusinessForms:MaxFileSizeMiB` (1–25, predeterminado 25) se sella en el manifiesto, fuera del esquema de preguntas. `form-files` habilita estos tipos en el editor. El perfil vigente del canal y sus pantallas están en [WhatsApp](whatsapp.md#fotos-y-documentos-en-flows--2b-2026-10-15); cambiar el perfil no invalida versiones ni sesiones anteriores.

`StoredFiles` pertenece al núcleo: Entity/IAuditable, dueño, nombre, tipo, tamaño, SHA-256 y claves privadas de original/miniatura. `BusinessFormRecordFiles` pertenece a BusinessForms y conserva registro, pregunta, archivo y orden. Migraciones `AddPrivateFormFiles`, `AddFlowReplyFilesProgress` y `AddFlowReplyFileCount`; sin FK entre módulos. `BusinessFormFileRecorder` verifica contenido y sube fuera de la UoW con claves determinísticas; después registro, valores, archivos, referencias y recibo se confirman juntos. Un rechazo no guarda una respuesta parcial ni consume número. Reintentar sobrescribe el mismo blob; no se agrega staging ni otro protocolo durable.

La verificación combina SHA-256, tamaño, MIME y contenido: imágenes JPEG/PNG reales, PDF, Word/Excel CFB y OOXML. SkiaSharp genera miniaturas orientadas sin alterar originales; OpenMcdf verifica streams de documentos antiguos; ZIP/XML usa la BCL sin ejecutar ni renderizar documentos. Los nombres de descarga se normalizan, quitan caracteres de formato Unicode y usan la extensión del MIME verificado, sin truncar. Antes de decodificar una foto, el ancho/alto original no puede superar 16.384 píxeles ni el área 32 Mi píxeles; imágenes como documento se inspeccionan sin rasterizar ni generar miniatura. Documentos dañados se rechazan como tipo inválido. El total no excede 100 MiB. Las lecturas históricas entregan sólo ID, nombre, tipo, tamaño y disponibilidad de miniatura, por lote y bajo RecordsRead; nunca claves ni URL del proveedor.

GET `/api/forms/records/{recordId}/files/{fileId}/thumbnail` y `/content?download=true` usan `formrecords.read`, pertenencia y comprobación de permiso antes y después de abrir el stream. Responden no-store/nosniff; fotos JPEG/PNG en línea, documentos siempre como adjunto. Si falta el blob, devuelve 404 aunque exista su metadata; otras fallas de almacenamiento siguen siendo errores de infraestructura. Los archivos tienen un cupo propio de 600 lecturas/minuto por actor, separado de los listados. No permiso nuevo ni acceso directo del navegador a Azure. El chat conserva miniaturas, visor y descarga sólo con lectura vigente; perderla cierra el visor y retira nombres y valores.

El worker neutral limpia cada hora blobs de más de un día sin metadata confirmada; consulta lotes de 128 claves por el índice de original y deriva la clave determinística de miniatura, sin una consulta por objeto. Los archivos referenciados duran mientras exista la respuesta; no se borran por la retención del chat. La API no crea contenedores; Aspire provisiona Azurite privado local. Credenciales y configuración real: [guía Azure](../guides/azure-blob-en-local.md). Antivirus pendiente antes de producción.

Verificación local final: backend **2857/2857**, sin omitidos, con PostgreSQL/Redis/Azurite y build sin advertencias; front **1681/1681**, arquitectura **48/48**, build/lint limpios y visual **434/434**. HTTP exportado y guardas del front comprobados. El borrador se creó, guardó, recargó y editó con la API en ambos idiomas; los estados corregidos se verificaron con Playwright y transporte simulado. Aspire apagado. El piloto físico con Meta y Azure real se verificó después de esa corrida, con el alcance del cierre siguiente.

## Cierre de la Etapa 2B — 2026-10-15

El recorrido conjunto real publicó y activó la versión 2 bilingüe del piloto, mandada desde el chat al celular vinculado. Cámara + PDF y foto opcional vacía se guardaron como **FOTO2B-000001**, con AzureCli y el contenedor privado `files-content` de `starqbaseezedev`. Meta omite la propiedad de la pregunta opcional. El chat mostró primero la recepción de dos archivos y después la respuesta guardada dentro del minuto de observación, sin Actualizar ni recarga; la recarga posterior conservó el número, la miniatura y «Sin archivos».

Se comprobaron visor y descargas por la API: JPEG de 88.229 bytes y PDF de 41.412 bytes con tamaño y SHA-256 iguales a los registros confirmados, más miniatura JPEG presente en Azure. El rechazo anterior por ID numérico de medio no guardó datos ni consumió número; se corrigió con test y se hizo un reenvío manual. El generador también corrigió los IDs de pantalla rechazados por Meta; sólo publicaciones futuras usan r7. Las proyecciones publicadas anteriores no se recompilan.

Se restauró Azurite predeterminado y se apagaron Aspire y el túnel. Los objetos del piloto permanecen en Azure; para abrirlos de nuevo debe activarse ese mismo almacenamiento, porque un cambio de configuración no copia blobs. Sigue pendiente elegir el antivirus antes de producción. Planilla/Excel, PDF y audio mantienen sus etapas y los demás consumidores del almacenamiento su propio plan.

**Etapa 2B cerrada.** Validación final del piloto: backend **2865/2865**, sin omitidos, con PostgreSQL/Redis/Azurite; front **1681/1681**, arquitectura **48/48**, build/lint limpios y recorrido real del navegador. Se conserva la corrida visual **434/434** de implementación: este cierre no cambió UI. El plan se retiró con procedencia en [history](../history/README.md#formularios-etapa-2b--2026-10-15).

## Planilla, detalle y Excel — Etapa 3

La entrada «Formularios» de Gestión de formularios reúne los publicados en la [tabla MF1](#gestión-de-formularios-y-detalle-completo--2026-10-21), con paginado y orden SQL. La planilla `/formularios/{id}/respuestas` muestra número, recibida y persona/canal, más la unión de preguntas por FieldId de las versiones publicadas o con respuestas. Va primero el orden de la versión activa y después las preguntas retiradas; cambiar tipo conserva identidades distintas. Los encabezados indican desde qué versión existe una pregunta. «—» significa que no existía; «Sin respuesta», que se dejó vacía. Cero y false se conservan. El detalle usa preguntas y opciones de su propia versión, conserva referencias capturadas y muestra origen e historial de envío, respuesta y guardado. Anterior/siguiente usa el filtro y orden actuales; el enlace al chat requiere además whatsapp.read y no manda mensajes al abrir.

Los textos históricos usan una cultura de contenido por versión: la cultura de la pantalla si figura entre las publicadas en su manifiesto sellado, y el original de esa versión en caso contrario. Nombre, etiquetas, opciones y referencias comparten esa elección; los textos del sistema siguen en el idioma de la interfaz. El texto/detalle de cada ítem de catálogo conserva su excepción individual. La metadata se lee por lote y queda fuera de los snapshots de respuestas, acciones y PDF; no se reconstruye la publicación desde mapas parciales. En el chat, una respuesta autorizada usa el nombre y los valores de la misma proyección; mientras no hay valores autorizados se conserva el nombre de la sesión enviada.
`formrecords.read` autoriza planilla, valores, archivos y conteos sin requerir lectura de diseños o catálogos; `formrecords.create` solo, lo propio ([Etapa 2 de la carga](#lo-propio-de-quien-sólo-carga--etapa-2-2026-11-04)). Todas las lecturas revalidan el permiso antes de responder. Los filtros son AND de condiciones por FieldId: texto contains/equals, número/fecha equals/gte/lte, elección/sí-no in y answered/unanswered/notAsked. En filtros y conteos, unanswered incluye tanto una pregunta opcional vacía como una que no existía en esa versión; notAsked distingue esta última y answered exige un valor o archivo. La celda ausente conserva «—». Suman persona, versión y rango de recepción UTC con final exclusivo. Búsqueda sólo en textos largos. SQL filtra y ordena antes de contar/paginar, limitado a las versiones del snapshot de columnas para no mezclar una publicación concurrente; el orden de una elección manual usa un CASE por versión y ChoiceId con etiquetas parametrizadas en su cultura de contenido, y el de catálogo usa el texto de la referencia histórica localizado en la cultura de esa versión, con respaldo al otro idioma (el original del ítem, que puede ser el inglés) y luego al texto ofrecido legado, igual que el detalle de la respuesta; cada faceta ignora sólo su dimensión y conserva opciones históricas con cero coincidencias, con la última etiqueta de cada identidad y las versiones en orden numérico. Listado y Excel hidratan primero filas livianas y reutilizan las versiones ya leídas en la definición; el canal y el detalle sin ese contexto cargan versiones distintas una sola vez por llamada. No viajan NameJson/SchemaJson desde SQL por registro. Los esquemas se deserializan una vez por versión y se comparten entre validación, filtros y proyección; las facetas de catálogo usan la misma localización histórica que las filas y conservan la última referencia de cada identidad. Texto, personas y condiciones van por POST en el cuerpo, nunca en URL, storage ni logs. Página, tamaño, orden, versiones y columnas pueden vivir en URL; los filtros privados sólo duran en memoria entre planilla y detalle.

Un fallo del conteo no bloquea Aplicar si el filtro local es válido. Reintentar el conteo se ofrece para red, 5xx y 429; la espera del 429 permanece aunque se cierre el aviso o cambie el filtro, y vencer no dispara una consulta sin clic. Un 403 retira la lectura privada.

Las fotos muestran primera miniatura/cantidad y abren F4 con todas las fotos de la respuesta. El detalle ofrece galería completa y documentos con nombre, tamaño y descarga por la API. La planilla conserva desplazamiento horizontal dentro de la tabla y selector de columnas. No se exponen URL de Azure ni se cambia la captura de Etapas 1–2B.

`POST /api/forms/definitions/{id}/records/export` exige además `formrecords.export`. Su política permite una exportación concurrente por usuario y proceso, sin cola y con 429 al intentar otra, Retry-After y retryAfter de al menos un segundo; las lecturas tienen su propio cupo. Exporta todas las preguntas o las visibles, con «N°», «Recibida», «Cargó», «Canal» y «Estado» siempre: la recepción en la zona del perfil de quien exporta, como la muestra la planilla (lo guardado sigue en UTC), el canal con sus palabras, «WhatsApp» o «App» (XL2, 2026-11-09), y el estado con los de la app: Recibida, Por aprobar, Aprobada, Rechazada o Anulada (2026-11-11). Hasta 10.000 respuestas, una consulta de IDs ordenados e hidratación por lotes de 100, definición y columnas leídas una vez, número máximo fijado al empezar y presupuesto de 30 s, sin cola ni trabajos de fondo. ClosedXML (MIT), sólo en Infrastructure, mantiene tipos numéricos/fechas/booleanos y textos literales; encabezados en el idioma del usuario y el estado real de la respuesta, incluido Aprobada o Rechazada. Va en una sola hoja, «Respuestas»: el encabezado en negrita y con el tono de la planilla, fijo al bajar y con filtros; cada columna mide lo que lleva en sus primeras 200 filas, con tope, y las fechas van sólo con números, como en la app: `dd/mm/aaaa` en español y `mm/dd/aaaa` en inglés, con la hora después de una coma (2026-11-11). Opcional vacía es celda vacía; pregunta inexistente es «—». De los archivos incluye cantidad/nombres completos, sin incrustarlos. No hay tablas, migraciones, Redis ni mecanismos durables nuevos para estas lecturas.

El recorrido real verificó FOTO2B-000001: miniatura, visor con el original privado de Azure, descarga PDF, pregunta opcional vacía y recarga. En PILK7 se comprobaron facetas históricas (1 entrega a domicilio y 2 retiros), filtro y anterior/siguiente entre 000003 y 000001 conservados al recargar. El usuario abrió FOTO2B-20261016.xlsx en Excel sin reparación, con número, nombres de foto/PDF y opcional vacía. EXPLAIN sobre 10.001 respuestas/40.004 valores usa índices existentes y pagina después de filtrar; es una medición local, no una garantía de producción. Azurite volvió a ser predeterminado y Aspire quedó apagado.

**Etapa 3 cerrada el 2026-10-16.** Backend **2896/2896**, sin omitidos, con PostgreSQL/Redis/Azurite y build sin advertencias. Front **1703/1703**, lint/arquitectura **48/48**, build limpio y visual completa **451/451**, sin cambiar referencias ni tolerancias. Los contratos del front verifican bytes HTTP/XLSX exportados por esa corrida backend; las capturas automáticas usan transporte simulado. Se revisaron capturas de escritorio/celular en es/en y el recorrido real descrito arriba. Las reglas, aprobación, PDF y avisos de la Etapa 4 se consolidan abajo; carga web/importación y correcciones conservan sus planes. El resumen terminado se retiró con procedencia en [history](../history/README.md#formularios-etapa-3--2026-10-16).

## Tablero por formulario — DOC7-A

`POST /api/forms/definitions/{id}/dashboard/search` da el tablero de un formulario ([DOC7-A](../../../ArquitecturaBaseFront/docs/design/documentos.md); [Parte T del plan](../plans/2026-10-22-documentos-por-whatsapp.md#parte-t--los-tableros-por-formulario-doc7-a)). Se lee como la planilla: «Ver respuestas», o «Completar formularios» sobre lo propio (el servidor pone `OwnerUserId`), el permiso revalidado antes de responder, la política de lectura y no-store. El cuerpo es el de la búsqueda de la planilla bajo `filter`, con su gramática y sus reglas (`FormRecordFilterPolicy`), más un solo `period` para todo el tablero: `day`, `week` o `month` (otro valor es `Forms.Record.PeriodInvalid`); la página, el tamaño y el orden no cambian lo que suma.

- **La zona** (`timeZoneId`): la del perfil de quien lee, leída en el servidor y nunca del pedido, porque el front arma en ella el filtro de la planilla y un día tiene que ser el mismo en los dos; si no se lee, la del negocio (la de la presentación) y después UTC. Va por su id IANA, el que lee PostgreSQL: un nombre de Windows, que el perfil admite, pasa a su IANA.
- **Cuántas llegaron** (`count`) y, con los dos límites de recepción, el rango justo antes (`previous`, con sus límites y su cuenta). Con los dos límites en medianoches de la zona retrocede por su calendario: de un 1.º a otro 1.º, los mismos meses (octubre compara con septiembre entero); si no, los mismos días locales, también a través de un cambio de horario. Otro rango, el mismo largo.
- **Números** (`numbers`): de cada pregunta de número fuera de un bloque, cuántas la respondieron, total, promedio, mínimo y máximo, y el total y el promedio del rango anterior; sin respuestas, total 0 y lo demás null. Cuando el estándar del formulario dice qué pregunta de elegir trae la moneda, el número se suma por moneda: `currencyId` (la opción; null, las que la dejaron vacía) y `currencyLabel`, para «Total de Monto (pesos)». Lo declara la lectura del estándar (DOC1, `Reading.Currencies`: en «Comprobante de transferencia», el monto con su moneda) y `FormDashboardCurrencyPolicy` lo lleva a las preguntas de la copia por su clave, su versión y su mapa sellado; un formulario propio, otra versión del estándar o una copia que no ata los dos datos suman cada número entero.
- **Uno malo no frena al resto:** una suma, un promedio, un mínimo o un máximo que no entra en un decimal de C# vuelve null con `overflow: true` en su número (también en el total de un balde o una opción). Todo número vuelve redondeado a 28 cifras significativas y sin ceros de más.
- **Series** (`series`): un balde por día, semana desde el lunes o mes en la zona, con la fecha local en que empieza, cuántas llegaron y el total de cada número y moneda (`totals`: `fieldId`, `currencyId`, `total` y `overflow`). Va del principio del rango, o la primera respuesta, a su fin o a hoy (un mes en curso termina hoy), y siempre hasta la última respuesta; un balde sin respuestas va en cero. Con más de 400 quedan los últimos y `truncated` lo dice.
- **Series por fecha** (`dateSeries`): de cada pregunta de fecha fuera de un bloque, los mismos baldes por la fecha respondida, que no cambia de zona, en su propia ventana: de la primera a la última fecha respondida entre las respuestas, con el mismo tope, los mismos totales y los baldes vacíos en cero.
- **Repartos** (`distributions`): de cada pregunta de elegir (una o varias), sí o no (`true` y `false`) o de catálogo fuera de un bloque, las ocho opciones con más respuestas, con la etiqueta de los conteos de la planilla en el idioma del pedido y el total de cada número y moneda entre ellas (`totals`, en la misma sentencia); el resto va en `others`: cuántas respuestas (cada una una vez, aunque haya elegido varias de esas opciones), cuántas opciones (`optionCount`) y sus totales. Las ocho salen por cuenta; un empate en el corte se decide por etiqueta: la de una opción fija, de su versión; la de un catálogo, su última referencia capturada (para quien sólo completa, entre sus respuestas); y después por la clave. Una pregunta de catálogo nombra su catálogo (`catalog`: `id` y `name` en el idioma del pedido); una de opciones fijas, null.
- **Quién cargó** (`actors`): las ocho personas con más respuestas (un empate, por nombre), con su nombre (una cuenta borrada, sin él), y el resto en `others` (`count` y `peopleCount`). **Por estado** (`states`), cuántas en cada uno.

Todo se suma en SQL acotado (`FormDashboardReader`): toma sin ejecutarlo el comando del filtro de la planilla (`FormResponseQuery`, la pieza que comparten la planilla, sus conteos, la navegación, el Excel y el tablero), controla su forma como la navegación (`FormRecordNavigationSql.Ids`: un solo `SELECT` de `x."Id"`, sin `;`, `$` ni comentarios), conserva sus parámetros tipados y suma con SQL fijo, una sentencia por familia y nunca una por pregunta; la zona, el período, las preguntas, las etiquetas y el tope van como parámetros. La moneda de cada respuesta es una búsqueda por índice de su respuesta (sin pregunta de moneda, no encuentra nada en el acto). Sin respuestas sólo se leen los totales. No entran las preguntas de un bloque de renglones ni la ubicación. El tablero no guarda nada: los ajustes de cada persona viven en la dirección del front (decisión 23 del plan). Sus cuerpos HTTP son la cohorte `forms-dashboard-http`.

## Reglas con revisiones propias — Etapa 4, G1

`formrules.manage` permite consultar y guardar las reglas, sus revisiones y las opciones para configurarlas. Cada guardado crea una revisión inmutable de `BusinessFormRuleSets` y mueve `ActiveRuleSetId` bajo el lock de definiciones existente. `expectedRevision` parte de cero y un guardado atrasado devuelve 409. No cambia la revisión del borrador, las preguntas ni la versión publicada, ni llama a WhatsApp. Guardar una lista vacía deshabilita las reglas mediante una revisión nueva.

Cada regla conserva identidades de regla y acciones, evento Received/Approved/Rejected, hasta diez condiciones AND de la gramática de planilla y acciones ordenadas. Pedir aprobación sólo al recibir y al final de su regla; generar PDF sólo al aprobar. Adjuntar PDF al aviso de quien cargó exige una acción anterior de generación en esa regla. El esquema de preguntas queda fijado junto con las reglas para resolver placeholders por identidad aunque después cambien las etiquetas. Un dato del mensaje puede guardar la identidad de la pregunta, `{FieldId}`, en lugar de su nombre (MD1-A, 2026-11-02). Se valida contra las preguntas de las opciones, sirve igual en los dos idiomas y sigue valiendo si la pregunta cambia de nombre. El nombre entre llaves, en el idioma del texto, se sigue aceptando. Al leer las reglas, una revisión o el historial, se convierte en la referencia con el esquema fijado de esa revisión, aunque la pregunta ya tenga otro nombre. Así el editor y la comparación del historial ven una sola forma. Los textos nuevos exigen sólo el idioma original del formulario; la traducción es opcional. La [regla de idiomas vigente](#idioma-original-y-traducción-opcional--2026-10-19) abarca también publicación y envío.

La sección del editor tiene guardado e historial propios y comparte el aviso de salida por cambios pendientes. El historial resume reglas agregadas, editadas, quitadas y movimientos frente a la revisión anterior; insertar o quitar una regla no cuenta como mover las restantes. Releer una revisión no adopta ni sobrescribe el trabajo local: ante un 409 se elige usar las reglas actuales o conservar el borrador, que al guardarse reemplaza entera esa revisión. Las condiciones se muestran en palabras. Guardar se habilita con cambios; lo incompleto se marca al intentar guardar y la espera o el conflicto quedan como ayuda del botón (decisión del 2026-10-23). Listo, quitar o mover conservan un foco útil. Las opciones de condiciones de catálogo son metadata de configuración y no incluyen conteos ni respuestas privadas. «Adjuntar el PDF» nunca se desmarca solo (RP1-A, 2026-11-01): si «Armar el PDF» falta o está después del mensaje, o el evento no es Aprobada, el motivo aparece junto a la casilla en el momento, con el botón que sube o agrega el PDF cuando el orden lo arregla; la regla del servidor no cambia. Cambiar el tipo de una acción conserva el mensaje y el rol en el editor, y se guardan sólo con el tipo que los usa. Los datos del mensaje se ponen con «Agregar dato» (MD1-A, 2026-11-03): el número, el motivo al rechazar o una pregunta, como etiqueta con el nombre actual. Debajo, «Así llega» muestra el mensaje con los datos de ejemplo de la vista previa del PDF.

## Motor de acciones y aprobación — Etapa 4, G5 y G3/G4

Cada respuesta conserva la revisión de reglas con que llegó. Los runs de un evento fijan sus condiciones, datos, textos, destinatarios y diseño del PDF; la clave respuesta/evento/regla/acción impide duplicados. Las acciones esperan sólo a la anterior de su propia regla. Los avisos a un rol envían a los miembros posibles y conservan por persona nombre fijado, canal, estado y motivo seguro. Un rol vacío completa; cuando todos están aceptados o fallidos, termina y libera la acción siguiente. Las fallas individuales siguen visibles, y una persona con la falla confirmada se reintenta sola (RT1-A, 2026-11-03): el canal rechazó o no mandó su aviso, o alguien resolvió «No salió». El pedido exige `formrules.manage` y la revisión esperada del aviso, y da 409 si cambió o si el canal no puede reintentar esa falla. Reabre el aviso sólo para esa persona, sin repetir a quienes lo recibieron ni la acción siguiente de la regla, y al terminar vuelve a «Hecho». Una condición falsa queda «No aplica». Se reserva con lease y fence, se ejecuta el efecto fuera de la transacción y se confirma bajo el fence vigente. Un resultado incierto se consulta antes de repetir; un resultado confirmado nunca vuelve a enviarse. La revisión humana de un aviso incierto fija destinatario y revisión esperada, con atribución y replay idéntico; nunca resuelve a otros destinatarios. «No se envió» permite reintentar el aviso al autor, o a esa persona en el aviso al rol. La revisión humana no acredita Entregado/Leído.

La aprobación es de un nivel y queda asignada a un rol. Al reservar, se vuelve a comprobar que el rol exista; si fue borrado, la acción falla con `Forms.Rule.RoleUnavailable`, sin abrir tarea ni pasar la respuesta a Por aprobar. Sólo una cuenta activa con `formapprovals.decide` y pertenencia vigente al rol puede decidir, y nunca quien confirmó la respuesta. La revisión esperada evita dos decisiones: cerrar tarea, cambiar estado y crear las acciones del evento es un único commit. Rechazar exige motivo de hasta 500 caracteres. Una decisión tardía conserva la revisión de reglas de la captura.

La solapa «Por aprobar (N)» de cada formulario (PL1-A, 2026-11-02; para quien sólo decide, el bloque «Por aprobar · N») ordena las tareas de más vieja a más nueva, dentro de los roles actuales y excluyendo las respuestas propias. La cantidad proviene del servidor. Este permiso permite leer metadata de la tarea; los valores y PDF siguen exigiendo `formrecords.read`. La planilla muestra Estado si las reglas actuales o alguna revisión usada por sus respuestas piden aprobación. Su filtro es una selección única (Recibida, Por aprobar, Aprobada o Rechazada), compartida por lista, conteo, facetas y Excel; cada faceta ignora sólo su propia dimensión y conserva el estado elegido. La fila indica si existe una copia PDF confirmada mediante la consulta de la página, sin consultas por respuesta ni identificadores de documento en cada fila: una aprobación por sí sola no implica que el PDF ya esté listo. El historial conserva quién decidió, cuándo y el motivo bajo el permiso de lectura de respuestas.

El detalle agrupa únicamente momentos observados de la revisión capturada: recepción/origen, tarea o decisión y efectos de cada evento ocurrido. La fecha de pedido de aprobación viene de la tarea real y permanece al decidir; no usa la fecha de aceptación del run. El origen conserva el tipo de emisor capturado, Manual o Asistente, y al operador real sólo cuando lo hubo, distinto de quien respondió; una identidad o un origen desconocidos no se inventan. Un RequestApproval completado se integra en el momento de su tarea real; uno fallido sigue visible como incidencia, incluso si otra regla abrió la tarea. Sin tarea no se crea un momento Por aprobar. No se consultan reglas vigentes ni se inventan eventos futuros para formar el resumen. Quien sólo puede decidir conserva metadata de tarea y decisión; valores, workflow, fotos y PDF requieren formrecords.read.

D2 abre la respuesta en un panel junto a la planilla o a Por aprobar, manteniendo la lista y sus filtros. La dirección conserva la selección y la pestaña; ↑ y ↓ recorren el orden filtrado y Escape cierra y devuelve un foco útil a la lista. Con lectura, en el celular abre directamente la pantalla completa DC1; quien sólo decide conserva el panel de metadata. El resumen muestra los momentos observados y sus incidencias; mientras espera, hay una sola edición de la decisión en Por aprobar, con sus controles al pie en el celular. Las pestañas son Respuesta, Fotos y Actividad; PDF aparece sólo cuando existe una copia fija. Cambiar de pestaña conserva una revisión o decisión en curso; cambiar de respuesta impide actuar sobre datos anteriores mientras carga. La descarga iniciada conserva su dueño aunque se cierre el menú, y se cancela al salir de esa respuesta.

La captura fija la revisión y crea sus runs antes de confirmar recibo y número, junto con valores, referencias y archivos. Los avisos usan el [canal neutral de acciones](whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6), con un commit separado en el canal y sin referencias entre módulos. Al preparar cada evento, el idioma de quien recibe debe estar completo en todos los avisos de esa revisión de reglas y en el diseño PDF vigente si la revisión lo genera. Si falta cualquier texto, los mensajes y las copias usan íntegramente el original del formulario; esa elección queda fijada en los runs.

## El bloque de renglones al armar y publicar — RG1-A, RG3-A y RG6-A, 2026-11-04

Un formulario puede llevar un bloque de preguntas que se cargan una vez por ítem, como los artículos de un remito con su
cantidad y sus observaciones. El editor lo arma como una pregunta más, y publicar lo revisa y lo compila para WhatsApp. Los
textos dicen «ítem» (RG12-A); el código y los documentos siguen diciendo renglón (`rows`). Pantallas y textos:
[renglones en los formularios](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md).

- **El borrador.** El bloque es una pregunta `group` con nombre, ayuda y `rows`, el mínimo y el máximo de ítems. Sus
  preguntas van contiguas detrás, marcadas `repeated` («Se carga varias veces», RG6-A): texto corto, texto largo, decimal,
  fecha, sí o no y elegir una, propia o de un catálogo. Elegir varias, las fotos y los documentos no entran en un ítem. Hay
  uno por formulario y ningún título de sección cae adentro. El bloque no cuenta entre las 40 preguntas y sus preguntas sí:
  el tope, `QuestionCount` y «Preguntas» en «Qué se publica» cuentan las que no son `group`. Un bloque nuevo arranca con 1
  y 20 («Agregar bloque», RG10-D) y con mínimo 0 es opcional; vacío se guarda, pero no se publica. Los campos nuevos se
  omiten cuando son null (`repeated: false` se guarda como null), así un formulario sin bloque conserva sus bytes, huellas y
  hashes.
- **Entrar y salir del bloque.** Pasar una pregunta guardada al bloque, o sacarla, le da una identidad nueva
  (`replacesFieldId`), como cambiar el tipo: la versión publicada conserva la anterior y sus respuestas. Renombrar, ordenar
  y traducir conservan la identidad; conservar el FieldId con otro `repeated` da `QuestionConfigurationInvalid`.
- **Publicar.** Un bloque sin preguntas frena (`Forms.Publication.GroupEmpty` en `draft.fields[i]`). Con un canal
  instalado, un máximo mayor que lo que carga el canal avisa sin frenar (`RowsOverChannelLimit` en `draft.fields[i].rows`,
  con `limit` y `maximum`: «En WhatsApp se cargan hasta 10 ítems; el máximo es 20.») y un mínimo mayor frena (RG13-A,
  [en la carga](#el-bloque-de-renglones-en-la-carga--rg2-a-y-rg3-a-2026-11-07)). «Pasan al bloque» (`diff.movedToRows`,
  omitido si está vacío) nombra las preguntas publicadas que entraron al bloque, en lugar de mostrarlas quitadas y
  agregadas: cada pregunta nueva del bloque se empareja con una quitada de afuera del mismo tipo, el mismo catálogo y el
  mismo nombre en el idioma original; si además se renombró, queda como quitada y agregada. La aceptación registra el bloque
  y sus preguntas en `BusinessFormField` y sella `rows` y `repeated`.
- **WhatsApp (RG3-A, RG7-A y RG8-A).** El perfil `flow-static-7.3-r10` suma una pantalla por ítem, hasta 10 o el máximo si
  es menor, titulada «Artículos · ítem 2». Desde el mínimo, cada ítem termina con «¿Agregás otro ítem?», sí o no y
  obligatoria: con «Sí» sigue el siguiente y con «No», lo de después del bloque o, si el bloque es lo último, una
  pantalla de cierre con el botón final (r12, 2026-11-09); el último no pregunta. Con
  mínimo 0, una pantalla pregunta antes «¿Agregás un ítem?». Con bloque, la respuesta tiene el tope de 64 KiB de los
  formularios con archivos. El detalle y la verificación con Meta están en
  [WhatsApp](whatsapp.md#selección-múltiple-y-perfil-r9).
- **Lo que no toma el bloque.** El asistente no prellena sus preguntas, las sugerencias no las mapean y la lectura de
  formularios de la IA no cuenta el bloque entre las 40. Las reglas sí lo leen: ver «Reglas» en
  [la lectura](#el-bloque-de-renglones-en-la-lectura--rg4-a-2026-11-07).

## El bloque de renglones en la carga — RG2-A y RG3-A, 2026-11-07

Un bloque de renglones se carga desde la app y desde WhatsApp y se guarda como una respuesta más, con sus ítems. El editor,
publicar y las pantallas del Flow están [al armar y publicar](#el-bloque-de-renglones-al-armar-y-publicar--rg1-a-rg3-a-y-rg6-a-2026-11-04);
acá va lo que guarda y valida la carga. Los textos dicen «ítem» (RG12-A).

- **El bloque.** Una pregunta `group` con `rows` (mínimo de 0 a 100, no mayor que el máximo, de 1 a 100; obligatoria si el
  mínimo es mayor que 0) seguida sin huecos por sus preguntas `repeated`: texto, decimal, fecha, sí o no, o elegir una. Hay
  a lo sumo uno y no cuenta entre las 40 preguntas. En la app se cargan hasta el máximo; en WhatsApp, hasta 10, o el máximo
  si es menor.
- **Cómo se guarda.** La entrada del bloque en `ValuesJson` es `{ fieldId: <bloque>, value: [ [ {fieldId, value}, … ], … ] }`:
  los ítems en orden, una pregunta sin respuesta sin entrada en su ítem, un ítem vacío `[]` y un bloque sin ítems `[]`. Las
  preguntas `repeated` no tienen entrada arriba. Cada valor y cada referencia de catálogo llevan su `Row` (de 0 a 99, 0
  fuera del bloque), que entra en sus índices únicos (`RecordId, FieldId, Row, Position` y `RecordId, FieldId, Row,
  ItemId`): el mismo artículo puede ir en dos ítems. La migración `AddFormRecordRows` no reescribe filas; su Down rechaza
  una respuesta de una versión con bloque, un `Row` mayor que 0 o un recibo con `RejectedRow`.
- **La captura.** El bloque llega en su clave como un arreglo JSON con un objeto por ítem y sus valores canónicos, en el orden
  de las preguntas (`FormRecordAnswerPolicy`), con tantos ítems como admiten el mínimo y el máximo: fuera de ese rango, la
  respuesta es inválida. Cada ítem se valida como preguntas sueltas: obligatorias, tipo, largo y que la opción siga
  ofrecida. La oferta de un catálogo se resuelve una vez por pregunta, o una por respuesta distinta cuando la lista depende
  de otra pregunta del ítem, y la actividad de una opción Manual se revisa una vez, donde aparece primero. Una opción dada
  de baja en un ítem rechaza la respuesta, y el recibo dice la pregunta y el ítem (`RejectedFieldId`, `RejectedRow`). Sin
  bloque, la huella y los bytes guardados no cambian.
- **La app (RG2-A).** La carga trae el bloque con `rows` y sus preguntas con `repeated: true`; el envío manda el bloque como
  `{ fieldId, rows: [[{ fieldId, value }, …], …] }`, aun sin ítems. El error de un ítem vuelve en
  `answers.<bloque>.<n>.<pregunta>` (n desde 0), también el de una opción dada de baja. Una lista del bloque que depende de
  otra pregunta del bloque usa la respuesta de su propio ítem, y una que depende de una pregunta de afuera, esa (LD3-A).
  Corregir abre la respuesta con sus ítems en orden, con las preguntas que siguen en el bloque de la versión activa. En la
  página el bloque es una tabla y en el panel y el celular, un recuadro por ítem
  ([diseño](../../../ArquitecturaBaseFront/docs/design/renglones-en-formularios.md)).
- **WhatsApp (RG3-A).** La respuesta trae cada ítem por su número y el «¿Agregás otro ítem?» de cada uno; los ítems llegan
  hasta el primer «No» y lo que viene después no cuenta ([recepción](whatsapp.md#recepción-de-respuestas-de-flow)).
- **Publicar (RG13-A y LD3-A).** Con un canal instalado, un mínimo mayor que 10 frena la publicación
  (`Forms.Publication.MinRowsOverChannelLimit` en `draft.fields[i].rows`, con `limit`): WhatsApp no podría cargar ese
  mínimo. Un máximo mayor que 10 sólo avisa. Una pregunta de afuera del bloque que filtra por la respuesta de una del
  bloque frena con `Forms.Publication.MatchQuestionInvalid`: no hay un solo ítem del que tomarla (LD3-A).

## El bloque de renglones en la lectura — RG4-A, 2026-11-07

Una respuesta con un bloque de renglones se lee entera: la planilla, «Filtros», la vista rápida, la pantalla completa, la
tarjeta del chat, el Excel y el PDF. Los textos dicen «ítem» (RG12-A); el código y los documentos siguen diciendo renglón.

- **La lectura neutral.** `FormRecordPresentationPolicy.Project` trae el bloque en su lugar como valor `group`, con sus
  ítems (`StructuredRecordReadRow`, de `Rows`): todas las preguntas del bloque de su versión, en orden, null en las vacías y
  la referencia de catálogo de su propio `Row`. Las preguntas `repeated` van sólo adentro. Un bloque sin ítems es `[]`.
- **La planilla.** Una fila por respuesta. El bloque tiene una columna `group` con `options: []` y `questions`, sus
  preguntas unidas por FieldId entre las versiones que tuvieron ese bloque, primero en el orden de la activa; sus preguntas
  no tienen columna suelta. La celda es NotAsked, Unanswered (`[]`) o Answered con un `{ cells }` por ítem y una celda por
  pregunta de `questions` (NotAsked la que el bloque de su versión no tenía). No se ordena por el bloque ni por sus
  preguntas (`Forms.Record.FilterInvalid`). El front muestra «3 · GLIFOSATO 64%, …»: la cantidad en gris y el primer valor
  respondido de cada ítem.
- **«Filtros».** Las preguntas del bloque filtran con los operadores de su tipo; el bloque mismo, no. Varias condiciones
  con valor sobre preguntas del bloque las tiene que cumplir un mismo ítem (RG5-A): un `EXISTS` sobre la respuesta con cada
  condición buscada en el `Row` de la anterior, sin multiplicar respuestas. La presencia mira la respuesta entera. Cada
  opción de los conteos cuenta las respuestas con un ítem que la tiene y cumple ahí las otras condiciones del bloque; la de
  un catálogo sale de las referencias de todos los ítems. «Buscar en textos largos» lee también los de los ítems y el
  total cuenta respuestas. En el front, las preguntas van bajo el nombre del bloque con «En algún ítem de {bloque}».
- **El detalle.** `values` trae el bloque con un `{ values }` por ítem. La vista rápida (D2) lista cada ítem con su número
  y su primera respuesta en negro y las demás alineadas debajo; la pantalla completa (DC1), una tabla con el número a la
  derecha y una columna por pregunta. Sin ítems, «Sin respuesta».
- **El chat.** La tarjeta recibe el bloque por la misma lectura, sin campos del catálogo, y pone un ítem por línea.
- **El Excel** (XL1-A, 2026-11-09). Una sola hoja, con una fila por ítem: los datos de la respuesta se repiten en cada
  uno de sus ítems y, en el lugar del bloque, van «Ítem» y sus preguntas, con su «· valor» y los campos que elige
  `catalogFields`, en el orden de las respuestas. «Ítem» va una vez aunque dos versiones tengan bloques distintos. Una
  respuesta sin ítems ocupa una fila con «Ítem» vacío, una de una versión sin el bloque lleva «—» y una línea marca dónde
  empieza cada respuesta. Con el bloque fuera de «Solo las visibles» no hay columnas del ítem. Hasta 100.000 filas, el
  tope que eligió el usuario al medirlo (un millón tardaba 84 s): si se pasa, se corta antes de escribir con
  `Forms.Record.ExportTooManyRows`. Una pregunta de ubicación ocupa tres columnas numéricas, «· Latitud», «· Longitud» y
  «· Precisión (m)» (UB4-A); nunca está en el bloque.
- **El PDF.** El diseño elige el bloque entero: el inicial, el de una respuesta sin diseño guardado y las preguntas que se
  pueden elegir dejan afuera las del bloque, y un idioma está completo si también lo están ellas. La copia pone el bloque
  bajo su nombre en una tabla: el número del ítem en una columna angosta y sin encabezado, y una columna por pregunta de su
  versión, que se reparten el resto por igual; línea entre columnas, números a la derecha, texto partido en su celda y el
  encabezado repetido si sigue en la página siguiente. La vista previa suma dos ítems de ejemplo. Una pregunta del bloque
  elegida sola en un diseño viejo no sale.
- **Reglas.** Las condiciones de una regla y de una sugerencia deciden como «Filtros» (Etapa 4 de renglones, RG5-A): las
  que tienen valor sobre preguntas del bloque las cumple un mismo ítem, y la presencia mira la respuesta entera. En un
  mensaje, el bloque es un dato: cada ítem va en su línea, numerado desde 1, con sus respuestas en el orden de las preguntas
  del bloque, separadas por « · » y sin las vacías. El primero empieza en una línea nueva, salvo al comienzo del mensaje o
  después de un salto de línea. Sin ítems, o en una versión sin el bloque, el dato queda vacío, y un ítem vacío deja sólo su
  número. Una pregunta del bloque no es un dato: guardarla da `Forms.Rule.PlaceholderInvalid`.

Límites conocidos, sin caso real hoy: un bloque con muchas preguntas angosta las columnas de la tabla del PDF, un ítem
partido entre páginas no repite su número y un formulario con muchas preguntas puede llegar al presupuesto de 30 segundos
del Excel antes que a las 100.000 filas.

Fuera de alcance, decidido con el diseño:
- más de un bloque, o un bloque adentro de otro;
- fotos o documentos por ítem;
- ordenar la planilla por el bloque;
- condiciones sobre todos los ítems o sobre su cantidad;
- duplicar un ítem o importar ítems desde Excel;
- la lista con los nombres antes de enviar por WhatsApp.

La prueba de punta a punta salió entera el 2026-11-09, con el usuario y clic por clic. Cubrió el editor, publicar con
preguntas que pasan al bloque, la carga desde la app y por WhatsApp (con la pantalla de cierre de r12), una respuesta de
34,6 KB, la lectura, la corrección y las reglas. Lo que quedó está en
[verificación y producción](../plans/verificacion-y-produccion.md#formularios-renglones-lo-que-quedó-de-la-prueba-integral--2026-11-11).

## Diseño y copia fija del PDF — Etapa 4, G2

El diseño tiene revisiones inmutables en `BusinessFormPdfDesigns` y puntero `ActivePdfDesignId`. Guardar exige `formrules.manage`, revisión esperada y el lock de definiciones; un guardado atrasado devuelve 409. El nombre original es obligatorio (hasta 160 caracteres), la traducción opcional y el pie opcional (hasta 1000). La selección conserva identidades: una pregunta nueva no se agrega a un diseño guardado; una retirada que ya estaba seleccionada puede seguir, pero sólo se dibuja si existe en la versión capturada. El diseño inicial no guardado incluye las preguntas de esa versión, aprobación y fotos, con revisión cero. No cambia el borrador ni publica nada.

El preview usa ejemplos y no guarda configuración, respuesta ni documento. Devuelve `application/pdf`, no-store y `Content-Language` con el idioma completo usado. Si falta nombre, pie o etiqueta seleccionada en el idioma pedido, toda la copia usa el original. La falta de traducción de texto/detalle de un ítem de catálogo usa su original individual, sin cambiar por eso el idioma de la copia.

Al aprobar, la acción fija diseño, nombre del sistema, número, versión, datos, etiquetas y decisión. SkiaSharp genera A4 con fuentes Public Sans OFL embebidas, texto ajustado y paginado. Los grafemas que Public Sans no cubre usan Noto Emoji OFL embebida y HarfBuzz, en contornos monocromos; la medición y el dibujo comparten la composición para conservar modificadores, secuencias ZWJ y banderas al partir líneas. No consulta fuentes del sistema. Skia guarda los contornos de la fuente variable en recursos Type3 autocontenidos; su ToUnicode conserva el texto común, pero la extracción o copia de ciertos emojis compuestos ZWJ y banderas puede ser incompleta. La representación visible se verifica por separado. Las fechas se indican en UTC. Con «Incluir las fotos», las miniaturas chicas quedan debajo de su pregunta dentro de la copia; sin marcar, se muestra su cantidad. Los documentos sólo se nombran. La aprobación muestra quién decidió y cuándo. Ningún PDF de respuesta se emite antes de aprobar.

La clave privada depende de run y cultura. Antes de generar se lee la copia existente; la creación condicional conserva el primer archivo y confirma tamaño y SHA-256 de esos bytes. Una recuperación sólo consulta y nunca genera hasta que se confirme ausencia y se pida reintentar. La confirmación del motor agrega metadata de archivo y documento bajo el fence vigente, una vez por run/cultura. Una fuente neutral de retención protege los PDF subidos que siguen pendientes de confirmación mientras exista su run/respuesta, incluso durante una caída de más de un día.

La copia fija aparece en el detalle y se abre o descarga sólo por la API con `formrecords.read`, pertenencia al registro y revalidación del permiso después de abrir el stream. Nunca se entregan claves de almacenamiento ni enlaces a Azure. La vista inline usa un blob del navegador y se retira al perder el permiso; cambiar reglas, catálogos o diseño no altera una copia emitida.

## Verificación de la Etapa 4 — 2026-10-16

Implementación y verificación local completas, incluidos los 14 arreglos del review de la Etapa 3. Backend **3096/3096**, cero omitidos, con PostgreSQL/Redis/Azurite y build de cero errores y advertencias. La corrida definitiva `20261016-203953-d6165b4572ea4d019735be2c387de43f` corresponde a `98fa775c`, con `sourceState` vacío: el exportador terminó con código 0 y copió **67 capturas en seis cohortes**. Sus bytes, tamaño y SHA256 coinciden con la salida HTTP y las fuentes productoras.

Front: build y lint limpios, suite completa **1793/1793 en 188 archivos**. Tras copiar las cohortes definitivas, **13/13 en 7 archivos** de consumidores/procedencia pasaron en 9,23 s; las fuentes productoras conservan los mismos hashes. Visual completa **493/493**, con referencias intactas. Las capturas visuales usan transporte simulado; los HTTP de permisos, revisiones, decisiones concurrentes, recuperación y descarga se acreditan en la corrida backend.

El recorrido de navegador contra la aplicación local verificó migraciones y AppHost Healthy; la bandeja vacía en Para mí/Todas; reglas con original y traducción vacía; diseño PDF inicial de revisión 0, Incluir fotos y preview HTTP con respaldo completo al original. Se descartaron los cambios y se confirmó que las reglas seguían en revisión 0. También se verificaron el filtro decimal 12,5, el conteo y un detalle histórico con posición, origen e historial. El visor PDF nativo no se pudo calificar en IAB/headless: el iframe quedó blanco; los bytes PDF, el color de las fotos y la salida de cuatro páginas se verificaron por render técnico separado. Aspire quedó detenido, sin procesos auxiliares activos.

El plan y el review se retiraron con procedencia en [history](../history/README.md#formularios-etapa-4-y-review-de-la-etapa-3--2026-10-16). La verificación local usó almacenamiento emulado y Meta simulado. Después se completó el [piloto real en español](#piloto-de-aprobación-y-pdf--2026-10-17); los casos físicos de idioma conservan su [pendiente separado](../plans/verificacion-y-produccion.md#formularios-piloto-de-aprobación-y-pdf).

## Piloto de aprobación y PDF — 2026-10-17

Con backend `d79f2a3` y front `5aa6318`, Aspire con túnel, Meta y Azure privado reales, se verificó en español **Registrar visita**, versión 1: `VISITA-000001`, reglas y diseño PDF revisión 1, con foto obligatoria e incluida en el PDF. Llegó «Recibimos…», la visita apareció en Por aprobar y otra cuenta activa del rol Admin, con `formapprovals.decide`, la aprobó. El usuario confirmó recepción y apertura del PDF con la foto en su celular. Las cuatro acciones quedaron en **Hecho**; antes de aprobar no había PDF.

Incidencias observadas en ese piloto, sin modificar código durante la prueba: la web rechazó `origin` pese al POST 202 y GET 200, y Chrome bloqueó el visor inline. El review posterior corrigió ambos contratos/consumidores; su verificación local consta en el cierre siguiente. Los recorridos físicos posteriores en inglés y con respaldo al original constan en el [piloto del asistente](#piloto-del-asistente-y-audios--2026-10-18). Aspire y el túnel quedaron detenidos.

La lectura del historial observa un lote de estados y receipts locales por canal, también después de Hecho. Entregado y Leído muestran sólo fechas reales del proveedor; una lectura no infiere entrega. Una aceptación tardía deshabilita la revisión de esa persona y conserva la decisión humana original como evidencia separada. Leer no modifica ni reabre la ejecución y no reenvía; los permisos se vuelven a comprobar antes de devolver el detalle.

## Indicación de ofrecimiento — Etapa 5

«Cuándo lo ofrece el asistente» es un texto original opcional de hasta1000 caracteres dentro de «El formulario». Se recorta al guardar; vacío significa que no se ofrece. Usa Guardar borrador, la misma revisión y el mismo recibo; el conflicto conserva el valor para revisión y no guarda de nuevo sin una acción explícita. No se traduce por cambiar el idioma de edición.

La indicación pertenece a la definición actual: no se copia al esquema de preguntas, al snapshot de una versión ni al cuerpo o la huella de publicación. Guardarla rige sin republicar. Las preguntas y su publicación siguen la [regla de original obligatorio](#idioma-original-y-traducción-opcional--2026-10-19). La selección del asistente debe leer preguntas de la versión publicada y la indicación vigente, sin usar preguntas del borrador.

## Propuestas de prellenado — Etapa 5

BusinessForms aporta al motor único una lectura de candidatos y una propuesta tipada. El snapshot incluye hasta cien formularios preparables y cien temas pendientes dentro de los 16 KiB de la herramienta; marca parcial si queda contenido fuera. Una referencia incluida puede justificar una propuesta; solo una lectura completa puede justificar un pedido faltante. Un catálogo que esa persona no puede usar excluye ese formulario, sin quitar los demás. Sólo lee versiones publicadas, activas y completas que la cuenta vinculada puede completar, junto a «Cuándo lo ofrece» vigente; lo inaccesible equivale a ausente. La propuesta conserva la lectura exacta del turno y la revisión de la definición. El dueño vuelve a comprobar cuenta, permisos, publicación y catálogos antes del envío. No confirma una respuesta. Los límites coinciden con la autoría: nombre de hasta 120 unidades UTF-16, indicación de hasta 1000 y etiquetas de preguntas y opciones de hasta 160 grafemas. Una candidata inválida se excluye, marca la lectura parcial y no impide incluir las siguientes.

El prellenado admite texto corto y largo, decimal sin redondeo, fecha ISO, booleano y una opción realmente ofrecida. El esquema declara sus formatos canónicos; un escalar con formato inválido se omite sin perder los valores válidos, incluida una elección sin formato GUID D. Una identidad, tipo o GUID de opción ajenos a la lectura observada siguen rechazando la propuesta, incluido el GUID vacío. No propone fotos ni documentos. Las instrucciones de prellenado piden sólo hechos de negocio explícitos para las preguntas y omitir campos sin hechos; una instrucción citada dirigida al asistente, una orden de cambiar destinatario o permisos y un metacomentario no son valores de respuesta. El motivo de selección se conserva aparte. Esto no excluye hechos de negocio legítimos por estar citados o contener nombres o identificadores. Sólo se reciben identidades de campos incluidos desde la web; valores y etiquetas salen de la propuesta observada. Los perfiles `flow-static-7.3-r8` y r9 usan datos dinámicos e inicialización de controles escalares sin completar valores ausentes; conservan false y cero. Las publicaciones nuevas usan r10 (r9 más el bloque de renglones) y las preguntas de varias y las del bloque quedan fuera del prellenado. Las publicaciones r7 conservan sus bytes y requieren republicar para adquirir esta capacidad.

Al preparar un envío se omiten los mapas de inicialización sin valores, tanto en Manual como al revisar una propuesta sin campos incluidos. Los mapas poblados conservan los valores exactos, incluidos cero y false; las opciones de catálogo viajan por separado aunque no haya prellenado. Sin catálogos ni valores iniciales, la navegación contiene sólo la pantalla. El arreglo conservó el perfil r8 y el JSON publicado: rige los envíos nuevos y no requiere republicar. Las publicaciones nuevas usan ahora [r9](#elegir-varias-en-whatsapp--e1-c3-2026-10-22), sin cambiar las sesiones r8 abiertas. La [referencia de Meta](https://developers.facebook.com/docs/whatsapp/flows/reference/flowjson/) documenta omitir el dato para representar su ausencia.

## Configuración de qué sugerir después — Etapa 5

`formsuggestions.manage` permite guardar una lista independiente de hasta 20 sugerencias, cada una con destino publicado, modo Assistant o Always, condición opcional, mapeos y estado activo. La fuente y cada destino fijan su versión publicada al guardar; el destino no necesita «Cuándo lo ofrece». Assistant exige una indicación original; Always guarda esa indicación como null. Los mapeos unen preguntas escalares del mismo tipo; las elecciones requieren el mismo catálogo explícito en origen y destino. Las opciones independientes no se vinculan por etiqueta ni por coincidencia de ID. El guardado y el prellenado actual verifican esa pertenencia; el número de respuesta puede ir a texto corto o largo.

El guardado usa una sola transacción, lock de definiciones, revisión esperada y 409; no publica ni guarda preguntas. Cada revisión es inmutable y conserva su autor. El historial paginado obtiene los datos en lote y el editor no reconstruye nombres históricos con publicaciones posteriores. El editor conserva la edición ante conflicto o validación y muestra los errores de campo sin cerrar la fila.

Al confirmar una respuesta se evalúa la configuración vigente contra ese registro real y sus versiones capturadas. Si hay varias opciones Always elegibles, se ofrece la primera en el orden guardado; se conserva una sola oferta por captura. Sin Always, el modo Asistente puede evaluar las opciones Assistant con capacidades limitadas a ese origen. La lectura de continuación entrega el conjunto completo, sus respuestas escalares, opciones y mapeos dentro de 16 KiB, incluido el envoltorio JSON. Si no cabe, queda no disponible sin autorizar una propuesta ni probar ausencia; no recorta el contenido ni lo presenta como parcial. Una propuesta fuera de presupuesto se rechaza. En Atención manual la propuesta queda para confirmar; Always funciona también con IA no disponible. No se vuelve a ofrecer automáticamente un formulario rechazado ni un antecedente de la cadena.

La preparación vuelve a comprobar el registro, la revisión, los mapeos, las opciones y la cuenta destinataria. La revisión o el envío manual requieren además formrecords.read antes y después de leer el origen; una propuesta nueva sobre texto no hereda ese permiso. Los identificadores Assistant se derivan sólo de la propuesta auténtica observada por BusinessForms; Always fija su configuración sin inventar un Id de IA. El motivo de la sugerencia sigue el idioma de la conversación; la presentación del formulario, los mensajes de apertura y el PDF siguen la cultura completa que resuelve BusinessForms. La respuesta final conserva el origen real, lo incluido y lo corregido, y la cadena enlaza únicamente respuestas confirmadas.

## Catálogos propuestos — Etapa 5, H4

El chat administrativo puede proponer un catálogo de tipo Filters, nunca SQL. El modelo recibe sólo metadata permitida, sin personas; BusinessForms valida la lectura observada, los filtros, roles y límites, y arma la vista previa. La creación es idempotente por propuesta y deja el catálogo inactivo.

Una cuenta activa con `formcatalogs.read` y `formcatalogs.manage` revisa y aprueba o descarta en la web, con revisión esperada y 409. Aprobar vuelve a probar los filtros con una persona de ejemplo activa antes de activar; guardar o reactivar por la edición común no elude esa decisión. La propuesta, sus limitaciones, procedencia y decisión se conservan después de editar. Como en el alta común, sólo se exige el nombre en el idioma original del catálogo.

Por WhatsApp (IA3-A), la propuesta llega como resumen con «Crear catálogo» y «Cambiar algo»: el catálogo se crea con el botón, desde la conversación propia de la persona, y la evidencia no lleva `requestedIn`.

## La IA arma formularios y catálogos — Etapa 1, 2026-11-04

Quien puede diseñar formularios (`formtemplates.read` y `formtemplates.manage`) o administrar catálogos (`formcatalogs.read` y
`formcatalogs.manage`) le pide al asistente, por escrito o con un audio, que arme un formulario o un catálogo Manual sin planilla,
en Inicio (IA1-A) y por WhatsApp (IA3-A). El asistente lee los nombres y campos de lo que existe (`forms.authoring.forms` y
`forms.authoring.catalogs`, cada uno con su permiso de leer y en 16 KiB) en el idioma de la conversación, o en el original si no
tienen traducción: es el mismo nombre que compara crear. Propone con `forms.authoring.propose-form` o
`forms.authoring.propose-catalog`. BusinessForms valida la propuesta: el formulario, con `SaveFormDraftRequestValidator` y sólo con
catálogos activos que la persona puede leer; el catálogo, con `FormAuthoringProposalPolicy`. Un dato inválido vuelve al modelo como
`rejected` con las rutas de sus argumentos para que lo corrija; nunca se elige ni se recorta. Un prefijo pedido que no cumple el formato
(de 1 a 12 mayúsculas o cifras, empezando con una letra) se cambia, sin preguntar, por el válido más parecido, en mayúsculas y sin
espacios ni símbolos: la propuesta lo muestra y la persona decide. La IA no publica, no activa ni importa.

En Inicio la propuesta es una tarjeta bajo la respuesta que la trae: «Ver cómo queda» abre la vista previa del editor, y «Crear
borrador» o «Crear catálogo» llama a `POST api/forms/definitions/assistant-proposals` o `POST api/forms/catalogs/assistant-proposals`
con el `requestId` del turno y el `proposalId`. El servidor relee la propuesta con `IAssistantProposalLookup` (sólo un turno completo
de Inicio del mismo actor, con su sello, su acceso y la reserva de permisos revalidados), la crea una sola vez (repetir devuelve lo
creado) y rechaza una propuesta que otra posterior de la misma capacidad ya reemplazó: un catálogo pedido después de un formulario
no reemplaza al formulario.
El formulario queda en borrador. El catálogo Manual queda inactivo y pendiente con la banda «Propuesta del asistente», que dice «en el
chat de Inicio», y se activa con «Aprobar y activar», que recibe sólo `expectedRevision`; la edición común no activa una propuesta
pendiente. Un nombre o un prefijo en uso lo dice la tarjeta (400 con su código): el nombre se compara sin mayúsculas con el nombre visible en el
idioma de la propuesta. Los catálogos que usa un formulario tienen que seguir activos y legibles al crearlo.

Por WhatsApp la propuesta es un resumen en texto fijo con «Crear borrador» o «Crear catálogo» y «Cambiar algo», y sólo valen los
botones del último ([WhatsApp](whatsapp.md)). Allí `forms.authoring.decline` dice por qué no se arma nada (falta un permiso de leer o
administrar, o lo nombrado ya existe y la persona puede leerlo): BusinessForms comprueba el motivo y uno equivocado vuelve `rejected`.
El catálogo de personas con filtros (H4) se crea con el mismo botón.

Un pedido que no dice qué armar («Armame un formulario nuevo para el taller») pregunta y espera, como en Inicio (decidido el
2026-11-07). Por WhatsApp el motivo es `unclear`: el canal contesta con un texto fijo («No me queda claro qué querés que arme. Contame
si es un formulario o un catálogo, para qué es y qué datos lleva, por escrito o con un audio.») y el mensaje siguiente, escrito o
dictado, continúa esa misma operación, como después de «Cambiar algo», así que la IA propone con el pedido y la respuesta juntos.
`unclear` no nombra nada y sólo vale para quien puede armar un formulario o un catálogo; si no, vuelve `rejected` con
`permissionMissing`. La pregunta no es una oferta ni gasta cupo.

## La IA arma formularios y catálogos — Etapa 2, planillas (2026-11-09 a 2026-11-11)

Con una planilla, un Excel `.xlsx` o un CSV, la IA propone en Inicio y por WhatsApp un catálogo Manual con el mapeo de sus
columnas, o importarla en uno que existe. Lo técnico está en la IA
([el motor](inteligencia-artificial.md#planillas-en-el-motor--etapa-2-2026-11-09) e
[Inicio](inteligencia-artificial.md#planillas-en-inicio--etapa-2-2026-11-09)), en
[WhatsApp](whatsapp.md#planilla-recibida--ia-etapa-2-2026-11-10) y en los
[servicios](formularios-servicios.md#crear-desde-una-propuesta-del-asistente).

- **Quién y cómo.** En Inicio, quien administra catálogos (`formcatalogs.read` y `formcatalogs.manage`) adjunta con el clip
  una planilla por mensaje, con un pedido escrito o dictado; sin texto, la IA pregunta qué hacer con ella. Inicio vacío le
  suma «Armame un catálogo con una planilla», que deja ese pedido en el editor y abre el selector de archivos. Por
  WhatsApp, en la conversación del Asistente automático de una cuenta vinculada y activa con ese permiso, la planilla llega
  como documento junto con su pedido: el `caption`, o el texto o el audio contiguo. Sola no se interpreta, salvo que
  responda a «Cambiar algo» o a una pregunta. Se lee la primera hoja de un archivo de hasta 10 MB, 10.000 filas y 64
  columnas. Un archivo que no se lee no llega a la IA, y se dice el motivo con su remedio (en Inicio, debajo del editor; por
  WhatsApp, en un texto que pide mandarla de nuevo junto con el pedido): un Excel viejo (`.xls`), uno con contraseña, uno
  con macros o binario (`.xlsm` o `.xlsb`), uno que pasa los topes, uno ilegible, como un CSV que no está en UTF-8, que hay
  que guardar como «CSV UTF-8», o uno sin filas con datos. Los PDF, las fotos y los Word son de la Etapa 3.
- **Lo que propone la IA.** Con una planilla, `forms.authoring.propose-catalog@2` propone el catálogo con el mapeo: si la
  primera fila con contenido es de encabezados y de qué columna salen el código, la opción, el valor (si el tipo no es
  texto), el detalle y cada campo. El código y la opción pueden salir de la misma columna, para una lista simple; sin
  planilla, el mapeo va vacío. `forms.authoring.propose-import@1`, sólo con una planilla, propone importarla en un catálogo
  Manual que existe y que la persona puede leer y administrar, con el mismo mapeo hacia sus columnas y sus campos
  obligatorios. Las dos se reemplazan entre sí, como dos propuestas de la misma capacidad: una importación pedida después de
  un catálogo propuesto lo reemplaza, y al revés. Inicio ofrece la versión 2 en lugar de la 1, y la importación sólo en un
  turno con planilla: con todos los permisos, 12 de las 16 definiciones sin planilla y 13 con ella; el Asistente automático,
  15 y 16; con un documento, Inicio 12 y el automático 11. La versión 1 sigue registrada: sus turnos se leen y sus tarjetas y resúmenes crean.
- **Lo que muestran.** En Inicio, la tarjeta del catálogo (IA1-A) suma el archivo y sus filas, de qué columna sale cada parte
  («Código · columna A») y hasta tres filas de muestra ordenadas como el catálogo, con los encabezados de la planilla o, sin
  ellos, «Columna A». La de importar es la tarjeta «Importación»: el catálogo destino con su tipo y las filas, de qué columna
  sale cada parte, la muestra y «Revisar la importación», que no crea nada. Por WhatsApp, el
  [resumen](whatsapp.md#ofrecimientos-automáticos--etapa-5) de un catálogo (IA3-A) suma «Son N filas.», y el de importar dice
  el catálogo, de qué columna sale cada parte y las filas, con «Revisar importación», que contesta con el enlace a la
  importación en la app, y «Cambiar algo». Si la IA pregunta con una planilla en la conversación, el canal contesta «¿Qué
  querés que haga con esta planilla? …».
- **Crear e importar.** «Crear catálogo» lo deja inactivo y pendiente, como en la Etapa 1, y abre «Importar opciones» con
  la planilla del chat ya revisada (IA2), sin subirla de nuevo: importar sigue siendo de la persona, con la vista previa y
  las confirmaciones de la [importación](#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22). Importar en uno que existe
  reemplaza la lista, como subir un Excel (decidido el 2026-11-09): lo que no viene queda inactivo, sin borrarse. Mientras un
  catálogo creado desde una propuesta con planilla, también por WhatsApp, siga pendiente de aprobar y sin opciones, y la
  propuesta esté disponible, quien lo pidió tiene su importación pendiente. El diálogo se abre de tres maneras: por su
  cuenta al entrar a ese catálogo, con «Revisar la importación» de Inicio y con el enlace de WhatsApp
  (`/catalogos/{id}?pedido=…&propuesta=…`). En el diálogo, «Desde» suma «La planilla del chat», que abre elegida y ya
  revisada. Si la planilla del chat ya no está (`ProposalUnavailable` o `SheetUnavailable`), o la propuesta cambió
  (`ProposalReplaced`), el diálogo lo dice y pide el archivo. La planilla nunca queda con el catálogo: su evidencia guarda el
  pedido, sin mapeo ni filas.
- **Lo que no hace la IA.** No importa, no activa y no cambia los campos de un catálogo que existe: sobre uno que existe,
  sólo propone importar la planilla.
- **Las reglas de la planilla.** El encabezado es la primera fila con contenido, no la fila 1 de la hoja: las filas vacías
  se saltean igual en la tarjeta, en lo que ve la IA y al importar desde el chat. La IA la recibe como datos, no como
  instrucciones. Cada conversación usa la última planilla: una nueva reemplaza a la anterior, y los turnos siguientes la
  heredan sin adjuntarla de nuevo (en Inicio, los que siguen; por WhatsApp, «Cambiar algo» y la respuesta a una pregunta).
- **La evidencia.** La calificación con el proveedor real quedó en verde el 2026-11-11: 30 casos con planillas en Inicio y
  por WhatsApp, más los sets cuyo catálogo cambió ([configurar IA](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles)).

## La IA arma formularios con un documento — Etapa 3, `propose-form@2` (2026-11-12)

La parte de la Etapa 3 que arma con lo leído de una foto, un PDF o un Word; lo técnico, en los
[servicios](formularios-servicios.md#crear-desde-una-propuesta-del-asistente). Lo que hay hoy es la capacidad y crear desde ella
(tarea 12), e Inicio, que la ofrece con un documento desde que su tarjeta la dibuja (tarea 13); DOC6 es la tarea 14 y WhatsApp
recibe documentos con las tareas 8 y 9.

- **La propuesta.** `forms.authoring.propose-form@2` es el formulario de la versión 1 y, sólo con un documento en el turno,
  la lectura prendida (`documentReading` con «Cómo reconocerlo», hasta 1.000 caracteres) y lo leído por pregunta (`sample`,
  en una línea, sin caracteres invisibles y hasta 160 caracteres contados como grafemas, para comparar en DOC6); sin documento,
  cualquiera de los dos vuelve `rejected` con `noDocument`. La lectura se prende sólo si el documento es un ejemplo de los que
  va a recibir el formulario, nunca con una planilla en blanco que describe preguntas, y «Cómo reconocerlo» describe ese tipo de
  documento en general, sin copiar nombres, números, cuentas, montos ni instrucciones escritas en él. `standard` (`key`) nombra
  un estándar parecido de la biblioteca: sólo uno que mostró `forms.library.standards@1` en el mismo turno (si no, `notFound`),
  con el nombre que mostró. Si la propuesta no entra en 16 KiB pero el formulario sin las muestras sí, el rechazo nombra las
  muestras, para que se saquen ellas y no preguntas.
- **Los estándares.** `forms.library.standards@1` (lectura con `formtemplates.read`, sólo con un documento) da los estándares
  de la biblioteca que leen documentos, con su nombre y descripción en el idioma de la conversación y lo que lee cada uno.
  Inicio no la ofrece; la ofrece el canal de documentos de [DOC6](#armar-formulario-con-la-ia-desde-la-bandeja--doc6-2026-11-12).
- **Quién la ofrece hoy.** Inicio (desde la tarea 13) y el Asistente automático de WhatsApp ofrecen la 2 en lugar de la 1,
  con las mismas definiciones; mientras WhatsApp no reciba documentos propone lo mismo que la 1, y su resumen (IA3-A) es el de
  la 1. La 1 sigue registrada: sus turnos se leen y sus tarjetas y resúmenes crean, y las dos se reemplazan entre sí como una
  capacidad.
- **Crear.** «Crear borrador», en Inicio y por WhatsApp, acepta las dos versiones (DOC6 crea con la misma regla): la 2 crea
  el formulario de la 1 con la lectura prendida y su «Cómo reconocerlo», sin cruces. Las muestras y el estándar nunca se
  guardan, y la evidencia del borrador sólo dice que la propuesta traía una lectura: el texto queda en el borrador, que se edita.

## Armar formulario con la IA desde la bandeja — DOC6, 2026-11-12

Para los documentos que no encajan en ningún formulario (DOC6, decisión 17 de [documentos por WhatsApp](../plans/2026-10-22-documentos-por-whatsapp.md));
lo técnico, en los [servicios](formularios-servicios.md#crear-desde-una-propuesta-del-asistente). Pide «Ver documentos recibidos»,
«Revisar documentos» y «Diseñar formularios» (`formtemplates.read` y `formtemplates.manage`): sus respuestas llevan documentos de la bandeja
y lo que se leyó en ellos. Está el backend (tarea 14); la página llega con su front.

- **El aviso de los parecidos.** La página de la bandeja (`POST api/forms/documents/search`) suma `groups`: los temas pendientes de
  «Formularios que faltan» que comparten tres o más documentos sin clasificar (los que dejó «Otro»), con todos los filtros de la
  página salvo el estado, los más grandes primero y hasta cinco: `{ topic: { id, name }, count, documentIds }`, con el nombre como
  se anotó y los más nuevos primero (hasta 20). No cuentan los documentos de un borrador ya armado ni el tema para el que se armó,
  mientras ese borrador exista: si se elimina, vuelven. Sale en una sola consulta con la página.
- **Pedir la propuesta.** `POST api/forms/documents/form-proposals` con `requestId` y `documentId` (uno sin clasificar, con su
  lectura y su copia) o `topicId` (sus sin clasificar que no son de un borrador, los más nuevos primero, hasta 20), nunca los dos. Es
  una interpretación del canal `forms-documents` con quien pide como Actor, sin Subject y en una conversación propia del pedido, que
  ofrece `forms.authoring.forms@1`, `forms.authoring.catalogs@1`, `forms.library.standards@1` y `forms.authoring.propose-form@2` y
  cierra con `assistant_proposal@1`: cinco definiciones de dieciséis. Su documento son las lecturas de hasta cinco: la primera con su
  copia y la mitad de los 16 KiB, las otras se reparten el resto, cada una recortada por el servidor y la que no entra, afuera. Los
  cupos son los del motor. 202 con el pedido; el mismo pedido otra vez contesta lo mismo sin pedirlo de nuevo y otro documento o tema
  con el mismo `requestId` da 409 `RequestConflict`. Lo que el motor ya tomó con ese `requestId` sin que se guardara el pedido (otro
  intento que llegó antes, o uno cuyo guardado falló) se recupera sin correr dos veces: se reenvía sellado en el mismo instante, el
  motor lo reconoce y el pedido se guarda; con otros documentos (u otro contexto que la búsqueda encuentra), 409 `RequestConflict`. Un
  conflicto del motor que no es ese UUID (el asistente pausado, sin configurar o con el modelo sin calificar) es su falla. Un documento que no está da 404; uno que no está sin clasificar, 409
  `StateConflict`; sin lectura, 409 `ReadingUnavailable`; un tema sin documentos que se puedan leer, 409 `GroupUnavailable`. Si el
  motor no lo toma no se guarda nada: 403 si se niega, 429 con el código, el texto y el `retryAfter` del motor si se agotó su cupo o su
  capacidad (como las otras rutas del asistente) y 500 `ProposalUnavailable` si falla.
- **Esperarla.** `GET api/forms/documents/form-proposals/{requestId}`, sólo para quien la pidió (si no, 404 `ProposalNotFound`), se
  lee con el contexto sellado: `state` (`pending`, `completed` o `failed` con su `failureCode`: `Forms.ReceivedDocument.NoFormProposal`,
  `Forms.ReceivedDocument.ProposalNotFound` o el código del motor tal cual, que la página muestra como una falla general), `topic`, `documents` como los lista
  la bandeja y en el orden en que se adjuntaron, `proposal` (`{ id, capability, version, content }`, la de `propose-form@2` como la
  muestra Inicio: el formulario, lo leído en el primero por pregunta, la lectura y un estándar parecido) y el `definitionId` del
  borrador que creó. Se puede salir y volver.
- **Crear el borrador.** `POST api/forms/definitions/document-proposals` con `requestId`, `proposalId` y el formulario como quedó:
  `name`, `prefix`, `submitLabel`, `recognition` (obligatorio: la lectura va prendida) y `questions` (`label`, el `type` de la
  propuesta, `required`, `options` y `catalogId`). La propuesta tiene que ser la que cerró su operación (si no, 404
  `Forms.ReceivedDocument.ProposalNotFound`, como un pedido que no está). Lo editado se valida como el borrador (`SaveFormDraftRequestValidator`) y contesta con
  las rutas del cuerpo (`name`, `prefix`, `submitLabel`, `recognition`, `questions[i]…`), sólo con catálogos activos que se puedan
  leer y un nombre libre. Lo crea el guardado del editor con la lectura prendida y sin cruces, la evidencia como la de Inicio
  (`requestedIn: "documents"`, el formulario como se propuso, nunca sus muestras ni el estándar) y, en ese mismo guardado, el borrador
  que creó el pedido: sus documentos son los de origen. 201 con `{ proposalId, definitionId }`; el mismo pedido otra vez da el mismo
  borrador y la propuesta de la que salió, diga lo que diga el cuerpo.
- **Al publicar.** Un worker (`BusinessForms:DocumentRereadWorker`, cada 5 s) toma los pedidos cuyo borrador tiene una versión activa
  publicada en un canal con la lectura prendida y no releída, fuera de la UoW de la publicación: para esa versión todos sus documentos
  quedan pendientes, y cada uno sale cuando se pide su lectura o deja de necesitarla (ya no está sin clasificar o el formulario no
  está). Cada vuelta toma los pendientes por dos minutos (otra copia del servidor no los repite) y vuelve a leer cada uno para ese
  formulario, una interpretación de `forms-documents` por documento con el formulario fijo y el contexto de quien lo pidió; al
  terminar queda «Propuesto», o como estaba si la lectura falla. Si el motor no puede tomarlo ahora (su cupo o su capacidad), los
  pendientes esperan lo que dice —el cupo del día, hasta el otro día—, y si el asistente no está disponible por ahora (pausado, sin
  configurar o sin calificar), un minuto; una vuelta posterior sigue, y un paso que falla a mitad deja el documento pendiente para la
  vuelta siguiente, hasta tres veces seguidas: a la tercera sale de los pendientes (anotado con su código), así no retiene para siempre
  su pedido ni los demás de esa persona. Las relecturas usan el cupo diario de quien pidió en el canal: sus pedidos releen entre todos a lo sumo unos
  `BusinessForms:DocumentReread:DailyPerAsker` por día UTC (de 1 a 100, 5 por defecto: la mitad del cupo del motor; sólo cuenta lo que el
  motor tomó), de a una vuelta por persona (una versión nueva espera la vuelta que retiene los pendientes, y cada vuelta ni lista los pedidos de
  quien ya tiene otro retenido), y el resto sigue al otro día, así su bandeja conserva lo demás. La cuenta es aproximada: una vuelta que tarda más que su retención puede cruzarse con la siguiente, y
  una lectura que el motor tomó no cuenta si su paso se corta antes de anotarla o si su guardado pierde con un cambio del documento. Una versión nueva relee los que siguen sin clasificar. Cada documento es su propia
  conversación del canal, también en el cambio de formulario de quien revisa, así que las lecturas de documentos distintos no se
  esperan entre sí.
- **Persistencia.** `FormDocumentProposals`, único por quien pidió y `requestId`: el contexto, el tema, los documentos (`uuid[]`), el
  borrador y la propuesta de la que salió, la versión releída, sus pendientes (`uuid[]`), hasta cuándo esperan y las relecturas del día;
  migración `AddFormDocumentProposals`.

## Formularios que faltan — Etapa 5

La misma lectura de formularios incluye temas generalizados existentes. Una intención clara de registrar algo sin formulario accesible puede proponer uno de esos temas o un nombre y frase en el idioma de la conversación; una consulta, un cupo agotado o el rechazo de un formulario conocido no son pedidos faltantes. El motor no guarda: BusinessForms valida la propuesta y registra un recibo estable del mensaje original fuera de la transacción del canal. El nombre y la descripción generalizados se inspeccionan antes de devolver la propuesta y otra vez al capturarla, antes y dentro del lock. La copia de inspección usa NFKC para detectar también `＠`, direcciones con esquema o `www` y secuencias de siete o más dígitos, incluso separadas como un teléfono con guiones Unicode o signo menos. Se admiten fechas ISO válidas y montos con moneda explícita sólo si cubren el tramo numérico entero. Los identificadores conocidos del actor y destinatario se comparan como palabras completas y sin distinguir tildes, sin confundir «Ana» con «mañana». La inspección no reescribe el texto guardado ni filtra el relato de una visita o su prellenado.

Los pedidos se agrupan por tema, con cantidades de pedidos y personas distintas y su última fecha. El tablero y descarte requieren sólo `formtemplates.manage`; no entregan nombres de quienes piden, mensajes, teléfonos ni valores de respuestas. Descartar conserva autor y fecha con revisión esperada; un pedido nuevo vuelve el tema a pendientes. Reinterpretar el mismo mensaje no cuenta otra vez ni reabre un descarte. Crear un formulario abre la autoría normal con el nombre elegido y no resuelve el tema.


## Cierre local de la Etapa 5 y del review de la Etapa 4

El `2026-10-18`, sobre backend `59c03587` y front `7ecabd6d`,
se verificaron los 18 arreglos del review, D2 y H1–H7/s1–s4. Backend `4.480/4.480 tests`
con PostgreSQL/Redis/Azurite y cero omitidos; build sin errores ni advertencias. Contratos HTTP
19 cohortes, 295 capturas —64 de WhatsApp— y 53 fuentes, con bytes y SHA idénticos al front; front 2.245/2.245 tests en 227 archivos, build sin errores ni advertencias y lint con 49/49 checks de arquitectura; visual 511/511 escenas en la pasada normal definitiva.
El renderer PDF y emoji pasó 17/17 checks (10 del renderer y 7 de emoji), seis PDF y nueve páginas A4 inspeccionadas en Linux, con sus dependencias Skia/HarfBuzz.
El navegador acreditó sólo Chrome e IAB: ingreso normal con código, D2 en planilla y Por aprobar, filtros/foco/URL/flechas/Escape, decisión con otra cuenta, PDF fijo con foto abierto y descargado, vista previa con respaldo completo al original, revisión manual, continuación Siempre, catálogo aprobado, tablero de pedidos faltantes y audio con permiso de lectura; móvil de 390×844. Los hosts de prueba quedaron detenidos.

La [calificación OpenAI](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) es independiente
del [piloto físico con Meta e IA](#piloto-del-asistente-y-audios--2026-10-18).
El plan y el review se retiraron con [procedencia](../history/README.md#formularios-etapa-5-y-review-de-la-etapa-4--2026-10-18).
En ese cierre, la publicación de preguntas y los catálogos de las Etapas 1–3 seguían exigiendo ambos idiomas.
La regla posterior de [original obligatorio y traducción opcional](#idioma-original-y-traducción-opcional--2026-10-19) sustituye esa exigencia de publicación y se extiende a los catálogos y a los datos de las personas.

## Piloto del asistente y audios — 2026-10-18

Con Aspire, túnel, Meta, OpenAI y Azure privado reales, **Registrar visita**, versión 2 (`flow-static-7.3-r8`), recibió texto y audio: la persona confirmó el prellenado y envió la foto (`VISITA-000002/000003`); se comprobaron la transcripción original completa y el motivo en chat/detalle. El pedido sin formulario produjo aviso y un tema en Formularios que faltan; la sugerencia en Manual se mandó con Revisar y enviar (`VISITA-000004`).

Con la cuenta en inglés y traducción completa, formulario, avisos y PDF salieron en inglés (`VISITA-000005`, reglas revisión 2 y PDF revisión 1). Al quitar solo el nombre inglés del PDF, los avisos y el PDF volvieron enteros al español, pero el formulario y su apertura siguieron en inglés (`VISITA-000006`, PDF revisión 2): el respaldo de todo el recorrido queda [pendiente](../plans/verificacion-y-produccion.md#formularios-piloto-del-asistente-y-audios). Otra cuenta con permiso de decidir aprobó ambas visitas; la persona abrió los dos PDF con foto en el celular y el historial mostró las acciones en Hecho y los avisos entregados. El formulario no tenía opciones y no se probó una continuación tras responder.

Sin correcciones de código durante el piloto. Quedaron restaurados español en la cuenta, el título inglés del PDF (revisión 3) y el modo original Manual; se conservaron la publicación y los avisos traducidos. Aspire y el túnel quedaron detenidos con `aspire stop`.

La corrección del review de Etapa 5 introdujo una cultura completa común antes de preparar cualquier envío, también manual y sin catálogos. La [regla vigente](#idioma-original-y-traducción-opcional--2026-10-19) incorpora las culturas publicadas de la versión y exceptúa el texto/detalle de ítem que usa su original individual. La respuesta guarda la cultura de su sesión para que el autor no pase después del original al alternativo por cambios en cuenta o traducciones. Cada destinatario del rol mantiene su propio idioma cuando está completo. La revalidación posterior puede volver al original ante nuevos faltantes; no reescribe el Flow ya entregado ni las ejecuciones aceptadas. El piloto físico anterior conserva su resultado histórico; comprobar de nuevo el recorrido completo en Meta sigue pendiente.

## Cierre del review de la Etapa 5 — 2026-10-19

El [review corregido](../reviews/2026-10-18-code-review-etapa-5.md#cierre--2026-10-19) registra la verificación completa con Docker, contratos HTTP, front y capturas, más el recorrido IAB de D2, decisiones con otra cuenta, revisión manual, Always, audio y PDF real con foto. La [calificación de canales](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles) usa la misma conexión y perfil OpenAI; Inicio y STT conservan la evidencia previa.

El navegador usó canales controlados. La integración cubre el idioma completo y el respaldo común ante traducciones faltantes; repetir ese recorrido con formulario, avisos y PDF en Meta permanece [pendiente](../plans/verificacion-y-produccion.md#formularios-piloto-del-asistente-y-audios).

## Verificación local de idioma original y traducción opcional — 2026-10-19

El cierre local verifica I1–I3 y la [regla vigente](#idioma-original-y-traducción-opcional--2026-10-19). Backend sobre `59b527723664daded31cec0f6702c375eb10b2f7`: build sin errores ni advertencias y suite completa con PostgreSQL/Redis/Azurite, **4.765/4.765 tests**, cero fallos y omitidos. La integración recorre original español → suma de inglés → pérdida de inglés con cuenta en inglés: apertura predeterminada, CTA, sesión y captura siguen es/en/es; las respuestas de sesiones anteriores se capturan después de activar la tercera versión y conservan bindings, hashes y bytes sellados. Los casos con faltante exclusivo de aviso o pie PDF respaldan todo el recorrido al original; el texto/detalle de un ítem sin inglés conserva su original individual.

Se exportaron **19 cohortes, 318 capturas y 54 fuentes**, incluida forms-http con **46 cuerpos** y las dieciocho comprobaciones/estados/detalles de idioma. Los cuerpos y su procedencia se incorporaron al front en `086d6ae`, con bytes y SHA preservados. Front: **79/79 consumidores**, build y lint con **49/49 checks de arquitectura** en verde; suite completa **2.355/2.355 tests en 228 archivos**, cero fallos y omitidos. Visual sobre `bf626a0`: **527/527 escenas**, cero fallos y omitidos, en 6,2 min; configuración y tolerancias intactas, con 127 referencias SHA idénticas y sin actualizar snapshots. Se inspeccionaron cuarenta PNG de la corrida.

El navegador recorrió I1–I3 en español e inglés a 390 y 320 px, con ingreso normal por código y API real del AppPreview: original obligatorio, traducción opcional, vista previa entera y publicación de las tres versiones. No hubo errores de consola; se conservaron doce JPEG revisados en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md), commit `0eb600c`. Los hosts de prueba quedaron detenidos y retiraron su estado temporal; la comprobación final con `aspire stop` no encontró un AppHost corriendo.

El [piloto físico agrupado con Meta](../plans/verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional) permanece abierto: original español, inglés completo y faltante exclusivo de aviso o PDF (P24). Este cierre usa proveedores controlados y no agrega una calificación OpenAI ni evidencia física nueva. El plan se retiró con [procedencia](../history/README.md#formularios-idioma-original-y-traducción-opcional--2026-10-19).

## Revisión del cierre de formularios — 2026-10-19

El [review del cierre](../reviews/2026-10-19-code-review-cierre-formularios.md) registra las correcciones de idioma, respuestas históricas, asistente y audios, con regresiones y un recorrido local de navegador sobre la SPA de producción y API de prueba con proveedores controlados. Las decisiones vigentes no cambian; la verificación completa y los contratos HTTP exportados quedan registrados en ese review. No acredita un piloto físico con Meta ni una nueva calificación LLM.

## Dar de baja, reactivar y eliminar — 2026-10-21

Dar de baja exige Read y Publish, junto con la revisión esperada de la definición. Conserva la versión activa y registra quién y cuándo; una publicación abierta responde 409. El selector, el asistente y las sugerencias comparten un único filtro de disponibilidad: no ofrecen definiciones dadas de baja ni eliminadas. Si alguien pide una de ellas al asistente, se registra en Formularios que faltan como un formulario inexistente. Los envíos ya abiertos conservan su versión sellada y pueden responder hasta su vencimiento; las respuestas, archivos, numeración, aprobaciones y reglas existentes siguen disponibles.

Reactivar usa la publicación y su recuperación habituales con el borrador actual: crea una versión nueva y Flows nuevos. La definición sigue dada de baja durante esa publicación y sólo vuelve a activa cuando se confirma la versión nueva; no se puede reactivar confirmando otra vez la misma versión. La numeración continúa.

Eliminar exige Read y Manage, sin exigir Publish. Es borrado lógico, sólo sin respuestas, sin envíos abiertos ni respuestas oportunas todavía en procesamiento, y sin publicación en curso. La revisión se comprueba bajo el mismo lock que las otras mutaciones; no elimina versiones, recibos ni evidencias del canal. El prefijo queda reservado y la recuperación de un guardado previo sigue viendo la definición eliminada, sin resucitarla. El diálogo identifica ese prefijo; la advertencia irreversible de WhatsApp sólo aparece si hubo publicación.

El listado de Diseñar formularios filtra Activos, Publicados, Borradores o Dados de baja; Activos reúne publicados y borradores, excluyendo los dados de baja. La tabla conserva su desplazamiento horizontal contenido en el celular. Listado y editor comparten las acciones según permiso y estado. Sin RecordsRead, cantidad de respuestas, pendientes y próximo número son null; los permisos de administrar o publicar no revelan esos datos. Eliminar no aparece cuando hay respuestas; un menú del editor que ya tiene otras acciones explica ese impedimento como texto no accionable. La metadata de ciclo de vida no participa en el borrador, su huella ni sus comprobantes de guardado. Un resultado de baja o eliminación no confirmado se consulta antes de volver a intentar, sin pisar cambios locales. Los motivos de las acciones bloqueadas se alcanzan con las flechas del menú y no ejecutan la acción al activarlos.

El diálogo de baja enumera las sugerencias entrantes de conjuntos activos que dejarán de ofrecerse y explica los envíos abiertos, la conservación de respuestas y la deprecación irreversible en WhatsApp cuando corresponda. El editor de sugerencias conserva los destinos dados de baja o eliminados como no disponibles: sólo permite quitarlos y no guarda mientras quede alguno. No altera silenciosamente la configuración.

BusinessForms declara, por un puerto neutral, qué pares exactos definición/versión ya no se usan: versiones propias de publicaciones terminadas que fueron reemplazadas, dadas de baja o eliminadas. Una publicación abierta de la definición suspende esa autorización. WhatsApp mantiene la operación durable y devuelve por otro puerto, sólo de lectura, el último vencimiento y Pending, Uncertain, NeedsAttention o Deprecated con su fecha confirmada; BusinessForms no escribe en WhatsApp ni llama a Meta. Pending también se informa para formularios activos y no autoriza el retiro. NeedsAttention muestra un problema persistente del canal, con reintentos espaciados; un POST sin confirmar se consulta antes de repetir. El estado no se infiere por ausencia de sesiones. Listado, editor y diálogo muestran esa misma evidencia local.

La entrada unificada MF1 y el detalle completo se rigen por [Gestión de formularios](#gestión-de-formularios-y-detalle-completo--2026-10-21). La verificación física de la baja y reactivación se realiza después del code review y se registra en [verificación y producción](../plans/verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación).

## Verificación local del ciclo de vida — 2026-10-21

La corrida completa posterior al [review 1–15, incluido 3b](../reviews/2026-10-21-code-review-dar-de-baja.md#contraste-y-corrección--2026-10-21), de fuente `49e14c21`, cerró con **5236/5236 pruebas**, sin fallos ni omitidos, y build de solución con **0 advertencias y 0 errores**. La integración con PostgreSQL y Redis en Docker cubre la deprecación incierta consultada antes de repetir, exclusión entre hosts y fence, el envío abierto respondido después de la baja, las versiones reemplazadas y el 409 durante una publicación. Meta está controlado en esas pruebas: no acredita la deprecación en el proveedor real. También incluye las regresiones de selección, conteos y avisos inmediatos; la [medición de latencia local](whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21) conserva su comparación antes/después.

El export conjunto conserva **20 cohortes, 356 capturas HTTP y 63 fuentes únicas**, con bytes y SHA256 verificados por el front; incluye **30 capturas de ciclo de vida**, quince por idioma, con NeedsAttention y su consumo estricto. La procedencia corresponde al run `20261021-141839-b590895e7fee4a4c8bbd4aeecd648066`; los contratos no se completan con valores inventados en los consumidores.

El front cerró con **2511/2511 tests en 237 archivos**, build y lint aprobados, arquitectura **49/49** y **585/585 pruebas visuales**. Las 127 referencias conservaron sus hashes, sin actualizar snapshots ni tolerancias. La matriz es/en a 390/320 y el recorrido interactivo en IAB comprobaron tabla contenida, diálogos, retorno de foco, motivos bloqueados por teclado, NeedsAttention y BA6 sobre componentes productivos y datos sintéticos; los seis casos visuales 429 cubren el espaciado de publicación y reactivación. El [diseño conserva la evidencia](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-baja-reactivación-y-eliminación--2026-10-21). Se ejecutó `aspire stop`, sin AppHost en ejecución. El piloto real con Meta queda pendiente después del code review.

## Gestión de formularios y detalle completo — 2026-10-21

`GET /api/forms/overview` permite leer respuestas o decidir aprobaciones, sin exigir lectura de diseños. Proyecta los formularios publicados no eliminados y usa el nombre de su versión activa sellada, con la misma cultura de contenido de las lecturas históricas; un borrador posterior no cambia ese nombre ni su búsqueda. Resuelve nombre e idiomas en SQL con `DisplayNameEs/En`, cultura original y `PublishedCultures`, sin traer el esquema ni descifrar manifiestos. Las culturas se sellan desde los perfiles verificados al aceptar la publicación; el seeder existente completa sólo la metadata histórica ausente desde su manifiesto protegido, sin reescribir contenido, hash ni operación. La cantidad de respuestas sale de `LastRecordNumber`. La última respuesta es metadata, sin valores ni archivos. Los pendientes sólo cuentan tareas abiertas que esa cuenta activa puede decidir por su rol vigente, excluyendo sus propias respuestas. El índice parcial de tareas abiertas por formulario, rol, fecha e ID sirve al listado y al recorrido de pendientes.

Con sólo lectura, los conteos de aprobación son null. Con sólo decisión, se incluyen únicamente formularios con tareas que puede decidir, y cantidad de respuestas y última respuesta son null; el segmentado conserva Activos o Dados de baja. Con ambos permisos, «Con algo por aprobar» reúne los pendientes, incluidos los de formularios dados de baja: cada uno conserva su etiqueta de baja, los segmentos quedan sin selección y no se repiten sus avisos. El vacío distingue pendientes, dados de baja y una búsqueda sin coincidencias. Las facetas se calculan antes de paginar; el lector ordena primero los que esperan una decisión y después la última respuesta. No hay consultas por fila. La autorización se comprueba de nuevo antes de devolver la proyección.

Overview reemplaza las lecturas `api/forms/navigation` y `approvals/options`; sus rutas, contratos y consumidores se retiran junto con la bandeja global. Se conservan el conteo autorizado del menú, las tareas y sus decisiones por formulario.

MF1 es la entrada «Formularios» del grupo «Gestión de formularios» y permanece activa al recorrer la planilla, D2 o la pantalla completa; Diseñar formularios, Catálogos y Formularios que faltan mantienen sus tareas. Las migas de D2 conservan la jerarquía de la planilla; sólo la pantalla completa agrega la rama de respuesta. Activos y Dados de baja usan sus conteos del servidor y el aviso de decisiones pendientes en los dados de baja. La tabla conserva el desplazamiento horizontal dentro de su caja en el celular. Quien sólo lee no consulta aprobaciones ni muestra su número; quien sólo decide no monta la planilla, las columnas de respuestas ni última respuesta, y tampoco necesita el filtro «Con algo por aprobar».

Con Decide y lectura, la planilla tiene dos solapas (PL1-A, 2026-11-02): «Por aprobar (N)», donde abre si hay algo para decidir, y «Todas»; la solapa va en la dirección (`view`). Perder Decide saca las solapas y deja el aviso sobre la planilla autorizada. Revisar abre D2 con el TaskId de esa cola; las flechas recorren sus pendientes de más viejo a más nuevo. «Aprobar» pide confirmar con el número, quién la cargó y cuándo, y el foco empieza en «Cancelar» (AP1-A, 2026-11-01): así ↓ y un clic de costumbre no deciden la siguiente. La selección guarda también el RecordId y la página observada para conservar el origen al abrir el detalle completo. Las tareas ya capturadas se conservan al decidir o compactarse la lista. Una relectura con offset actualiza la tabla, pero nunca amplía la cola: en su borde se pide continuación desde la tarea ancla, aunque ya esté decidida. Las referencias se combinan por RequestedAtUtc e Id inmutables, conservando la precisión UTC y sin usar la posición actual para ordenarlas. El contexto del recorrido y los filtros privados permanecen en el dueño de memoria del formulario.

Read y Decide conservan límites independientes durante la navegación: un 403 de aprobación retira listas, tareas y su contexto, también una tarea todavía en vuelo, sin retirar una planilla que sigue autorizada. Un 403 de lectura retira valores, archivos y PDF, y permite continuar con metadata de aprobación si Decide sigue vigente. Los datos anteriores no se conservan en caché después de la denegación ni se recuperan desde una respuesta tardía. El registro observado para mantener una lectura todavía autorizada pertenece sólo a la selección actual: se limpia al cerrar o cambiar de cuenta, formulario o tarea, y al perder Read; reabrir una URL no recupera metadata de aprobación retirada.

`POST /api/forms/definitions/{definitionId}/records/{recordId}/navigation` exige lectura y reutiliza el contrato de búsqueda de la planilla. Aplica su mismo filtro y orden; `ROW_NUMBER`, `LAG`, `LEAD` y `COUNT` se calculan en SQL antes de filtrar el ancla. Devuelve como máximo una fila con el registro, sus vecinos, posición desde uno y cantidad total. No transfiere el conjunto de IDs, hidrata valores ni recorre páginas. El SQL sale de la proyección EF del mismo lector; el adaptador local conserva orden y parámetros tipados al volver al pipeline de EF, sin otra implementación del filtro. Un registro ausente, excluido por el filtro o de otro formulario devuelve el mismo 404. Esto permite ubicar un enlace directo fuera de la primera página sin incorporar filtros privados a su dirección.

`GET /api/forms/approvals/{taskId}/continuation` exige Decide y recibe formulario, dirección y límite. Autoriza la tarea ancla con la cuenta y los roles actuales; admite el ancla decidida y excluye tareas propias, ajenas o de un formulario eliminado. Un único snapshot SQL calcula el lote de tareas abiertas, sus posiciones, total y continuidad, sin valores de respuestas. La posición actual sirve sólo para volver a la página; el recorrido conserva su cola capturada. La denegación o el cambio de dueño cancelan y retiran la continuación pendiente.

D2 es la vista rápida de escritorio y ofrece «Abrir en pantalla completa». DC1 tiene su ruta `/formularios/{formId}/respuestas/{recordId}/completa`, para quien lee respuestas y, sobre lo propio, para quien sólo completa ([Etapa 2 de la carga](#lo-propio-de-quien-sólo-carga--etapa-2-2026-11-04)). En el celular, tocar la fila o abrir la dirección de D2 conduce directamente a esa pantalla; quien sólo decide conserva la metadata en D2 y no accede a DC1. Cambiar una vista rápida de escritorio a móvil conserva su recorrido.

«Volver» retorna a la misma fila, página, columnas, filtros y orden en memoria, con foco útil. Al navegar en D2, la selección y la página recordadas siguen al destino; cerrar devuelve el foco y la marca a esa misma fila. Las flechas de la pantalla completa siguen la planilla filtrada o la cola de pendientes capturada, según su origen. Una decisión no pierde un sucesor ya capturado al compactarse la lista. Mientras el router confirma el siguiente registro, el anterior queda ocupado y no admite acciones. El contenido confirmado recibe foco para usar las flechas del teclado también al entrar por un enlace compartido.

La dirección compartible sólo conserva orden, pregunta de orden y versiones; no incluye búsqueda, personas, condiciones, selección de tarea ni pestaña. Una entrada directa limpia el contexto privado antes de consultar y usa el anclaje para ubicar el registro, incluso fuera de la primera página. La vuelta interna conserva su contexto únicamente en el dueño de memoria del mismo formulario y cuenta, sin guardarlo en la dirección, el estado del historial ni storage.

DC1 muestra Respuesta y Actividad en dos secciones plegables a todo el ancho. Plegadas resumen su contenido; Respuesta conserva la versión del formulario. La cabecera muestra el número y el estado, las flechas, conversación, descargas y la decisión mientras espera; el formulario queda en las migas. En el celular, las migas conservan el formulario y el número, y el escritorio mantiene la jerarquía completa. La actividad usa la misma historia y editor de incidencias de D2: recepción y emisor capturados, acciones al recibir, tarea o decisión y efectos posteriores. El menú de descarga incluye la copia fija y los documentos de la respuesta.

La miniatura del PDF aparece en la acción de generación de «Al aprobarla». Actividad plegada al entrar no lee sus bytes. En el celular, el atajo «Ver PDF» de la planilla conserva una intención interna efímera y abre Actividad con la copia visible; una entrada ordinaria o compartida conserva el plegado y la carga diferida. La intención no viaja en URL, historial ni storage y se retira al volver o cambiar de dueño. Una miniatura ya abierta y su visor comparten el blob y la URL privados hasta salir de esa respuesta, cambiar de dueño o perder lectura. Plegar y volver a abrir conserva esa lectura; actualizar sólo la navegación no vuelve a pedir el archivo. El visor ampliado también permite descargar ese mismo blob, sin otra lectura. La pestaña PDF de D2 conserva su apertura explícita.

Cada documento del workflow expone el `RunId` durable que ya guardaba `BusinessFormDocument`: identifica la ejecución que lo generó y coincide con el ID de la acción proyectada en la historia. La relación no se infiere por fecha, cultura ni posición del PDF. No se reescriben las copias históricas ni se agrega otra identidad de documento.

La limpieza de datos de prueba es una herramienta de desarrollo separada del producto. Esta etapa no habilita borrar respuestas desde un formulario ni cambia su retención; copiar definiciones conserva su alcance pendiente.

## Verificación conjunta de menú, detalle y aislamiento — 2026-10-21

Backend sobre `64303a4b`: `dotnet test` completo con PostgreSQL y Redis en Docker, **5.317/5.317 pruebas**, cero fallos y omitidos; build de solución con **0 advertencias y 0 errores**. Incluye las migraciones y sus Down, el alcance de turnos Manual, los tres contactos concurrentes en Asistente y Atención manual, Overview por permisos/cultura, anclaje fuera de página, origen durable del PDF y retirada de las rutas anteriores. La prueba concurrente retiene la primera llamada al proveedor y exige el envío y la confirmación durable de las otras dos antes de liberarla. IA, Meta y archivos están controlados: no acredita un recorrido con proveedores reales.

El export conjunto del run `20261021-215429-cdff15e2b5e048b98305bf815bd9a2e5` conserva **20 cohortes, 371 capturas HTTP y 65 fuentes únicas**, con longitud, bytes y SHA256 verificados. Respuestas reúne **37 cuerpos**, incluidos diez Overview y ocho anclajes nuevos es/en; aprobaciones conserva **11** después de retirar options. Los consumidores estrictos nuevos calificaron los cuerpos originales, sin completar valores ni mantener compatibilidad con las rutas retiradas.

Front: **2.600/2.600 pruebas en 248 archivos**, sin fallos ni omitidos; build y lint completos limpios, arquitectura **49/49**. La suite visual final sobre producto `54cca75` y ese conjunto HTTP pasó **617/617 escenas** en 8,5 minutos. Las **127 referencias** conservaron sus hashes; no se actualizaron snapshots ni se alteraron timeouts, reintentos o tolerancias.

El navegador interactivo recorrió MF1, D2 y DC1 en es/en a 1440/390/320 sobre componentes productivos con transporte sintético: tabla contenida, perfiles Read/Decide, apertura móvil, flechas por origen, retorno y foco en la fila, plegado y PDF compartido. Las [siete capturas del cierre](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#cierre-local-de-menú-y-detalle--2026-10-21) conservan el alcance: el iframe nativo no rasteriza sus páginas en los PNG; se verificaron marco, controles, carga diferida, bytes y reutilización del mismo blob, no los píxeles internos de ese visor.

Los servidores propios y pestañas de prueba se cerraron. Se ejecutó `aspire stop`: no había un AppHost en ejecución. El [plan retirado](../history/README.md#formularios-menú-y-detalle-completo--2026-10-21) conserva su procedencia; el [code review y el piloto real](../plans/verificacion-y-produccion.md#formularios-menú-y-detalle-completo) siguen pendientes. No cambiaron capacidades, esquemas o instrucciones del proveedor de IA y no se repitió su calificación. Todo quedó en main, sin push.

## Verificación del review de menú, detalle y aislamiento — 2026-10-21

Los diez puntos del [review](../reviews/2026-10-21-code-review-menu-y-detalle.md#contraste-y-corrección--2026-10-21) se contrastaron con el código y sus recorridos. Los puntos 2 y 5 reducían costes reales de lectura; el índice no portable y los estados, navegación, selección y acceso móvil tenían regresiones concretas. Se agregaron también las coberturas negativas de Overview y de perfiles del menú, sin modificar «Después, sin bloquear».

Backend sobre `6732fa5d`: `dotnet test` completo con PostgreSQL y Redis en Docker, **5.389/5.389 pruebas**, cero fallos y omitidos; build con **0 advertencias y 0 errores**. El run `20261022-004502-e7394c4e320c48f38f599396d85ba3fe` conserva **20 cohortes, 377 capturas HTTP y 67 fuentes únicas**; longitud, bytes y SHA256 coinciden entre productor y front. Respuestas reúne **37 cuerpos** y aprobaciones **17**, incluidos los seis de continuación por tarea. Los cinco nuevos tests consumidores calificaron las capturas reales sin completar ni adaptar su contenido.

Front: **2.689/2.689 pruebas en 252 archivos**, build productivo y lint limpios, arquitectura **49/49**. La visual sobre `0ad49f3` pasó **623/623 escenas** en 7,7 minutos, con los hashes de las **127 referencias** intactos y los tiempos, reintentos y tolerancias originales. El bundle privado de pruebas agrupa paquetes y shared, conserva las páginas lazy y reduce de 76 a 16 los preloads iniciales; su chunk común genera el aviso visible de Vite por tamaño, efecto revisado del agrupado. El build productivo conserva su validación aparte y quedó limpio.

El [navegador interactivo](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#verificación-interactiva-posterior-al-review-de-menú--2026-10-21) verificó MF1, D2, DC0/DC1, teclado, fila/foco, PDF y perfiles Read/Decide en es/en a 1440/390/320, con transporte sintético. Servidores y pestañas propios cerrados; `aspire stop` confirmó que no había AppHost en ejecución. El review quedó cerrado localmente; el [piloto real](../plans/verificacion-y-produccion.md#formularios-menú-y-detalle-completo) sigue pendiente. No se modificaron capacidades, esquemas o instrucciones de IA ni se repitió su calificación. Main, sin push.


## Catálogos: base de la etapa E1 — 2026-10-21

El catálogo Manual admite hasta **10.000 opciones**, incluidas las inactivas, con el total validado bajo su lock. El PUT conserva el límite de 500 movimientos y 512 KiB de cuerpo. Un código repetido al editar señala `items[i].code`. La unicidad de código queda en una restricción PostgreSQL diferida, fuera del modelo EF igual que la de posición, para permitir un intercambio en el mismo guardado.

Las condiciones leen todas las opciones, incluidas las inactivas, en una consulta ordenada por Position e Id y reutilizan esa lectura entre preguntas del mismo catálogo. La lectura de oferta Manual trae una fila de más para comprobar el tope sin truncar: 201 para elegir una y 21 para elegir varias. El prellenado reconoce los perfiles r8, r9 y r10; las publicaciones nuevas usan r10. Las listas recibidas se validan contra el mapeo sellado antes de interpretarlas como archivos.

En el front, reglas y sugerencias comparten el editor de condiciones; la lectura de opciones de una celda y el rango mínimo/máximo tienen una sola implementación. La tabla y los campos de un ítem del catálogo están separados del dueño de borrador, paginado y foco. Estas extracciones conservan el recorrido y los textos actuales. El [cierre local de E1](#verificación-local-de-catálogos-e1--2026-10-22) distingue sus resultados del [piloto físico pendiente](../plans/verificacion-y-produccion.md#formularios-catálogos-e1).

### Eliminar una opción — 2026-10-29

Una opción Manual que nada usa se puede eliminar; si algo la usa, sólo se da de baja. El PUT recibe `deleteItemIds` (hasta 500 identidades distintas, sin vacías) bajo la misma revisión y Guardar que el resto. Bajo el lock del catálogo, cada identidad debe ser una opción del catálogo (si no, `ItemNotFound`) que no se edite ni se mueva en el mismo pedido (`ContentInvalid` en `deleteItemIds[i]`). Después se leen sus usos y la primera opción usada, en el orden del pedido, da 409 `Forms.Catalog.ItemInUse` en `deleteItemIds[i]` con `uses`, en este orden: `records` (una respuesta la eligió), `options` (el campo de una opción la guarda, en cualquier catálogo y aun en un campo inactivo), `people` (el dato de una persona la guarda, también de una cuenta borrada) y `forms` (la nombra un filtro o una condición del borrador, la versión activa, las reglas activas o las sugerencias activas de un formulario no eliminado). Un formulario eliminado o una versión o revisión anterior no la frenan: un envío abierto de una versión vieja ya rechaza una opción que no está activa. Como la identidad es única, la consulta la busca como valor JSON en esas columnas. Nada se guarda si una falla.

Eliminar es un borrado físico: libera el código, el lugar en las 10.000 y su posición, y las demás opciones cierran el hueco en el mismo guardado. Los campos se revisan contra las opciones que quedan, así que una eliminada no necesita convertir ni ser única. La importación no elimina: Reemplazar sigue dando de baja lo que falta.


## Catálogos con tipo de valor — E1-B, 2026-10-22

Al crear Manual se fija Text (por defecto), Integer, Decimal o Date; el PUT no puede cambiarlo y Filters conserva Text. Las opciones de un catálogo tipado, activas e inactivas, requieren un valor. `FormCatalogValuePolicy` guarda enteros de hasta 18 dígitos, decimales sin redondeo y fechas ISO; los errores se señalan en `items[i].value`. El front convierte el formato local sin pasar por Number y conserva el valor al editar, reordenar o reactivar.

La oferta sella valor/tipo y la captura los copia a la referencia, también en el origen propuesto; sólo se revalida la actividad actual del ítem. Los campos nuevos se omiten al ser null y las sesiones anteriores conservan sus bytes y huellas. Las ofertas incoherentes se rechazan antes de subir archivos y se comprueban nuevamente dentro del límite de captura. El chat, la planilla y el detalle conservan las etiquetas históricas.

Excel agrega «pregunta · valor» numérico o fecha, aun con cero respuestas, con los tipos de catálogos leídos por lote; PDF muestra «texto · valor» localizado, sin redondear el decimal. La prueba física del escritor existente verificó el límite de **15 cifras significativas** de las celdas numéricas al guardar y reabrir: `123456789012345678` pasa a `123456789012346000` y `0.1234567890123456789012345678` a `0.123456789012346`. El canon, la base y la copia sellada conservan el string exacto; no se presenta esa precisión de Excel como exactitud de 18/28 dígitos.


## Carga de catálogos desde una planilla — E1-A, 2026-10-22

Manual permite pegar (Append) o importar XLSX/CSV (Replace) sobre un catálogo guardado y sin cambios. Desde el 2026-10-31 lo pegado se ve en una grilla con las columnas del catálogo en el orden de la plantilla: sin encabezados, las filas van en ese orden; con encabezados (nombre o clave), cada columna va a su lugar y una ajena al catálogo no se manda. La revisión recibe un CSV sólo con las columnas pegadas, así una columna opcional que no se pegó conserva sus valores. Los dos recorridos usan la misma revisión previa del servidor; las filas importadas no entran al borrador del navegador. La vista previa no guarda el archivo ni abre transacción. Aplicar vuelve a leer los mismos bytes, compara SHA-256 y revalida permisos, revisión y plan bajo los locks del catálogo y de las fuentes de sus campos activos, ordenados. El recorder de ítems se comparte con PUT y no guarda: una sola UoW y `MarkImported` avanzan una revisión y conservan hash/UTC de la última planilla. Una revisión obsoleta o un archivo distinto dan 409. El aviso de planilla ya aplicada es informativo y permite volver a aplicarla con la revisión vigente.

La importación interpreta números y fechas de texto con la misma cultura de UI que el editor. Si una entrada admite dos lecturas distintas, devuelve `Forms.CatalogImport.ValueAmbiguous`. Las celdas numéricas y de fecha de XLSX conservan su origen canónico. Exportar escribe el Excel como lo lee una persona (2026-10-31): la columna del valor dice «Valor (tipo)», como la plantilla; entero y decimal van como números de Excel con separador de miles y tantos decimales como tenga el valor, sin coma colgando (`#,##0`, `#,##0.0`, …), y la fecha como fecha de Excel con el formato del idioma del menú (DD/MM/AAAA en español, MM/DD/AAAA en inglés). Una celda numérica guarda 15 cifras significativas: un valor más largo, o una fecha anterior a 1900, va como su texto canónico exacto, que se reimporta sin ambigüedad con el menú en cualquiera de los dos idiomas. La garantía de volver sin cambios es para subir el archivo: pegar celdas copiadas del Excel lee el texto que muestra Excel (con los separadores de la configuración regional) en la cultura del menú, como cualquier texto pegado, así que un decimal entero de miles («1.234» en español) da `Forms.CatalogImport.ValueAmbiguous` y una fecha se lee con el formato del menú del momento. Un export anterior, con «Valor canónico (tipo)», se sigue importando igual; la plantilla para carga humana conserva sus encabezados. La vista previa muestra los valores normalizados de altas y cambios.

Cierre del export legible, sobre `87e0d907`: build de la solución sin advertencias y suite completa con Docker, **6567/6567**, sin fallos ni omitidos.

La identidad es el código sin distinguir mayúsculas. Una columna opcional ausente conserva la traducción o el detalle existentes; una columna presente vacía, también en una fila corta, los limpia. La ausencia de Activa sigue reactivando. Replace inactiva los ausentes, sin borrar, y Append conserva los ausentes; una fila errónea con código existente conserva íntegro ese ítem. Todos los renglones de un código ambiguo se omiten, sin elegir un ganador. Los textos repetidos sólo avisan. El plan conserva IDs y posiciones existentes y agrega en el orden del archivo. Las bajas de más del 25 % de los activos requieren confirmar la cantidad exacta; una planilla sin filas válidas requiere confirmar vacío. Aplicar sin las filas con errores se acepta explícitamente con su botón y, si superan el 10 %, además con la casilla. Listas de vista previa limitadas a 100 con totales completos; los errores se muestran antes que los avisos. Cada incidencia conserva su severidad, encabezado y, cuando hay otra fila del archivo con el mismo texto, su referencia; una coincidencia solo con el catálogo no inventa una fila.

El lector usa el stream del llamador, SHA-256 de los bytes originales, hasta 10 MiB, 64 columnas y 10.000 filas de datos. XLSX valida todas las hojas que ClosedXML carga antes de abrirlo, conserva los números de fila originales y solo devuelve la primera. El presupuesto global es de 10.002 filas físicas y 640.128 celdas de trabajo. Incluye filas y celdas vacías o con estilo, y suma la expansión de fórmulas matriciales por cada hoja cargada aunque compartan su parte interna. Las direcciones dispersas no llenan la grilla. Un rechazo por filas o columnas identifica la hoja real que lo provoca.

CSV es UTF-8 estricto con BOM opcional, detecta coma, punto y coma o tabulación y conserva comillas y saltos dentro de una celda; los números de registro siguen siendo lógicos aunque omita registros vacíos de datos. Un encabezado vacío no promueve filas posteriores. El ZIP se acota a 100 MiB declarados y efectivamente entregados por ZipArchive; la prueba de longitud falsa confirmó que .NET 10 trunca la entrega, sin inflar contenido oculto. No hay extracción, parser ZIP adicional ni persistencia del archivo.

Se valida XML truncado antes de ClosedXML; las fórmulas usan exclusivamente CachedValue, sin recalcular, y una fórmula sin caché es error de celda. El caché numérico de una fórmula se representa con 15 cifras significativas; los textos exactos y los números comunes conservan su tratamiento. Las excepciones no cancelación al abrir ClosedXML se traducen a FileUnreadable; el preflight y el mapeo propio quedan fuera de esa captura general.

Exportar y bajar plantilla exigen CatalogsRead; importar, pegar y editar suman CatalogsManage. Preview, Apply y exportación comparten `forms-export`: una operación concurrente por actor y proceso, sin cola; no es una exclusión global entre actores o réplicas. Los POST multipart declaran su 400 explícitamente, porque la convención general no lo deduce de `[FromForm]`. La plantilla y el lector comparten columnas/reglas es/en. La exportación conserva todas las opciones, incluidas inactivas, y se reimporta sin diferencias. Plantilla y exportación traen las columnas de traducción sólo con el menú en el otro idioma; con el menú en el idioma original del catálogo no aparecen y, al reimportar, la columna ausente conserva la traducción (2026-10-28). El archivo se llama como el catálogo en el idioma del menú (o, si falta, en el otro), con un guion en lugar de los caracteres que un nombre de archivo no admite; la plantilla agrega « - plantilla» o « - template». El Excel de respuestas conserva la representación numérica documentada en E1-B. «Ordenar por…» viaja en el guardado como `sort`, con `column` (`text`, `code` o el id de un campo activo) y `descending`, y no se combina con movimientos. Ordena todas las opciones, también las dadas de baja, con los valores que quedan guardados: los empates conservan el orden de antes, lo vacío va al final en los dos sentidos, los números de un texto o un código se comparan por su valor (117 antes que 10030), cada campo compara en su tipo y uno de otro catálogo, por el texto de su opción (2026-11-11).

CA1-A conserva la carga de una opción, suma edición en la tabla de escritorio, búsqueda/páginas, selección y baja/reactivación sólo de la página; en celular mantiene tabla contenida y editor por opción. La simulación de WhatsApp ilustra esa página y explica el límite de opciones activas, sin decidir la capacidad real. Duplicados y generación de códigos se calculan sobre lo conocido del borrador; las advertencias de texto agrupan una sola vez. CA2 conserva el archivo sólo en el diálogo, cancela lecturas al salir o perder alcance y permite Revisar de nuevo tras un 409; no usa MutationCache.


## Elegir varias: base y captura — E1-C1, 2026-10-22

MultiChoice es un tipo nuevo. Su rango requiere 0 ≤ mínimo ≤ máximo ≤ 20, máximo mayor que cero y Required si y sólo si mínimo > 0; con opciones propias hay de 1 a 20 y el mínimo no puede superar su cantidad. Cambiar desde elección simple crea otra identidad; cambiar el rango la conserva. Selection se omite cuando es null en los contratos canónicos y neutrales, sin modificar las huellas anteriores. Guardado, chequeo y aceptación HTTP conservan el mismo rango; un catálogo Manual con más de 20 opciones activas bloquea el chequeo mediante un COUNT SQL por catálogo compartido.

La captura valida GUID D distintos y ofrecidos, mínimo y máximo, sin ordenar ni deduplicar. ValuesJson conserva un array por pregunta y cada selección una fila con Position 0–19; las referencias de catálogo se identifican por ítem y sellan su presentación, valor y tipo. Omitir una pregunta opcional no agrega entrada; [] explícito se conserva sin filas de valor. Sólo las opciones Manual elegidas revalidan actividad bajo los locks existentes. Huella y replay mantienen el recorrido anterior, también con archivos.

La migración asigna Position 0 a valores existentes, cambia las claves únicas a respuesta/pregunta/posición y respuesta/pregunta/ítem e impide Down cuando hay identidades multiChoice publicadas o en el borrador, aun sin versión o con activa escalar, posiciones adicionales o referencias múltiples. El guard se ejecuta con locks de definiciones y hechos antes de cualquier DDL, sin modificar esquemas ni copias selladas.


## Elegir varias: lecturas — E1-C2, 2026-10-22

Planilla, detalle y chat proyectan una lista de opciones desde ValuesJson, en el orden recibido y con las referencias históricas de cada ítem. No consultan el catálogo actual. La celda opcional vacía es Unanswered y una versión sin la pregunta es NotAsked; los filtros conservan la inclusión histórica de NotAsked dentro de unanswered. MultiChoice usa anyOf, no admite ordenar esa columna y las facetas cuentan respuestas mediante EXISTS, sin multiplicar una respuesta por sus selecciones. Reglas y sugerencias comparten las opciones de condiciones y evalúan la intersección.

Excel une las etiquetas con «; » y conserva la columna adicional de valor cuando el catálogo es tipado. Para varias, esa columna contiene texto canónico unido en el mismo orden, también con una sola selección: conserva enteros de 18 dígitos y decimales de 28 sin pasar por la precisión numérica de Excel. La elección simple mantiene número o fecha. PDF presenta cada etiqueta con su valor localizado; los avisos usan las etiquetas. La vista previa genera un array de ejemplo válido para opciones propias. Las preguntas múltiples quedan fuera del prellenado y los mapeos del asistente, conservando candidatos mixtos; sus capacidades, esquemas e instrucciones no cambiaron.


## Elegir varias en WhatsApp — E1-C3, 2026-10-22

Las publicaciones nuevas usaron flow-static-7.3-r9 y codec 1 (desde los renglones, r10, con los mismos bytes sin bloque, y desde elegir primero por un campo, r11, con los mismos bytes sin esa pregunta). MultiChoice compila como CheckboxGroup, con 1–20 opciones, mínimo/máximo sellados, etiqueta de hasta 30 caracteres y ayuda de hasta 300; sin ayuda muestra «Elegí hasta N» o «Choose up to N». El presupuesto contempla el array completo y señala selection.maxSelected si debe reducirse. La huella incorpora Selection sólo cuando existe y rechaza un replay con otro rango. Mapping y proyección la omiten al ser null; las publicaciones anteriores conservan sus huellas, JSON y bindings. El formulario escalar de referencia mantiene los mismos bytes r8/r9, verificados contra ambos archivos; la copia r8 de integración pertenece a su proyecto y conserva sus bytes originales.

El prellenado sigue admitiendo de r8 a r12 para escalares; varias no declara init-values ni acepta una clave de prellenado. La recepción clasifica arrays por el mapping: varias exige GUID D distintos, ofrecidos y dentro del rango, conserva el orden y distingue [] explícito de omisión. Sólo foto/documento pasan a la descarga de medios, después de validar todas las elecciones. Veinte selecciones no consumen el tope de diez archivos. El contador preliminar omite arrays de strings; un [] ambiguo conserva el presupuesto anterior de 64 KiB, pero sin archivos reales no muestra una descarga.

Una oferta de varias bloquea más de 20 opciones sin truncar. El envío también bloquea una oferta no vacía menor que el mínimo; cero opciones conserva el comportamiento previo de obligatoria/opcional. Choice mantiene su límite de 200 y el mínimo dinámico no impide publicar un catálogo que puede completarse después. Una sesión ya sellada conserva su oferta aunque el catálogo crezca por encima del máximo para envíos nuevos.


## Elegir varias en el front — E1-C4, 2026-10-22

El modelo y los guards distinguen multiChoice de la elección simple. Selection conserva mínimo y máximo entre editor, borrador, guardado y publicación; un límite puede estar vacío durante la edición, pero el cuerpo canónico exige los enteros y el rango válido. Opciones propias y catálogo son excluyentes. Activar o desactivar varias en una pregunta confirmada requiere crear otra identidad, también para sus opciones propias; editar sólo el rango conserva la pregunta y sus opciones.

La vista previa conserva las casillas propias y su máximo. Para un catálogo mantiene el aviso de que las opciones se determinan para quien recibe al enviar: no busca ni inventa opciones, ni presenta la muestra como una oferta real. La cultura del contenido y su respaldo siguen la regla común del formulario.

La planilla muestra las etiquetas en su orden y no permite ordenar una columna multiChoice. «Filtros» usa anyOf y su chip; single conserva in. Desde PL1-A (2026-11-02) la planilla deja a la vista la búsqueda, el canal y el estado, y el resto va en «Filtros», que aplica cada cambio enseguida. Desde PL2-A (2026-11-03), adentro de «Filtros» cada pregunta con opciones, Cargó y Versión son un combo donde se elige una: la faceta del servidor da las opciones con su búsqueda y su paginado, sin que se muestren las cantidades, y se fue «Respuesta» (answered, unanswered y notAsked siguen en el contrato, para reglas y sugerencias). La columna de una pregunta de catálogo no trae sus opciones: el front recuerda en memoria el nombre de la elegida, con el filtro, para el chip. Reglas y sugerencias reutilizan el mismo editor de condiciones. Los mapeos excluyen varias como origen y destino, sin cambiar RecordNumber a texto ni una elección simple del mismo catálogo.

Detalle y chat muestran listas históricas por su tipo. Los lectores de opciones y archivos son distintos: una lista de elecciones no abre el visor ni ofrece descargas. Los guards conservan Answered, Unanswered y NotAsked y rechazan un array de archivos en una pregunta de varias. Las lecturas privadas mantienen sus permisos y alcance por actor; una tarjeta sin RecordsRead no muestra valores. Las preguntas múltiples siguen fuera del prellenado y no cambian las capacidades, esquemas o instrucciones de IA.

## Verificación local de catálogos E1 — 2026-10-22

Backend sobre `496ec1e5`: `dotnet test` completo con PostgreSQL, Redis y Azurite en Docker, **5.948/5.948 pruebas**, cero fallos y omitidos; build con **0 advertencias y 0 errores**. Las integraciones externas usaron proveedores controlados. La cobertura incluye intercambio de códigos y capacidad de 10.000 opciones; migraciones y copias selladas; importación XLSX/CSV, conflictos y exportación/reimportación; arrays, orden, replay, filtros y facetas; Excel/PDF; veinte elecciones con foto/documento; sesiones r8 y bloqueo de ofertas nuevas que superan veinte. La prueba de capacidad no acredita un benchmark de memoria o latencia.

La exportación `20261022-182821-262ad02cbf954670aa5846d39f54a352`, generada el **2026-10-22 a las 19:02:20 UTC**, reúne **21 cohortes, 429 capturas HTTP y 78 fuentes únicas** de la misma corrida. Inventarios, longitud, bytes y SHA-256 coinciden entre productor y front; las fuentes no tenían cambios y el histórico Manual permanece intacto. Los cuerpos originales se copiaron completos, sin adaptar su contenido.

Front sobre `f9e1009`, con las capturas de esa corrida: **2.919/2.919 pruebas en 273 archivos**, cero omitidos; build productivo **sin advertencias**, lint limpio y arquitectura **49/49**. Las pruebas visuales pasaron **671/671**, con los hashes de las **127 referencias** intactos. Las 90 capturas de E1 conservaron sus dimensiones: se inspeccionaron individualmente las 48 con bytes distintos y se contrastaron las 42 idénticas con la evidencia previa. El ajuste del test de navegación espera que el enlace de la fila refleje la URL antes de capturarla; conserva las comprobaciones de regreso, foco y selección, sin cambiar el producto.

El [recorrido local del navegador](../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#recorrido-local-de-e1--2026-10-22) conserva **42 JPEG** en es/en a 1440, 390 y 320 px. Después del reinicio se repitió el recorrido a **390 y 320 px** y se renovaron sus **30 capturas móviles**; las doce de escritorio conservan sus bytes. Usa componentes y rutas productivos con datos y transporte sintéticos. Sus escenas son independientes: no acreditan propagación entre ellas, persistencia ni entrega a Meta.

Los servidores y las pestañas propios quedaron cerrados; `aspire stop` confirmó que no había AppHost en ejecución. No cambiaron capacidades, esquemas ni instrucciones de IA; no se repitió su calificación. En ese cierre quedaron pendientes el code review y el [piloto real de E1](../plans/verificacion-y-produccion.md#formularios-catálogos-e1). E2 se implementó después según las [reglas de campos extra y filtros por persona](#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23).

## Cierre del review de catálogos E1 — 2026-10-22

El [review de E1](../reviews/2026-10-22-code-review-catalogos-e1.md#cierre-local) quedó cerrado con sus 15 puntos contrastados y corregidos; el diagnóstico de hoja incluye TooManyCells y UnknownColumn conserva literalmente un encabezado que coincida con una clave del front. El diálogo de importación reserva espacio para su cierre también con títulos largos a 320 px.

La verificación completa sobre backend `76b07d6f` y front `e78a946` acredita **6.016 tests de backend con Docker**, build sin advertencias, **2.959 tests del front**, lint limpio, **49 controles de arquitectura y 677 visuales**. Las 21 cohortes HTTP se regeneraron juntas en `20261022-232900-f581c74e9c8e4d28940699dd1589547b`: 429 capturas y 78 fuentes con procedencia y hashes válidos. Las 127 referencias visuales y los seis tableros conservan sus bytes; las 114 capturas E1 y el recorrido móvil es/en quedaron revisados.

El navegador usa transporte sintético; el [piloto con Meta](../plans/verificacion-y-produccion.md#formularios-catálogos-e1) sigue pendiente. No cambiaron capacidades, esquemas ni instrucciones de IA. Los servidores propios quedaron cerrados y se ejecutó `aspire stop`.

## Catálogos con campos extra y filtros por persona — E2, 2026-10-23

### Campos del catálogo

Los catálogos Manual conservan sus campos fijos y admiten hasta 20 campos extra (incluidos inactivos) en FieldsJson, bajo la misma revisión del catálogo. Cada campo tiene identidad estable, clave minúscula reservada, nombre obligatorio en el idioma original del catálogo (`originalCulture`) y traducción opcional en el otro (una traducción `null` o en blanco se descarta al guardar), tipo texto/entero/decimal/fecha/booleano/catálogo, reglas, papel filtro/dato, posición y actividad. Los nombres no traducidos usan el original individual, sin bloquear el idioma del formulario, PDF o envío. No hay nombres empresariales incorporados al código.

Un campo nuevo nace opcional y sólo carga valores después de guardarse. Quitar significa inactivar y conservar el último nombre activo. Si contiene valores requiere confirmación: cada campo del detalle trae `valuesCount`, la cantidad de opciones (activas o dadas de baja) con un valor para ese campo, la misma que lleva `count` en `Forms.Catalog.FieldRemovalNotConfirmed`. Si lo usan el borrador o la versión activa de un formulario, o la presentación del catálogo, no se puede quitar. Mientras una pregunta del borrador o de la versión activa filtre por el campo (`match`), tampoco se le puede cambiar el papel, el tipo ni el catálogo fuente: el 409 `Forms.Catalog.FieldInUse` trae esos usos en `fields[i].role`, `fields[i].type` o `fields[i].sourceCatalogId`, la primera propiedad cambiada en ese orden. Una pregunta que sólo lo muestra en su presentación no lo frena. Los metadatos de uso se consultan por lote. Cada uso (`usedBy` de un campo y `uses` del 409 `Forms.Catalog.FieldInUse`) nombra el formulario y la pregunta, con su identidad (`questionId`, el FieldId de la pregunta), y dice si es el borrador (`Draft`) o la versión activa (`Active`): una misma pregunta en los dos son dos usos con la misma identidad. El uso de la presentación propia del catálogo es `Catalog`, sin formulario, pregunta ni identidad (`definitionId`, `question` y `questionId` null). El formulario y la pregunta se nombran sólo a quien tiene `formtemplates.read`: sin ese permiso, los usos `Draft` y `Active` llegan uno por formulario y etapa (primero `Active`), con `definitionId`, `formName`, `question` y `questionId` null, así que se ven el bloqueo y la cantidad sin nombrarlos; el uso `Catalog` no cambia. Una regla mal formada se informa en su control: `min` o `max` en un tipo sin rango, en `fields[i].type`; una cota ilegible o de más de 64 caracteres, en `fields[i].min` o `fields[i].max`; un mínimo mayor que el máximo, en `fields[i].max`, y un `maxLength` fuera de 1–300, en `fields[i].maxLength`. Cambiar tipo o restricciones valida todas las opciones bajo el lock del catálogo; un error revierte el conjunto. Bajar `maxLength`, cambiar `min` o `max`, activar «único» o reactivar un campo valida de nuevo sus valores guardados, también en un campo inactivo (que nunca es obligatorio; uno inactivo que pasa a catálogo o cambia de fuente se valida contra esa fuente, Manual y activa); quitar un campo sin cambiar sus reglas no los revisa. Los que no cumplen se acumulan en el control que rompen y responden 400 `Forms.Catalog.FieldValuesIncompatible` en `fields[i].isActive` (todo lo que falla al reactivar), `fields[i].maxLength`, `fields[i].min`, `fields[i].max` o `fields[i].unique`, el primero con problemas en ese orden, con `invalidCount` e `invalidRows` como el cambio de tipo. Cada guardado informa todos los valores incompatibles de ese primer control afectado, con su conteo y sus filas; los de los demás controles se informan en el guardado siguiente, una vez corregido el primero; con «único» activado (o al reactivar un campo único) cada opción activa que comparte su valor con otra es una fila, con motivo `invalid`, también si el mismo guardado cambia el tipo o la fuente: entonces primero se informan los valores que no convierten (`fields[i].type`) y, si todos convierten, los repetidos. Un valor editado en el mismo guardado, o el que falta en una opción nueva o reactivada en él, conserva su error de celda en `items[j].fields.<id>`, y un «único» que ya estaba activo informa el primer repetido como antes. Al cambiar el tipo, un valor guardado como texto se convierte como una celda importada, en la cultura del pedido (el idioma del menú): en español «1,5» da 1.5, «1.234,5» da 1234.5 y «15/03/2027» una fecha, y un número ambiguo como «1.500» o «12.345» no convierte (motivo `invalid`). Pasar a texto guarda el texto visible, en el idioma original del catálogo y no en el del menú: una opción de catálogo, el texto de la opción fuente (aunque esté dada de baja; sin traducción, su texto en el otro idioma), y un booleano, «Sí» o «No». Si ese texto no entra en la longitud máxima, la fila no convierte (motivo `length`, con el texto como valor intentado), y un valor que no es opción de su fuente tampoco (`invalid`). Una opción guardada que no convierte a otro tipo o a otra fuente también se informa por ese texto de la fuente anterior, en el mismo idioma, y no por su identidad. Otro valor guardado de otro tipo se lee en su forma canónica, y uno editado en el mismo guardado (mandado en `items[i].fields`, aunque coincida con el texto guardado) llega en el canónico del tipo nuevo: así «12,345» corregido en español llega como 12.345 y se guarda como decimal. Obligatorio y único se comprueban sobre opciones activas. Los problemas conservan path de campo/celda y metadata acotada: cantidad completa de conversiones fallidas y, en `invalidRows`, las primeras 50 filas en orden de fila (no el de almacenamiento), con número, valor intentado (hasta 300 caracteres) y motivo (`required`, `range`, `length` o `invalid`); faltantes o usos.

FieldValuesJson de la opción es jsonb con default `{}` y GIN jsonb_path_ops. El canon es único: entero/decimal exactos, fecha ISO, booleano JSON y GUID de opción fuente. En el alta y el PUT, `items[i].fields` es un delta, como el PUT de datos de las personas: se mandan sólo los valores cambiados, un campo ausente conserva su valor y null lo limpia; la importación manda cada campo activo de la fila, con null si no tiene valor. Sólo se reescribe la opción cuyo mapa de valores cambió, comparado por valor y no por el texto del jsonb: renombrar el catálogo, editar otra opción o aplicar una importación sin diferencias no toca la auditoría (`ModifiedAtUtc`) de las demás. Las respuestas HTTP de edición e importación conservan las representaciones decimales como string, sin pasar por Number. Una fuente es otro catálogo Manual activo; se guarda la identidad de la opción, no su etiqueta. Un valor nuevo, cambiado o convertido exige una opción activa de la fuente; el valor guardado de una opción dada de baja se conserva y no impide editar ni guardar el catálogo, pero uno que no es opción de la fuente se rechaza. El mismo catálogo de valores puede alimentar un campo y un atributo de persona; una fuente usada por campos o atributos activos no puede darse de baja.

La tabla suma campos después de Código, con tipo, errores por celda y menú de columnas. Las columnas nuevas sin guardar quedan apagadas, con el motivo en el tooltip de cada celda y sin texto en el encabezado; desde el 2026-10-31, «Listo» del modal de campos avisa con un warning que hay que guardar el catálogo para cargar sus valores. En ese modal, la clave de un campo nuevo sigue al nombre (snake_case minúscula, sin tildes, con el patrón del servidor y sin repetir otra clave) hasta que se edita a mano, y la fuente se elige en un combo que muestra los primeros catálogos antes de escribir. El ancho se desplaza dentro de su región, sin un texto de más columnas: la barra alcanza; en el celular se conserva el editor por opción. Importación, pegado, plantilla y export usan los mismos tipos. Los encabezados reconocen clave o nombre es/en cuando son inequívocos, y uno en blanco no reconoce ningún campo; una columna extra opcional ausente conserva su valor y una presente vacía lo limpia. Para fuentes se acepta identidad activa, código o texto único es/en, con índice reutilizado por catálogo. Una celda que no nombra ninguna opción activa, o cuyo texto es ambiguo entre ellas, pero nombra la opción guardada (identidad, código o texto es/en) la conserva aunque esté dada de baja; si nombra una activa, aunque comparta el texto con la guardada, pasa a esa. El export escribe el texto de la opción fuente en el idioma del menú y, si ese texto nombraría otra opción (una dada de baja con el mismo texto que una activa), su código; si tampoco, su identidad: así se reimporta sin cambios. Un valor nuevo o cambiado sigue exigiendo una opción activa. Aplicar una importación bloquea las fuentes de los campos activos y el catálogo, ordenados, igual que el guardado. La plantilla encabeza cada campo con su clave y desambigua la que coincide con una cabecera fija con [campo]/[field]. El export lo encabeza con su nombre en el idioma del menú (o, si falta, en el otro); un nombre que la importación leería como una cabecera fija u otro campo lleva [campo]/[field], que la importación también reconoce, y uno que sigue ambiguo, como dos campos con el mismo nombre, usa la cabecera de la plantilla. Un sí o no se exporta «Sí» o «No» en el idioma del menú, y entero, decimal y fecha como el valor del catálogo (E1-A). Un export anterior, con [canónico]/[canonical], se sigue importando sin reinterpretar precisión ni separadores.

El listado de opciones del catálogo admite `ids` repetido (`?ids=a&ids=b`, hasta 100 identidades, sin la vacía) y devuelve sólo esas opciones, en el orden y la página habituales; sin `isActive` incluye las dadas de baja y una identidad de otro catálogo no aparece. Más de 100 o la vacía responden 400 con el código genérico `Validation.Failed` y el texto de `Forms.Catalog.ContentInvalid` en `errors.ids`. Sirve para resolver opciones por identidad, como la etiqueta de un valor fijo o las filas que señala `invalidRows`, sin buscarlas entre la primera página.

Una baja de campo rechazada por un uso concurrente conserva la fila inactiva con su error y permite restaurar la misma identidad y valores, sin recargar ni perder otros cambios.

La vista previa muestra los valores normalizados, el nombre/tipo del campo y la etiqueta de las fuentes; no muestra GUID como texto al usuario. Faltante o repetición de columna obligatoria se informa dentro del diálogo junto al archivo o pegado, conservando la entrada. Aplicar sin diferencias no cambia revisión ni marca de importación; un cambio real avanza una sola revisión. El recibo de importación usa mapas de identidad vacíos; el PUT conserva el requisito de revisión mayor.

### Datos de las personas

BusinessForms define atributos y valores en sus propias tablas, sin FK a Usuarios. Cada atributo tiene su clave reservada, nombre es/en, tipo Catalog o Text, fuente, posición, activo y revisión. El máximo 20 incluye inactivos. Un atributo usado por el borrador o la versión activa no cambia tipo/fuente ni se desactiva: el 409 Forms.PersonAttribute.InUse sale aunque el PUT confirme borrar los valores, que se conservan; renombrarlo sí se puede. Cada uso (`usedBy`) nombra el formulario sólo a quien tiene `formtemplates.read`, como los usos de un campo de catálogo: sin ese permiso llega uno por formulario con `definitionId` y `name` null, así que se ven el bloqueo y la cantidad sin nombrarlo. Si alguna persona tiene cargado el dato, cambiar su tipo o su fuente devuelve Forms.PersonAttribute.ValueRemovalNotConfirmed (400, con `count` y el error en `type`, o en `sourceCatalogId` si sólo cambia la fuente) hasta que el PUT lleve `confirmValueRemoval: true`; al confirmar, el mismo guardado borra ese valor de cada persona y avanza su revisión, como quitar un campo de catálogo con datos. La confirmación y `count` cuentan sólo las personas de la tabla administrativa (cuentas no borradas); el borrado del valor alcanza también a las cuentas borradas, que pueden restaurarse, aunque sólo ellas lo tengan y no se pida confirmar. Así no quedan valores que no corresponden al tipo o a la fuente nuevos; si queda alguno anterior, la lectura lo devuelve sin texto. Inactivar conserva su último nombre activo. La clave sigue el mismo patrón que la de un campo de catálogo (`^[a-z][a-z0-9_]{0,39}$`) y es única entre los datos (Forms.PersonAttribute.KeyDuplicate): en el diálogo del front, la de un dato nuevo sigue al nombre sin repetir otra hasta que se edita a mano, y el catálogo se elige en el mismo combo que la fuente de un campo (2026-10-31).

Los valores ocupan una fila por UserId, con ValuesJson y revisión independiente; nunca se guardan en la fila del usuario. La ausencia de fila devuelve revisión null y valores vacíos. El PUT es un delta: atributo ausente conserva su valor, null o blanco limpia el nombrado, Text quita espacios de los extremos y admite hasta 300 caracteres y Catalog exige opción activa de una fuente Manual activa. Los valores de atributos inactivos quedan guardados y no se devuelven a la edición. El recibo devuelve la misma persona con revisión siguiente y las etiquetas de los valores conservados, además de los recién cambiados.

Definir atributos exige formpersondata.manage. Leer su metadata u opciones requiere ese permiso, users.manage o formtemplates.manage. La tabla administrativa requiere formpersondata.manage y users.read: incluye usuarios inactivos y excluye borrados, sin devolver correo. Además de la página, `GET /api/forms/person-data` devuelve `missing`: para cada dato activo, en su orden, `{ attributeId, count }` con cuántas personas de todo el conjunto filtrado (la misma búsqueda, no sólo la página) no lo tienen cargado: sin valor o, en un dato de catálogo, con un valor que no es opción de su fuente actual, que la tabla muestra vacío y el envío trata como faltante; es lo que muestra «N personas · M sin zona», que el front no cuenta sobre una página parcial. Cargar valores desde Editar usuario exige users.manage. Su selector de valores usa la séptima ruta, GET /api/forms/person-attributes/{id}/options, paginada e id/text, y no concede formcatalogs.read. Forms.PersonAttribute.Invalid es de validación y Forms.PersonAttribute.SourceUnavailable es un conflicto 409; OpenAPI y DTO conservan el contrato completo. Elegir una fuente al definir el atributo reutiliza el selector de catálogos bajo su permiso; sin él el selector queda apagado con motivo, pero la metadata existente y la edición de nombres/Text siguen disponibles.

La ruta plana /datos-de-personas vive en Administración. La sección Datos de Editar usuario la aporta el módulo por un contrato neutral compuesto en app: el núcleo Usuarios no importa BusinessForms. Se guarda aparte y sólo si cambió. Si el usuario se guardó y los datos fallaron, el diálogo sigue abierto e informa ambos resultados en la sección. Un 409 conserva el borrador hasta recargar explícitamente. La lista de usuarios no suma columnas. Sin el módulo no están la ruta, la capacidad ni la sección.

El orden de locks de atributos es definiciones → atributos → fuentes ordenadas; para valores es atributos → persona → fuentes ordenadas. Cada caso de uso que escribe abre una sola transacción y revalida permiso y revisión dentro; las lecturas privadas revalidan antes de devolver. Sin caché ni replay durable: es configuración interna acotada. HTTP y Application redactan búsquedas/valores al formatearse. Los clientes son no-store, cancelables y sin MutationCache.

### Presentación y filtros por destinatario — E2

El catálogo define la presentación por defecto; una pregunta puede heredar o elegir la propia, que se publica con su versión. Null conserva texto/detalle anteriores. Campos fijos o extra se unen con « · »; el título debe tener contenido y hasta 30 caracteres, la descripción hasta 300. Título y descripción muestran el valor y los campos tipados con el formato de pantalla del idioma, el mismo que usa el front (el PDF tiene el suyo): en español 1.500 · 1.500,5 · 15/03/2027 · Sí, y en inglés 1,500 · 1,500.5 · 03/15/2027 · Yes. Con ese texto se cuentan los largos y se arman los ejemplos del aviso, «Como si lo recibiera» y el Shown de la oferta. El editor deja guardar y avisa, en cada idioma del catálogo (el original y la traducción si tiene nombre), cuántas opciones activas superan los 30 caracteres del título y los 300 de la descripción, con hasta tres ejemplos de cada una (`presentationCheck.cultures` del detalle y de los recibos de guardado e importación). Una pregunta con presentación propia recibe el mismo aviso del servidor, calculado sobre todas las opciones activas de su fuente y en cada idioma del catálogo, sin guardar: `POST /api/forms/catalogs/{id}/presentation-checks` con formcatalogs.read, que responde `PresentationInvalid` en `presentation` si el catálogo no es Manual o la presentación usa un campo que no está activo; «Como en el catálogo» usa el chequeo del detalle. Chequeo y envío los bloquean. Sólo viaja lo elegido. Shown se sella en la oferta de la sesión; Text sigue siendo la etiqueta de la opción para planilla y chat.

Match combina filtros de igualdad, uno por campo de papel filtro y con tope. Un filtro compara un valor fijo o un dato de la persona; el segundo exige campo y atributo de tipo catálogo con la misma fuente. El valor fijo de un campo de catálogo tiene que ser una opción activa de su fuente: si no existe, es de otro catálogo o está dada de baja, el chequeo y la aceptación frenan con Forms.Publication.MatchValueUnavailable en `source.match`: el filtro tiene que apuntar a una opción vigente de la fuente. Normalize ordena por identidad y transforma [] en null. Editar filtros/presentación conserva FieldId si no cambia Source.Catalog. Los contratos nuevos opcionales omiten null para conservar huellas viejas.

La preparación común resuelve la persona real, valida campos y atributos activos, arma contención JSON tipada y filtra en SQL antes del límite 201; no descarga todo para filtrar en memoria. Sin el dato requerido devuelve Forms.Record.RecipientDataMissing, también cuando el valor guardado no es una opción de la fuente actual del dato (otro catálogo o un texto anterior), aunque la misma opción sea un valor válido de otro dato de la persona, sin leer opciones. Ese issue nombra en `missingData` el dato que falta (el primero de los filtros por persona sin valor, en el orden normalizado), en el idioma del menú y, si no tiene, en el otro, para que el chat diga qué cargarle; en los demás issues `missingData` es null. Un filtro que dejó de funcionar devuelve Forms.Record.CatalogFilterUnavailable. Nunca retira el filtro ni manda la lista completa. Asistente, manual, prellenado, aclaración y sugerencias usan el mismo punto. La preview Como si lo recibiera usa personas de ejemplo y exige Read+Manage+CatalogsRead, porque devuelve títulos de las opciones del catálogo pedido; no requiere administrar catálogos (CatalogsManage). Elegir el catálogo y consultar sus nombres/campos sigue requiriendo CatalogsRead; no se amplían sus getters.

La vista previa y sus personas de ejemplo exigen Read+Manage+CatalogsRead, revalidan antes de devolver incluso una ausencia y no guardan ni toman locks. Su estado distingue Ready, RecipientDataMissing y FilterUnavailable; este último devuelve siempre los valores de persona vacíos, y un valor guardado que no es opción de la fuente actual del dato no figura entre ellos, como en la preparación. Puede representar títulos vacíos o largos y descripciones largas para mostrar sus diagnósticos, pero no informa cuántos títulos superan el largo: ese aviso es de la fuente de la pregunta, sobre todas sus opciones activas y no sólo las ofrecidas a una persona. El lector común cuenta hasta 201 opciones, también para varias, y la vista muestra sólo las primeras 20 o 200 según el tipo. El envío recompone Shown después de decidir la cultura efectiva y lo sella en esa cultura.

El editor de la pregunta lee el máximo de opciones por persona sin elegir persona ni guardar el formulario: `POST /api/forms/recipient-maximums` recibe sólo `source` (catálogo y entre 1 y 20 filtros) y devuelve `maximum` y `maximumLabel`. Con filtros por persona, `maximum` es el grupo más grande que dejan los filtros fijos y `maximumLabel` nombra sus datos en el idioma del menú (por ejemplo «Zona Sur»); con sólo filtros fijos todos ven las mismas `maximum` y `maximumLabel` es null. Es el mismo cálculo que el aviso y el bloqueo de publicar. Exige los mismos permisos que la vista previa, antes y después de leer, no guarda ni toma locks; un catálogo que no es Manual activo da Forms.Publication.CatalogInvalid en `source.catalog` y un filtro inválido, el error de Match en `source.match`; un valor fijo sobre un campo de catálogo que no es opción activa de su fuente da, como al publicar, Forms.Publication.MatchValueUnavailable en `source.match`, sin máximo. Como las ausencias de la vista previa, esos errores salen sólo si los permisos siguen después de leer el catálogo (si no, 403). El límite contra el que se compara (20 o 200) sale del tipo de la pregunta, en el front.

Publicar distingue problemas bloqueantes de avisos no vinculantes: la cantidad máxima de opciones por combinación de datos, sólo cuando pasa de 200 o 20 o cuando da cero, como el editor de la pregunta (NI3-B, 2026-11-03: lo que entra no avisa), y personas activas con formrecords.create a las que falta cualquiera de los atributos: sin valor o, en un dato de catálogo, con un valor que no es opción de su fuente actual (activa o dada de baja), la misma regla que la tabla de personas y el envío. El lector es SQL fijo por lote; cuenta personas distintas y limita ejemplos. Los avisos se presentan separados y no impiden publicar; un envío real conserva sus propios bloqueos por destinatario. La excepción es «elegir varias» sin filtros por persona: ahí el máximo es lo que dejan los filtros fijos para todos y, si pasa de 20, el chequeo y la aceptación frenan con Forms.Publication.CatalogTooLargeForMultiple en `source.catalog`, como un catálogo sin filtros y sin aviso. Datos empresariales no se convierten en SQL configurable. El canal de compilación de WhatsApp y su huella de publicación no reciben nuevos campos de pregunta; sólo el payload de envío usa Shown.

### Campos sellados en las respuestas

La captura lee campos sólo de opciones Manual elegidas, bajo sus locks y por lote, y sella identidad, clave, nombres, tipo, papel y valor en cada referencia. Si el campo elige de otro catálogo, conserva también el texto de esa opción fuente; un valor que no es opción de la fuente actual del campo (guardado antes de un cambio de fuente) no tiene texto visible y queda fuera del sello, como un campo sin valor, en lugar de frenar la captura. La sesión no carga 200×20 valores y ninguna lectura posterior amplía la historia. Referencias antiguas mantienen null y omiten el contrato nuevo; no se completan con el catálogo actual.

La metadata de la planilla combina campos activos actuales y campos históricos retirados. Se conoce antes de leer respuestas, también en una planilla vacía; la unión histórica no se limita artificialmente a 20. El nombre de cabecera puede ser el actual, pero cada valor del detalle conserva su nombre, tipo, papel y etiqueta fuente sellados. Un nombre o texto de campo sin traducción usa su original individual y no cambia el idioma de formulario, aviso o PDF.

La planilla suma subcolumnas de sólo lectura después de su pregunta: las de papel dato se ven por defecto y las de filtro se activan en Columnas. No se ordenan, filtran ni cuentan en facetas. Las opciones y sus valores mantienen el orden recibido; falta histórica se muestra como «—», distinta de una pregunta sin respuesta. D2 y pantalla completa muestran todos los campos junto a cada opción.

Excel incluye todos los campos si catalogFields falta o es null, independientemente de la selección de preguntas. Una lista vacía no incluye campos extra y una lista de pares pregunta/campo elige sólo esas subcolumnas, incluso sin su pregunta base; «Solo las columnas visibles» usa esa selección. Una elección simple conserva tipos número/fecha/booleano y los límites numéricos de Excel ya documentados; varias conserva los valores canónicos como texto con «; » entre opciones. Los IDs ajenos se rechazan antes de leer registros o generar bytes.

El PDF agrega sólo campos de papel dato, formateados según su tipo y con etiqueta fuente en los de catálogo. La tarjeta del chat conserva su contrato: el puerto de lectura y los valores de origen de las sugerencias omiten estos campos y no los agregan al contexto del asistente. Las nuevas propiedades opcionales se omiten con null, sin reescribir publicaciones ni respuestas anteriores.

### Persistencia y alcance de las comprobaciones

Las migraciones conservan filas previas con valores extra vacíos y presentación sin definir. Sus Down toman locks antes del DDL y rechazan hechos que el esquema anterior no representa; bajar exige un respaldo compatible cuando hay esos hechos, sin borrarlos ni transformarlos para pasar la guarda. Las lecturas por contención usan SQL fijo parametrizado, antes del paginado/límite de oferta. La integración con 10.000 opciones y 20 campos devolvió las 100 coincidencias exactas antes del límite; EXPLAIN usó Bitmap Index Scan sobre IX_BusinessFormCatalogItems_FieldValuesJson. La medición local fue de aproximadamente 2,14 s al guardar y validar, 5,16 ms al ofrecer y 10,86 ms al capturar. Son observaciones de esa corrida, sin SLA ni benchmark de memoria; no se fuerza GIN y el planificador puede preferir el índice por catálogo/actividad. Rangos, orden por campos extra y tabla tipada quedan fuera de alcance.

### Referencias y compatibilidad con E1

La [referencia oficial de CheckboxGroup](https://developers.facebook.com/documentation/business-messaging/whatsapp/flows/guides/components#checkboxgroup) fija el data-source dinámico, mínimo y máximo enteros, 1 a 20 opciones, título/label de hasta 30 y descripción de hasta 300. Es evidencia documental del compilador r9, no una prueba del webhook físico: su array, orden real y vacío siguen en el piloto pendiente de E1. El CSV Windows-1252 se rechaza sin recodificarlo; el error pide XLSX o CSV UTF-8.

### Verificación local de E2 — 2026-10-23

La corrida final del backend, con PostgreSQL y Redis reales en Docker, aprobó 6.266/6.266 tests, sin omitidos. El build completo terminó con cero advertencias y errores. Las 22 cohortes HTTP se exportaron juntas desde la misma corrida; su procedencia identifica los productores y conserva los cuerpos completos para los guards del front.

Los PDF reales de campos extra en es/en se renderizaron con Poppler y se inspeccionaron: una página A4 por cultura, sin recortes ni solapamientos, sólo datos, con nombres históricos, respaldo individual al español y números, fechas, booleanos y fuentes localizados. La prueba SQL y la medición 10.000×20 se distinguen del recorrido visual y del piloto físico.

La retirada física del módulo se comprobó en una copia aislada: se excluyeron sus carpetas y composición y se alineó allí el snapshot mediante una migración propia de esa copia. El build quedó sin advertencias ni errores, con 169/169 pruebas de arquitectura, 2/2 de inventario y 1/1 de ausencia en Docker, sin omitidos. Los PostgreSQL/Redis descartables no tocaron las bases reales ni de desarrollo y main permaneció intacto; esto no es una guía automatizada para quitar formularios.

El front aprobó 3.116/3.116 tests en 309 archivos, con build completo sin advertencias y lint limpio. Los consumidores calificaron las 499 capturas intactas de las 22 cohortes exportadas juntas; la procedencia y el contenido se conservaron en el commit de fixtures e4db46c.

La última corrida visual aprobó 719/719 pruebas. El navegador recorrió CE1/CE2, datos de las personas, CE3/CE4 y CE5 en es/en a 390 y 320 px; las escenas también produjeron evidencia de escritorio. Se inspeccionaron campos, valores y errores, nuevo campo opcional apagado, tablas con desplazamiento contenido, foco, guardado parcial, presentación heredada/propia, Juan con 14 opciones y Pablo sin dato con cero opciones, subcolumnas y exportación visible. El ajuste final de dos botones a 320 px conserva el Button compartido y pasó su regresión geométrica.

Las pantallas usan transporte y datos sintéticos; los PDF y XLSX reales corresponden a la integración del backend. No se hizo el piloto con Meta. Capacidades, esquemas e instrucciones de IA no cambiaron y no se repitió su calificación. Se cerraron la pestaña y los servidores propios, se restauró el viewport y se ejecutó aspire stop: no había AppHost en ejecución. El review de E2 precede al [único piloto físico pendiente](../plans/verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2). Main, sin push.

## Carga desde la app — Etapa 1, 2026-10-30

Quien tiene «Completar formularios» (`formrecords.create`) carga una respuesta desde la app, en su propia página o en un panel junto a la planilla, sobre la versión publicada activa. La respuesta queda a su nombre, igual que la que llega por WhatsApp, y sigue las reglas de su formulario. Pantallas y textos: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md); alcance y etapas siguientes: el [plan](../plans/2026-10-29-cargar-formularios-desde-la-app.md).

### Rutas y permisos

- `GET /api/forms/entries/{definitionId}` devuelve la versión activa: las preguntas en el idioma del menú si la versión lo publicó (si no, en el original), el texto del botón, el tamaño máximo de archivo y, en cada pregunta de catálogo, la única opción que dejan los filtros o el dato de la persona que falta (`Forms.Record.RecipientDataMissing` con `missingData`).
- `GET .../versions/{versionId}/questions/{fieldId}/options` devuelve hasta 50 opciones por página, con búsqueda y los filtros fijos y por persona de quien carga, la misma contención que el envío por WhatsApp. Si la versión ya no es la activa responde 409 `Forms.Entry.VersionChanged`.
- `POST /api/forms/entries/{definitionId}` recibe un multipart con `requestId`, `versionId`, las respuestas y los archivos, cada uno con su pregunta. Las tres rutas exigen cuenta activa y `formrecords.create`, antes de leer y dentro del límite de guardado.
- «Formularios» (`GET /api/forms/overview`) también se abre con `formrecords.create`: quien sólo completa ve los formularios activos con «Cargar» y, desde la Etapa 2, sus cargas y la última (ver abajo).

### Envío

- Repetir el mismo `requestId` con las mismas respuestas devuelve el primer resultado, antes de mirar la versión o la baja: un reintento después de un corte no duplica ni toma otro número. Otras respuestas son otro envío. Después de un envío que llegó, el front arma otro `requestId` aunque mantenga las mismas respuestas para la siguiente: es otra respuesta, con otro número (AR2-A).
- Valida como la captura de WhatsApp: tipos, obligatorias, rango de «elegir varias» y archivos (tipo, tamaño, cantidad y contenido). La oferta sella sólo las opciones elegidas; una opción dada de baja entre abrir y enviar responde `Forms.Record.CatalogItemInactive` en `answers.<fieldId>`.
- Un formulario dado de baja responde 409 `Forms.Entry.Unavailable` y una versión nueva, 409 `Forms.Entry.VersionChanged`; el front ofrece pasar a la nueva conservando lo cargado.
- El registro guarda `EnteredInApp`. La planilla lo muestra en «Canal» como App, con el filtro `enteredInApp` también en conteos, navegación y Excel, y el detalle dice «En la app · …» debajo de «Recibida» y de «Se respondió el formulario», donde una respuesta de WhatsApp dice «Por WhatsApp · …» (TL1-A y TL3-A).
- Aprobación y PDF siguen las reglas del formulario. «Mandarle un mensaje a quien la cargó» no aplica a lo cargado en la app si quien lo cargó no tiene un canal donde recibirlo, hoy WhatsApp vinculado: la acción queda como no aplicable.

### Cómo se arma el formulario en la app

- El autor decide el contenido (orden, textos, ayuda, obligatoria, opciones, largo máximo, rango de «elegir varias» y límites de fotos) y la app la forma, con una tabla fija por tipo de pregunta (CW12 y CW16). La unidad va en la etiqueta que escribe el autor, «Densidad (kg/ha)», como en WhatsApp, la planilla, el Excel y el PDF (CW13-A).
- En escritorio, cada zona es una grilla de tres columnas en el orden del editor; en tableta, dos; en el celular y en el panel, una. Las columnas salen del ancho de la zona y no del de la pantalla: una tableta con un panel del menú abierto usa una. Hay dos anchos (AR1-A, 2026-11-02). Lo que tiene un solo valor ocupa una columna: el número, la fecha, sí o no, el texto corto de hasta 80 caracteres, elegir una y elegir de un catálogo, de una o de varias. Lo que necesita espacio ocupa el renglón entero: el texto largo, el texto corto de más de 80 o sin largo máximo, elegir varias con opciones propias y los archivos. Una pregunta que no entra baja de renglón; nunca se reordena.
- Elegir una muestra hasta cinco opciones en fila aunque sean largas, y las que no entran bajan; con más, una lista desplegable. Elegir varias reparte sus casillas de arriba abajo en tres columnas en escritorio, dos en tableta y en el panel y una en el celular.
- «Datos» y «Fotos y documentos» se pliegan, también en el panel, con los archivos al final; arrancan abiertas y, plegadas, dicen cuántas preguntas se respondieron; la de los archivos dice lo que se adjuntó («3 fotos · 1 archivo»). Al enviar con algo para corregir, una zona plegada se abre sola y el foco va a la primera pregunta con error.
- La página y el panel ofrecen «Mantener las respuestas para la siguiente» (AR2-A): marcada, al enviar el aviso dice el número y el formulario queda con lo cargado, salvo los archivos, sin pasar por «Enviada»; en la página el foco vuelve a la primera pregunta. No se recuerda al volver a entrar.

## Lo propio de quien sólo carga — Etapa 2, 2026-11-04

Quien tiene «Completar formularios» y no «Ver respuestas» lee sólo lo que cargó, por la app o por WhatsApp (CW6-C): la misma planilla, el detalle, los archivos y el PDF. Lo propio es el registro con su `ActorUserId`, el mismo para los dos canales. Pantallas: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#lo-propio-etapa-2--2026-11-04).

- Las lecturas de respuestas (planilla, conteos, navegación, detalle, archivos, documentos y workflow) se abren con `formrecords.read` o `formrecords.create` (`HasAnyPermission`). `FormRecordAccessGuard.CheckReadAsync` decide: con lectura, todo; sólo con completar, lo propio, y revalida ese mismo permiso al terminar. El Excel sigue exigiendo lectura y exportación, y los controles del workflow, lectura y reglas.
- El servidor acota lo propio, también ante una dirección directa. La planilla, los conteos y la navegación llevan `OwnerUserId` en el pedido interno (nunca viene del cliente) y el lector lo aplica al filtro común y a las opciones de cada conteo: el de personas sólo la nombra a ella y el de un catálogo sólo trae las opciones que eligió. Un filtro por otra persona no devuelve nada. El detalle, un archivo, un documento o el workflow de una respuesta ajena dan el mismo 404 que una inexistente.
- El workflow de lo propio trae el estado, la tarea con su decisión y motivo, y los documentos, sin las acciones internas (avisos y sus destinatarios).
- «Formularios» cuenta las respuestas propias de cada formulario y trae la última propia (número, fecha y estado); quien también lee ve el total de siempre.

## Listas dependientes — Etapa 4, 2026-11-04

Una pregunta de catálogo puede filtrar sus opciones por la respuesta de otra pregunta del mismo formulario (CW7): «según otra respuesta» es un tercer filtro, junto al fijo y al de quien lo recibe, y se suma con ellos. Sirve a quien carga desde la app; en WhatsApp queda para después, con el intercambio cifrado con Meta. Pantallas y textos: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#listas-dependientes-cw7--2026-11-04).

- **El filtro.** `match[i].questionId` nombra la pregunta de la que depende, y cada filtro lleva uno solo de `value`, `personAttributeId` o `questionId`. Compara un campo activo de papel filtro y tipo catálogo con la opción elegida en esa pregunta, que tiene que ir antes, ser de elegir una y elegir del catálogo fuente del campo. En el borrador, una pregunta nueva se nombra por su `localId` (el UUID que le da el editor) y el guardado sella su `fieldId`; un nombre que no es una pregunta del borrador queda como está y publicar lo frena. La propiedad se omite cuando es null, así las preguntas ya publicadas y sus huellas no cambian.
- **Publicar.** Frena `Forms.Publication.MatchQuestionInvalid` en `draft.fields[i].source.match` si la pregunta no va antes, no es de elegir una o no elige del catálogo del campo, también al aceptar. Avisa sin frenar con `Forms.Publication.DependsOnAnswer` en esa pregunta: por WhatsApp el formulario no se manda, y en la app se carga igual. Una lista dependiente no lleva el aviso del máximo por persona ni el bloqueo de «elegir varias» con más de 20, que son límites de WhatsApp; el aviso de personas sin el dato sigue si además filtra por quien lo recibe. El máximo del editor (`recipient-maximums`) agrupa también por la respuesta, aunque la pantalla no lo muestra para estas preguntas.
- **WhatsApp y el asistente.** La preparación de un canal devuelve `Forms.Record.CatalogDependsOnAnswer` en esa pregunta, así un envío no sale y el asistente no ofrece el formulario. La lista de «Enviar formulario» trae `dependentQuestion`, la etiqueta por idioma de la primera pregunta dependiente, para mostrarlo apagado con el motivo.
- **La carga.** `GET /api/forms/entries/{definitionId}` dice en `catalog.dependsOn` de qué preguntas depende una lista, sin una opción elegida de antemano; si a la persona le falta un dato de un filtro por quien lo recibe, se dice eso primero, con `missingData`. Las opciones reciben `answers[<questionId>]=<optionId>` y filtran en SQL por esa opción; sin la respuesta, 409 `Forms.Record.CatalogDependsOnAnswer`. Al enviar, la oferta de la lista se arma con la opción elegida en la pregunta de la que depende, que se revisa antes en el mismo orden: una opción que no corresponde, o una lista respondida sin esa respuesta, da `Forms.Record.CatalogItemInactive` en `answers.<fieldId>` y no se guarda nada.

## Elegir primero por un campo — LD1-A, 2026-11-07

Una pregunta de catálogo puede pedir primero el valor de un campo del mismo catálogo y después ofrecer sólo las opciones con ese valor (LD1-A): primero «Herbicidas» y después sus artículos. Es un ajuste de la pregunta, no un filtro ni otra pregunta, y se guarda sólo la opción. Están el backend, el editor y la carga en el front, el envío por WhatsApp (LD2-A, r11) y el resumen por valor del editor; las listas dependientes en los renglones (LD3-A) siguen en el [plan](../plans/2026-11-07-elegir-primero-por-un-campo.md). Pantallas: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#elegir-primero-por-un-campo-ld1-a-a-ld3-a--2026-11-07).

- **El borrador.** `source.groupBy` nombra el campo por su id y se omite cuando es null, así las preguntas publicadas y sus huellas no cambian. Guardar rechaza un id vacío y el ajuste en una pregunta de «elegir varias» (400 en `draft.fields[i].source.groupBy`).
- **Publicar.** Frena `Forms.Publication.GroupByInvalid` en `draft.fields[i].source.groupBy` si el catálogo no es Manual o si el campo no existe, está dado de baja o no es de texto ni una opción de un catálogo; el papel del campo no importa.
- **La carga.** `catalog.groupBy` dice el campo y su nombre en el idioma del contenido, también en una lista dependiente. `GET …/questions/{fieldId}/groups` (y la misma ruta de la corrección) devuelve `groups`: los valores que tienen las opciones que dejan los filtros (fijos, por persona y según otra respuesta, con los mismos `answers[...]`), los de un campo de texto en orden alfabético y los de una opción de un catálogo en el orden y con los textos de su fuente; las opciones sin valor van al final, con `id` null y «Sin rubro» (el nombre del campo). Una pregunta sin el ajuste da 404 `Forms.Entry.QuestionNotFound`. Las opciones reciben `group=<id>` o `withoutGroup=true` como un filtro más, con la búsqueda adentro; los dos a la vez dan 400. El envío no cambia: la opción tiene que ser de la oferta de la pregunta, sin mirar el valor. Al corregir, cada respuesta de una pregunta con el ajuste, también en los ítems del bloque, trae en `group` el valor que la opción guardada tiene ahora, como lo dicen los valores («Sin rubro» si no tiene); una opción que el catálogo ya no tiene va sin `group`.
- **Qué opciones ve cada persona.** `POST /api/forms/recipient-maximums` acepta `source.groupBy` aun sin filtros y con él suma `groups` (cuántos valores) y `largestGroup` (las opciones del más grande), lo más que recibe una persona; sin el ajuste se omiten. Un campo que no sirve da 400 `Forms.Publication.GroupByInvalid` en `source.groupBy`. El editor dice «Todos ven las mismas 125, en 3 valores de «Rubro». El más grande tiene 75: entra en el desplegable (200).» (el nombre del campo no se pasa a plural) y, con un solo valor, la línea de siempre. Al publicar, el aviso `OptionsPerRecipientHigh` de una pregunta con filtros compara con 200 cada valor y la lista de valores, no el total, e informa el mayor de los dos.
- **La oferta para un canal (LD2-A).** Cada opción lleva su valor y la oferta suma los valores en orden con su texto, como en la carga («Sin rubro» al final), y el nombre del campo. El tope de 200 vale para cada valor y para la lista de valores, no para el total, también al validar la captura. El prellenado del asistente no toca una pregunta con el ajuste y una sugerencia no puede apuntarle.
- **WhatsApp (LD2-A).** El perfil `flow-static-7.3-r11` pone antes de la pregunta un desplegable con los valores (`<clave>_group`), con el nombre del campo de etiqueta; la lista de la pregunta arranca vacía y oculta y la llena el `update_data` del valor elegido. Los valores, la etiqueta y las opciones de cada valor llegan con el mensaje, y la vista oficial manda lo mismo, así que el Flow compilado no depende del nombre del campo: cada valor viaja con un id del mensaje (`g1`, `g2`…) porque su elección no va en la respuesta, y la etiqueta y los valores se cortan con «…» en 20 y 30 caracteres, los topes del desplegable. El desplegable de valores es obligatorio si la pregunta lo es, y la pregunta lo es sólo mientras se ve (Meta no abre un desplegable obligatorio oculto). Con un solo valor también hay que elegirlo. Cada pantalla que muestra la pregunta declara su lista y el `navigate` que entra se la pasa vacía; una pantalla que no es la primera toma los valores de la primera. Suma un componente de los 50 de su pantalla, el prellenado no la toca y un diagnóstico de Meta sobre el desplegable de valores señala la pregunta. Sólo vale en una elección de catálogo; sin esa pregunta, r11 compila los mismos bytes que r10. Se probó con Meta el 2026-11-08: dos borradores compilados por la app (dos pantallas, y renglones con mínimo 0) se recorrieron en la vista oficial interactiva y se borraron.

## Corregir y anular — Etapa 3, 2026-11-05

Lo guardado no se edita, tampoco lo que llegó por WhatsApp: corregir guarda otra respuesta que reemplaza a la anterior con el mismo número (CW10-A), y anular deja la respuesta con su número, marcada «Anulada» (CW11-A). Nada se borra ni se deshace: si se anuló por error, se vuelve a cargar. Pantallas y textos: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#corregir-y-anular-etapa-3--2026-11-05).

- **Quién (decisión 8).** Quien la cargó (`ActorUserId`), con «Completar formularios», mientras nadie la decidió: recibida o por aprobar, y siempre si el formulario no pide aprobación. Con «Corregir y anular respuestas» (`formrecords.amend`) junto con «Ver respuestas», cualquiera y en cualquier estado. Quien sólo ve respuestas, no. `FormRecordAmendmentGuard` decide para abrir, guardar, anular y para el `canAmend` del detalle: exige cuenta activa, revalida al terminar bajo el lock de definiciones y no toma la pérdida del permiso como si la respuesta fuera propia. Una respuesta ajena sin el permiso da 403 a quien la lee y el mismo 404 que una inexistente a quien sólo ve lo propio; una reemplazada, una anulada o una propia ya decidida dan 409 `Forms.Record.AmendUnavailable`.
- **Corregir es una carga más.** `GET /api/forms/entries/{definitionId}/records/{recordId}` abre la versión activa, como la carga, con la respuesta llevada a esa versión: los valores, las opciones de catálogo elegidas y los archivos. Las opciones (`.../records/{recordId}/versions/{versionId}/questions/{fieldId}/options`) usan los filtros por persona de quien la cargó, no de quien corrige. `POST .../records/{recordId}` recibe el multipart de la carga más `reason` (obligatorio, hasta 500 caracteres; `Forms.Record.ReasonRequired` o `ReasonInvalid`) y `keptFileIds`, los archivos de la anterior que siguen. Las tres rutas se abren con «Completar formularios» o con el permiso nuevo (`HasAnyPermission`) y el guard decide. Valida como la carga (tipos, obligatorias, opciones activas, rango de «elegir varias»); los archivos que siguen y los nuevos cuentan juntos para el mínimo y máximo de cada pregunta y el tope de 10. El mismo `requestId` con el mismo envío (respuestas, motivo y archivos) devuelve el primer resultado.
- **El reemplazo.** La corrección copia el número, el autor, la fecha de recepción y el canal de la anterior, y guarda `ReplacesRecordId`, `OriginalReceiptId` (el recibo de la primera respuesta, que da el origen y el canal), quién corrigió y el motivo; la anterior guarda `ReplacedByRecordId`. El número es único entre las respuestas originales y cada enlace es único, así una respuesta tiene una sola corrección vigente. Los archivos que siguen se enlazan también a la corrección (`BusinessFormRecordFiles` es único por archivo y respuesta) y la anterior conserva los suyos.
- **Vuelve a pasar por las reglas.** La corrección corre las reglas de «Cuando llega una respuesta» con los valores nuevos: los avisos salen otra vez y, si el formulario pide aprobación, queda «Por aprobar» aunque la anterior estuviera aprobada; el PDF nuevo sale al aprobarla. La tarea abierta de la anterior se cierra (`Closed`, sin decisión) y lo que ya se avisó, aprobó o imprimió queda en su historia. Un pedido de aprobación de una respuesta reemplazada o anulada que todavía no corrió queda como no aplicable.
- **Anular.** `POST /api/forms/definitions/{definitionId}/records/{recordId}/void` con `reason` (las mismas reglas del motivo) responde 204: la respuesta queda `Voided` con quién, cuándo y por qué, y su tarea de aprobación abierta se cierra. Los avisos y el PDF ya emitidos siguen en su historia.
- **Lecturas y conteos.** La planilla, las facetas, la navegación y el Excel nunca traen las reemplazadas y dejan afuera las anuladas, salvo con `state=Voided`. El Excel suma las anuladas con `includeVoided` y, sin filtro de estado, una columna «Anulada». «Formularios» cuenta las respuestas vigentes y no anuladas, y la última sale de ellas (ya no de `LastRecordNumber`). La fila de una corrección trae `corrected: true`; el detalle trae `correction` (quién, cuándo, por qué y cómo había terminado la anterior: estado, decisión y PDF), `replacedBy` en la reemplazada, `void` en la anulada y `canAmend` para quien puede cambiarla ahora. Esas propiedades se omiten cuando no aplican, así los cuerpos anteriores no cambian. Una reemplazada se lee por su enlace, como la «Versión anterior» de su número.
- **El chat de WhatsApp.** La respuesta que llegó por el chat lleva `amendment` (`Replaced` o `Voided`): la tarjeta conserva lo que llegó y dice al pie que después se corrigió o se anuló.
- **La migración** `AddFormRecordCorrections` agrega las columnas, los enlaces, los índices y los controles de consistencia. Su `Down` se niega si ya hay correcciones, anuladas, tareas cerradas o archivos compartidos, porque volver atrás los perdería.

## Títulos de sección — Etapa 5, 2026-11-05

El autor ordena un formulario largo con títulos entre las preguntas (CW15-B). Un título no es una pregunta: no se responde ni cuenta entre las 40. En la app arma una zona por título y en WhatsApp sale como subtítulo; la planilla, el detalle, el Excel y el PDF no cambian. Pantallas y textos: [carga desde la app](../../../ArquitecturaBaseFront/docs/design/carga-desde-la-app.md#títulos-de-sección-cw15-b--2026-11-05).

- **El borrador.** `draft.sections` lista los títulos en orden, cada uno con `start` (cuántas preguntas van antes) y `title` (es/en). El original es obligatorio y la traducción, opcional; hasta 80 caracteres (el tope del subtítulo de WhatsApp) y hasta 40 títulos. `start` va de 0 a la cantidad de preguntas, nunca baja y no cae entre el bloque de renglones y sus preguntas; un título sin preguntas debajo se guarda igual. Sin títulos la lista se omite, así borradores, versiones, huellas y comprobantes anteriores conservan sus bytes. Los títulos no tienen identidad: el editor les da una propia que no viaja.
- **Publicar.** Frena `Forms.Publication.SectionEmpty` en `draft.sections[i]` un título sin preguntas debajo, antes del siguiente título o del final. Un título sin traducción deja ese idioma incompleto, como una etiqueta (`draft.sections[i].title.<idioma>`). La versión sellada (`FormVersionSchema.Sections`) y el pedido al canal (`StructuredFormCheckRequest.Sections`) llevan los títulos; «Qué se publica» no los lista.
- **WhatsApp.** El perfil `flow-static-7.3-r10` pone un `TextSubheading` justo antes de la primera pregunta de cada sección, en la pantalla de esa pregunta; una sección que empieza con el bloque de renglones lo lleva arriba de su primera pantalla («¿Agregás un renglón?» o el renglón 1). No suma pantallas ni cambia el prellenado ni las respuestas. El subtítulo admite hasta 80 caracteres y necesita su traducción (`TextTooLong` o `MissingTranslation` en `draft.sections[i].title.<idioma>`), cuenta entre los 50 componentes de su pantalla y un diagnóstico de Meta sobre él señala su título. Sin títulos, r10 compila los mismos bytes.
- **La carga.** `GET /api/forms/entries/{definitionId}` suma `section`, el título en el idioma del contenido, a la pregunta donde empieza cada sección. La página y el panel abren una zona plegable por título, en el orden del editor: lo que va antes del primer título sigue en «Datos», y las fotos y los documentos van al final, en «Fotos y documentos» (AR1-A), así que una sección de sólo archivos no arma zona.

## Cálculos — CA1-A, 2026-11-09

Un cálculo es una pregunta de número de sólo lectura que calcula el servidor (CA1-A), como el importe de cada ítem y el total de
una factura. Es parte del [motor para documentos](../plans/2026-11-09-arca-etapa-2-motor.md) de ARCA. Pantallas:
[motor para documentos](../../../ArquitecturaBaseFront/docs/design/motor-para-documentos.md).

- **El borrador.** Una pregunta `decimal` con `calculation`: `operation` (`multiply`, `sum` o `percent`) y `operands`, las
  preguntas que usa, por `fieldId` o, si son nuevas, por su `localId` UUID, que el guardado sella. Multiplicar y porcentaje
  (base y alícuota) llevan dos datos y la suma, de uno a veinte, todos distintos y ninguno la misma pregunta. No puede ser
  obligatoria. Prender o cambiar el cálculo conserva la identidad de la pregunta. Sin cálculo la propiedad se omite, así las
  preguntas, versiones y huellas anteriores conservan sus bytes.
- **Publicar.** Frena `Forms.Publication.CalculationInvalid` en `draft.fields[i].calculation` si un dato no existe, va después o
  no es un número (un decimal u otro cálculo), o si no respeta su alcance: multiplicar y porcentaje usan las del mismo ítem si el
  cálculo está en el bloque y las de afuera si no; la suma va afuera del bloque y suma una pregunta del bloque en todos los
  ítems. La alícuota de un porcentaje puede ser una elección de un catálogo entero o decimal, cuyo valor es la alícuota; con un
  catálogo de texto o de fechas, frena. Las opciones de la carga traen su `value` en un catálogo con valores, así ese porcentaje
  también se ve al cargar.
- **La captura** (app y WhatsApp, `FormRecordAnswerPolicy`) calcula cada uno en orden y lo guarda como una respuesta decimal más,
  redondeada a 2 decimales con half-even (`MidpointRounding.ToEven`): el total suma los importes ya redondeados. Un producto o
  un porcentaje con un dato vacío queda vacío; la suma suma lo que haya y queda vacía sin valores. Una respuesta enviada para un
  cálculo es inválida, también dentro de un ítem. Corregir recalcula.
- **La carga.** `GET /api/forms/entries/{definitionId}` trae `calculation` en la pregunta, para la vista previa con el mismo
  redondeo; el envío no lo manda.
- **Lo demás lo lee como un número:** planilla, «Filtros», Excel, PDF, reglas y avisos. El asistente no lo prellena, una
  sugerencia no lo tiene de destino y la IA no lo propone.

## Condiciones — CO1-A, 2026-11-09

Una pregunta se muestra según la respuesta de otra (CO1-A), como el período de una factura sólo si es un servicio.

- **El borrador.** `showWhen` con `questionId` (la pregunta, por `fieldId` o por su `localId` UUID) y `values`, de una a
  cincuenta respuestas distintas que la muestran: el id de una opción propia (una nueva, por su `localId`, que el guardado sella),
  el id de una opción de catálogo o `"true"`/`"false"`. Ni el bloque ni sus preguntas llevan condición, y una pregunta no se
  nombra a sí misma. Sin condición se omite.
- **Publicar.** Frena `Forms.Publication.ConditionInvalid` en `draft.fields[i].showWhen` si la pregunta no va antes, está en el
  bloque o no es de elegir una (propia o de catálogo) o de sí o no, o si una respuesta no es suya.
- **La captura.** Oculta, la pregunta no es obligatoria y no puede traer respuesta: una con valor rechaza el envío. Un cálculo la
  toma vacía y las reglas, sin responder. Mostrada, sigue sus reglas.
- **La carga** trae `showWhen` en la pregunta; la app la muestra u oculta al cambiar la respuesta y no manda lo escrito en una
  oculta. Un título de sección sin preguntas a la vista también se oculta.
- **WhatsApp** la muestra con la misma condición (perfil r13, ver [WhatsApp](whatsapp.md)), menos varias respuestas de una
  pregunta de catálogo y una cadena de más de tres, que frenan su publicación con su propio error
  (`WhatsApp.Flow.ConditionCatalogValues` y `WhatsApp.Flow.ConditionTooDeep`); probado con Meta el 2026-11-11. En un formulario
  que va por WhatsApp, el editor no deja elegir más de una respuesta de una pregunta de catálogo, con su motivo, y avisa debajo
  una cuarta condición en cadena, con «Sólo en la app» como salida. El asistente no prellena una pregunta con condición y una
  sugerencia no la tiene de destino.

## Resultado externo — RE1-A, 2026-11-09

Lo que devuelve una acción externa, como el comprobante que autoriza ARCA, se guarda en la respuesta (RE1-A). La acción que lo
trae es de la etapa 3 (`IssueDocument`, [ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)).

- **Qué se guarda.** Una fila por run en `BusinessFormRecordResults` (única por run y del run de esa respuesta): la fuente
  (`arca.invoice`), su nombre por idioma y los datos, cada uno con clave, nombre por idioma, tipo (`text`, `date` o `number`) y
  valor canónico, sellados al confirmar, así se leen igual aunque se quite el módulo. Los trae el DTO neutral
  `StructuredRecordResult`; los guarda `FormRecordResultRecorder` en la transacción que confirma el run, bajo el fence vigente.
  Un resultado mal armado es un error del módulo que lo produjo: lanza y no se guarda nada.
- **El detalle** (`GET api/forms/records/{id}`) trae `hasIssuance: true` si la respuesta tiene una emisión (una que se aplicó,
  también su reversión, o un resultado), para que la zona «Comprobante» guarde su lugar mientras llega el flujo, que se lee
  aparte. Trae `results` en el idioma del contenido, en el orden en que llegaron; sin
  resultados se omite. El front lo muestra como una zona con el nombre de la acción, arriba de «Datos».
- **La planilla** trae `resultColumns` (una por dato, con id `<fuente>.<clave>`, después de las preguntas, con los nombres del
  último resultado de cada fuente) y `results` en cada fila; sin resultados se omiten. No se filtra ni se ordena por ellos.
- **El Excel** suma esas columnas después de las preguntas, con celdas del tipo de cada dato; sin valor, vacía. Con «Solo
  las visibles», el pedido nombra en `resultColumns` los ids que se ven y van sólo esos; sin `resultColumns`, van todas. Un
  id que ya no está se deja afuera, porque un resultado nuevo puede cambiar los datos de su fuente después de leer la
  planilla (2026-11-11).
- **La copia PDF** pone una zona por resultado arriba de las respuestas, con el nombre de la acción y sus datos escritos como
  una respuesta de su tipo.
- **Los avisos.** Un dato del resultado se escribe `{result:<fuente>.<clave>}` y sólo vale en un aviso que va después, en la
  misma regla, de una acción que lo declara; si no, `Forms.Rule.PlaceholderInvalid`. El texto se arma al recibir con el dato
  marcado y se completa al mandar, en el idioma de quien lo recibe (fecha sólo con números, número con sus decimales); sin resultado queda
  vacío. Las respuestas citadas no pueden traer la marca.
- **La migración** es `AddFormRecordResults`.

## Emitir un documento — IssueDocument, 2026-11-09

La acción de regla «Emitir comprobante» (`IssueDocument`) entrega los datos atados de la respuesta a un emisor instalado
(`IStructuredRecordIssuer`, colección opcional en `Application/Interfaces/Channels`). ARCA aporta «Emitir comprobante en
ARCA» ([ADR 0028](../decisions/0028-emision-de-comprobantes-arca.md)); sin emisores, la acción no se ofrece.

- **Dónde vale.** Sólo en un formulario copiado del estándar del emisor, con su `issuerId`: una por regla, en «Cuando llega»
  o «Cuando se aprueba» y antes de los avisos, que pueden usar los datos de su resultado. Las opciones del editor traen
  `issuers` (el nombre y los datos del resultado) sólo en esos formularios; si no, `Forms.Rule.IssuerUnavailable`. El
  `issuerId` se guarda en la acción y se omite en las demás.
- **El run.** Pregunta primero al emisor y encola una sola vez por run, que es la clave del pedido: un run que perdió su
  resultado vuelve a preguntar sin emitir dos veces. Encola sólo si la respuesta sigue en pie (ni anulada ni corregida); si
  no, «No aplica». Mientras el emisor trabaja queda `Running` y vuelve a mirar cuando el emisor dice, nunca antes de 5
  segundos. Si espera por su proveedor, quien cargó la respuesta recibe una sola vez el aviso que da el emisor, en su idioma
  y por el primer canal que lo alcanza; sin canal, no hay aviso. El aviso va con un id propio del run
  (`FormIssueConfirmationPolicy.NoticeId`), no con el run, que puede ser ya el id del «¿La emito?» de una respuesta de un
  canal: así llega también después de ese mensaje, y una sola vez aunque el run pierda la reserva y vuelva a preguntar. Mientras espera, la acción del flujo
  (`GET api/forms/records/{id}/workflow`) trae, además de `Running`, el motivo de la espera en `reasonCode` (y su texto en
  `reason`), `nextAttemptAtUtc` (cuándo vuelve a probar el emisor) y `attempts` (cuántas veces probó, si las cuenta): la banda
  «En cola»; sin espera, no trae ninguno. Emitido, termina `Completed` y guarda el resultado en la misma transacción; rechazado, `Failed` con el código del
  emisor (reintentable si no se encoló nada) y, en `reason`, lo que dijo el proveedor tal como lo da el emisor
  (`FailureDetail`, «10015 · El número de documento del receptor no es válido.»), también en el run de reversión (evento
  `Voided`); sin eso, `reason` es el texto del código. En revisión, `Uncertain` con el motivo del emisor y, en `review`, los
  textos que da el emisor por idioma (`title`, `detail`, `proceed` y `advice`): el front no tiene textos de un emisor. Sin el emisor,
  `Forms.Action.IssuerUnavailable`; sin los datos atados, `Forms.Action.IssueDataUnavailable`; si una persona decidió no
  emitir, `Forms.Action.NotIssued`.
- **El cliente nuevo, con «Emitir».** Si al encolar el emisor completó a alguien escrito en la respuesta (el cliente de
  «Otro: escribir la CUIT»), lo devuelve con la clave del catálogo de su estándar (`CatalogItem`) y formularios lo suma, en
  la transacción que guarda el run, como opción activa de ese catálogo: la CUIT como código, el nombre como texto (los primeros
  30 caracteres; uno más largo va entero en el detalle) y los campos que el catálogo tiene activos (uno de tipo catálogo, con
  la opción activa del código que dio el emisor; un valor que no entra en su campo se descarta), con el lock del catálogo y
  sus fuentes y una revisión nueva. No se suma si el catálogo no existe o no es Manual, si ya hay una opción con ese código o
  con esa CUIT en su campo (comparada por letras y dígitos) o si la opción no entra (un campo obligatorio sin valor, un valor
  único repetido, las 10.000): la emisión sigue igual.
- **La revisión.** `POST api/forms/records/{id}/actions/{runId}/review` sin `recipientUserId`: `wasSent` dice si sigue (otro
  número o la fecha de hoy, según el motivo) o no se emite. Pide `formrules.manage` y el permiso del emisor (`arca.issue`), y
  el flujo marca `canReview` sólo para quien los tiene. Sólo se revisa una emisión que el emisor dejó en revisión (el último
  resultado del run es `NeedsReview`): mientras el run espera la confirmación de quien cargó (`Forms.Action.AwaitingConfirmation`),
  el flujo no marca `canReview` y la revisión da 409 `Forms.Action.StateConflict`, así nadie la emite sin el «¿La emito?». La
  misma decisión repetida es un replay. La decisión vale para la revisión de la emisión que vio el run. Con
  `false` («No emitir»: «La respuesta queda anulada, sin factura.»), el run lleva la decisión al emisor fuera de toda
  transacción y, cuando el emisor confirma que no la emite (`Cancelled`), la transacción que confirma el run anula la
  respuesta con el motivo «No se emitió», en nombre de quien decidió, y cierra su aprobación abierta, como el «No emitir» del
  «¿La emito?»; el run queda `Failed` con `Forms.Action.NotIssued`. El motivo se guarda como ese código
  (`BusinessFormRecord.VoidReasonCode`, sin texto) y el detalle lo lee en el idioma de quien lo pide. Si el
  emisor no la toma (la revisión cambió), la emisión sigue en revisión y la respuesta no cambia. La revisión de un aviso sin
  `recipientUserId` da 400 en ese campo.
- **Los datos y el PDF fiscal.** Una opción de catálogo llega al emisor por su código, y un campo capturado que nombra otra
  opción, por el código de esa opción. Cada casilla de una pregunta atada (IV2-A, ver [la biblioteca](#biblioteca-de-estándares--bi1-a-2026-11-09))
  llega como un dato más con su clave y `"true"` o `"false"`, como la marcó la versión con que se cargó la respuesta, aunque la
  pregunta quede sin responder. Emitido, el PDF fiscal queda como un documento más de la respuesta (en español, sin
  diseño) y «Avisar a quien cargó» puede adjuntarlo, como el de «Armar el PDF».
- **La copia por correo (`SendCopy`, 2026-11-11).** Va después de un `IssueDocument` de su regla, con un texto como el de un
  aviso (el original obligatorio, los datos `{result:…}` de la emisión), sin rol ni casilla de PDF. Su run fija el texto en el
  idioma original del formulario, el emisor de la regla y el PDF de esa emisión. Al correr, el emisor dice adónde va con los
  datos atados de la respuesta (`IStructuredRecordIssuer.CopyRecipient`); sin dirección, o si la emisión no dejó PDF, queda «No
  aplica». Si hay, se lee el PDF fiscal (tamaño y SHA-256 confirmados) y el correo (asunto: el nombre del documento, sin
  `.pdf`; cuerpo: el texto con los datos del resultado; el PDF adjunto) entra en la cola durable de correo de la app en la
  misma transacción que confirma el run: sale una sola vez. Si la cola no lo toma, `Failed` con
  `Forms.Action.CopyNotQueued`, para reintentar. El correo no queda en el resultado del run.
- **«¿La emito?» (EM2-A).** Una respuesta que llegó por un canal no se emite sin que quien la cargó la confirme. Antes de
  encolar, el run pide al emisor la vista previa (`PreviewAsync`: el comprobante que decidió, el receptor y el total, sin guardar
  nada, con el porqué, la fecha y los importes que discrimina). Si se puede, le manda a quien cargó «Factura C para … por $ …, con fecha ….», con los importes que discrimina entre
  paréntesis después del total (IV3: «por $ 1.210,00 (Importe neto gravado: $ 1.000,00; IVA 21%: $ 210,00)»), y «¿La emito?», con los botones «Emitir» y «No emitir» (`Choices` del mensaje, por el primer canal que lo alcanza)
  y queda `Uncertain` con `Forms.Action.AwaitingConfirmation`. El flujo trae `confirmation` (el run, el comprobante, el
  receptor, el total, `info`, `date` y `amounts`) sólo para quien la cargó, y `POST api/forms/records/{id}/actions/{runId}/confirmation` con
  `{"issue": true}` la emite o con `false` anula la respuesta con el motivo «No se emitió» (guardado como el código
  `Forms.Action.NotIssued`, que el detalle lee en el idioma de quien lo pide), sin ir al emisor. El botón del canal llega por
  `IStructuredRecordActionReplySink` y decide lo mismo; un botón viejo o ajeno se ignora. Decide sólo quien la cargó, con los
  permisos que tiene ahora, revalidados bajo el lock también desde el botón (los de `FormRecordAmendmentGuard`: «Completar
  formularios», o «Corregir y anular respuestas» con «Ver respuestas», y una cuenta activa): sin ellos el botón se ignora y la
  ruta da 403, y la respuesta de otra persona da 404 a quien sólo ve lo suyo y 403 a quien ve respuestas. Una respuesta que ya
  no está en pie (anulada en la app mientras esperaba) no trae `confirmation`, la decisión da 409 y su emisión termina «No
  aplica»: la anulación la suelta. Después de «No emitir» el
  dueño le devuelve al canal lo que contesta, una sola vez y en el idioma de quien la cargó: «No la emitimos. FAC-000012 quedó
  anulada.», con el prefijo y el número de la respuesta; después de «Emitir» no se contesta nada en el acto, porque el aviso con
  el PDF lo manda «Avisar a quien cargó». **Si la vista previa falla** (decisión 4: un cliente nuevo que el padrón no tiene o
  un padrón que no responde, o cualquier otro motivo del emisor), el run espera igual (`Uncertain` con
  `Forms.Action.AwaitingConfirmation`) y le manda a quien cargó «Todavía no puedo emitirla: » con el motivo en su idioma (el
  texto de `Errors.resx` del código del emisor) y los botones «Volver a probar» y «No emitir». `confirmation` trae entonces
  `failureCode` y `failureMessage` (el motivo en el idioma del pedido), sin comprobante, receptor ni total, y la ruta con
  `{"issue": true, "retry": true}` es «Volver a probar»: el run vuelve a pedir la vista previa y sigue como siempre, con el
  «¿La emito?» si sale o el mismo aviso si vuelve a fallar. «No emitir» anula igual. La decisión tiene que ser sobre lo que el
  run está mostrando: `issue` sin `retry` después de una vista previa que falló, o `retry` después de una que salió, da 409
  (y el botón viejo del canal se ignora). El canal guarda un mensaje por id y persona: el primero va con el run como id y el
  de cada «Volver a probar», con un id que sale del run y de la revisión de esa decisión (`FormIssueConfirmationPolicy.MessageId`),
  así un run que perdió su reserva y vuelve a preguntar repite el mismo en lugar de mandar otro; como el canal devuelve el id
  del mensaje, los botones de esos mensajes nombran el run (`retry.`, `issue.` o `skip.` y el run en Base64URL, 28 caracteres
  como mucho). Lo cargado en la app ya se
  confirmó antes de «Enviar» y se emite directo: la carga trae `issuePreview` cuando enviar emite (formulario del estándar del
  emisor y reglas vigentes con `IssueDocument`), y `POST api/forms/entries/{id}/issue-preview` con `versionId` y las respuestas
  sin enviar (las del envío, sin archivos) pasa por la misma preparación que el envío y devuelve `document`, `receiver` y
  `total`, con `info` (el porqué del comprobante, la «(i)»), `date` y `amounts` (cada importe con su etiqueta por idioma), o
  `failureCode` y `failureMessage` si no se puede emitir así. No guarda nada y usa el cupo de lectura del módulo: la app la pide
  también mientras se carga, para el total discriminado (IV1-A), y nunca le saca cupo a «Enviar». Si el emisor consulta un
  padrón (el cliente escrito por su CUIT), lo limita y lo guarda en caché el módulo que lo aporta. Un
  formulario que no emite da `Forms.Entry.NoIssuance` (404).
- **Corregir y anular (NC1-A).** Una respuesta con una emisión viva (emitida, en curso, en revisión o esperando la confirmación de
  quien cargó: una sola definición, `BusinessFormActionRun.LiveIssuance`, que usan la anulación, la corrección y los lectores) no se corrige:
  `Forms.Record.IssuedNotAmendable`, se anula y se carga otra. Anularla pide además el permiso del emisor y no la anula en el
  acto: cada emisión recibe un run de reversión (evento `Voided`) que el worker lleva fuera de toda transacción. Lo que nunca se
  emitió se cancela, si todavía se puede, y la respuesta queda anulada; lo emitido se revierte por el total (la nota de crédito)
  y la respuesta queda anulada, con el motivo y la persona de la anulación, cuando la reversión se emite. Una emisión con número
  todavía sin resolver hace esperar a la reversión. Si el emisor rechaza la reversión, la respuesta sigue vigente y se puede
  volver a anular, con otra clave. Mientras dura, la actividad muestra la reversión («Anulándose») y anular otra vez da
  `Forms.Record.Voiding`. Toda anulación (la de la app, la de «No emitir», la de una revisión que no emite y la de una reversión
  que termina) pasa por una sola pieza, `FormRecordVoidRecorder`, en la transacción de quien anula: deja la respuesta anulada con
  su motivo (un texto, o el código de un motivo que decide el sistema), cierra su aprobación abierta y suelta las emisiones que
  esperaban la confirmación de quien cargó, que terminan «No aplica». Una respuesta ya anulada conserva su primera anulación:
  anularla otra vez no cambia nada ni falla, así la reversión que termina después de un «No emitir» se completa en lugar de
  quedar reintentándose.

## Catálogos que mantiene un módulo — External, 2026-11-11

Un módulo instalado aporta listas que formularios guarda como catálogos de sólo lectura (`IStructuredCatalogFeed`, colección
opcional; ARCA aporta sus tablas). Son de un tercer tipo, `External`, con la clave estable que da el origen («arca.concepts»).

- **Se crean solos.** El primer día que el origen responde: una instalación que nunca conecta el módulo no tiene catálogos
  vacíos. Van a la carpeta del sistema que nombra el origen (por clave, como la biblioteca).
- **Se actualizan solos.** El worker `FormCatalogFeedBackgroundService` (`BusinessForms:CatalogFeedWorker`: `ProcessInBackground`
  y `PollInterval`, 5 minutos) mira cuáles tocan: los que nunca se leyeron, los leídos hace más de un día y los que fallaron
  hace más de 15 minutos. Leer no tiene efecto afuera ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)): una
  reserva corta evita que dos hosts pregunten a la vez y las diferencias se aplican con la revisión del catálogo. Lo que deja de
  venir se desactiva, nunca se borra: las respuestas lo siguen nombrando. Una falla guarda el motivo y deja lo que había. El
  lock de la clave (`forms:catalog-key:<clave>`) serializa la creación y va antes del lock del catálogo, que se toma antes de
  leerlo.
- **Lo que no entra en una opción no se guarda.** Una opción admite hasta 30 caracteres (`StructuredFeedItem.MaxTextLength`,
  el mismo de una opción a mano): un origen que manda un texto más largo no cambia nada y la falla queda con el código
  `Forms.Catalog.FeedTextTooLong`. ARCA corta con «…» las descripciones que no entran, y su código sigue siendo el que viaja.
- **No se tocan a mano.** Editar o importar da `Forms.Catalog.ReadOnly` (409); el detalle trae `feed` (cuándo respondió y la
  última falla, con su código) y el listado filtra `kind=External` y cuenta `byKind.external` cuando hay alguno.
- **Se usan como un Manual.** Ofrecen sus opciones en la app y en WhatsApp, se publican y se capturan igual, con su valor tipado,
  y pueden ser la fuente de un campo de catálogo. Como no tienen campos, no filtran ni eligen primero por un campo. El asistente
  los ve como Manual y no puede importarles una planilla.

## Completar con un padrón — CA1, 2026-11-11

Un módulo instalado completa una opción de un catálogo que trae su estándar a partir de la CUIT (`IStructuredPartyLookup`,
colección opcional; ARCA aporta «Completar con ARCA» para los «Clientes» de la «Factura»). El padrón dice qué catálogo completa,
por su clave (`arca.customers`), y qué campo tiene la CUIT; sin padrones, ningún catálogo lo ofrece.

- **Si se ofrece.** `GET api/forms/catalogs/{id}/lookup` devuelve el nombre del padrón por idioma y el campo de la CUIT, para
  poner el botón al lado; en otro catálogo, o si el campo no está activo, `Forms.Catalog.LookupUnavailable` (404).
- **Completar.** `POST api/forms/catalogs/{id}/lookup` con `{"taxId"}` pregunta al padrón fuera de toda UoW y devuelve `text`
  (el nombre) y `fields` (cada campo activo que el padrón conoce, con su valor; uno de tipo catálogo, con la opción activa del
  código que dio el padrón), o `failureCode` y `failureMessage`. No guarda nada: la persona revisa y guarda la opción como
  siempre. Las dos rutas piden «Administrar catálogos». Los cupos de formularios no cuidan al padrón: el módulo que lo aporta
  limita y guarda en caché sus consultas (ARCA: CUIT distintas por persona y por minuto, y diez minutos de caché).

## Biblioteca de estándares — BI1-A, 2026-11-09

Los módulos traen formularios estándar como datos y «Agregar» crea un borrador propio que recuerda de dónde viene (BI1-A,
[ADR 0029](../decisions/0029-biblioteca-de-estandares.md)). La factura llega con la etapa 3; sin estándares, «Biblioteca» no
aparece.

- **Los estándares.** `IStructuredFormStandardSource` (colección opcional): clave, versión, nombre y descripción por idioma,
  prefijo sugerido, el formulario en el formato del borrador (`localId` UUID, con las casillas de sus preguntas atadas), qué
  pregunta guarda cada dato (`Bindings`), los catálogos por su `Key`, la opción con que empieza una pregunta de catálogo
  (`Defaults`) y la carpeta del sistema.
- **Leer.** `GET api/forms/library` («Ver formularios») lista los estándares en el idioma del menú, con cuántos formularios
  salieron de cada uno y el prefijo que ofrece «Agregar» (`suggestedPrefix`): el del estándar o, si un formulario ya lo tiene, el
  primero con un número después que ningún formulario usa, también los eliminados, como mira el guardado.
- **Agregar.** `POST api/forms/library/{key}/copies` («Administrar formularios») recibe `requestId`, nombre y prefijo y crea el
  borrador por el guardado de siempre: el mismo pedido devuelve el mismo borrador. La definición sella una vez el estándar, su
  versión y `dato → fieldId`, que el detalle trae en `standard`. Un estándar que no está da 404
  `Forms.Library.StandardNotFound` y un catálogo que falta o está inactivo, 409 `Forms.Library.CatalogUnavailable`.
- **Un catálogo propio del estándar** (`OwnCatalogs`, como «Clientes» de la factura) se crea Manual, con su `Key`, sus
  campos y sus primeras opciones (`Items`: código y texto por idioma, en orden, como «Consumidor final»), en la misma
  transacción que el borrador, si ningún catálogo tiene esa clave; si ya existe se usa tal como está, sin sumarle opciones, y
  si está inactivo frena con `Forms.Library.CatalogUnavailable`. Un campo de tipo catálogo nombra su fuente por `Key`.
- **Una condición sobre una pregunta de catálogo** nombra sus opciones por código (`code:<código>` en `showWhen.values`),
  porque los ids de las opciones son de cada instalación: «Agregar» los cambia por el id de la opción activa con ese código,
  también en un catálogo propio creado en el mismo guardado. Un código que el catálogo no tiene frena con
  `Forms.Library.CatalogUnavailable`.
- **La opción con que empieza una pregunta de catálogo atada (IV1-A).** El estándar la declara por código (`Defaults`:
  `localId → código`, como «Alícuota» en 21 %, el código 5) sólo para una pregunta de elegir una de un catálogo, y «Agregar» la
  cambia por el id de la opción activa con ese código, igual que una condición (un código que el catálogo no tiene frena con
  `Forms.Library.CatalogUnavailable`). La copia la guarda en `source.default` y no se cambia. La carga desde la app
  (`GET api/forms/entries/{definitionId}`) la trae en `catalog.default` (id, título y descripción, como las opciones) mientras
  los filtros de quien carga la ofrecen y la lista tiene más de una opción; la app la trae elegida en la pregunta y en cada ítem
  nuevo del bloque, y se puede cambiar. Una lista que depende de otra respuesta no la trae. WhatsApp no la muestra: el emisor
  completa la que falta (ARCA, 21 % en la A y la B).
- **Las casillas de una pregunta atada (IV2-A).** El estándar declara en el borrador, en `toggles` de una pregunta atada, sus
  opciones de sí o no: clave, texto y (i) por idioma y el valor con que empiezan, como «El precio incluye IVA» de «Precio
  unitario», sin marcar. La copia las guarda en la pregunta y el editor muestra una casilla por cada una, debajo de «Se carga
  varias veces», con el texto y la (i) del estándar en el idioma del menú. Sólo se cambia su valor: cambiarles la clave o el
  texto, o sumar o quitar una, es `Forms.Draft.BoundQuestionChanged`. Cada una llega al emisor como un dato (ver [Emitir un
  documento](#emitir-un-documento--issuedocument-2026-11-09)) y cambiarla pide publicar otra versión. Una casilla puede traer
  también `whenOn` y `whenOff` (IV1-A): lo que dice la (i) de la pregunta en la carga de la app con el valor elegido, como
  «Sin IVA: la factura le suma el de su alícuota.»; la carga lo suma a la ayuda de la pregunta, después de la propia.
- **Sólo una pregunta atada** tiene casillas o una opción con que empieza: en otra, el guardado responde
  `Forms.Draft.QuestionConfigurationInvalid` en `draft.fields[i].toggles` o `draft.fields[i].source.default`. Las casillas
  llevan clave, texto y (i), sin repetir la clave y hasta diez; la opción con que empieza no va en una de elegir varias.
- **Las preguntas atadas** conservan todo menos su texto, su ayuda, su lugar, el valor de sus casillas y lo que reciben (W3):
  guardar el borrador
  rechaza quitarlas (en `draft.fields`) o cambiarles tipo, bloque, catálogo, la opción con que empiezan, opciones, cálculo,
  condición, renglones, casillas, largo o si son obligatorias (en `draft.fields[i]`), con `Forms.Draft.BoundQuestionChanged`. La
  migración es `AddFormDefinitionStandard`.
- **Las reglas que trae** (`Rules`, 2026-11-11): un estándar puede traer sus reglas en el formato de las reglas, con los
  `localId` de su borrador: el evento, las condiciones (una opción de un catálogo por `code:<código>` y una propia por su
  `localId`) y las acciones con su tipo (`IssueDocument` con su emisor, `NotifyAuthor` con su texto y su PDF, `SendCopy`).
  «Agregar» las deja como la revisión 1, activa, de las reglas de la copia, en el mismo guardado que el borrador y con los
  ids que ese guardado da (`FormStandardRuleRecorder`); el editor las muestra y las cambia como cualquier otra. Pasan el
  mismo chequeo que un guardado del editor, con los emisores del estándar: unas que no lo pasan son un error del módulo. Un
  código que el catálogo no tiene frena con `Forms.Library.CatalogUnavailable`, antes de guardar. La definición nueva y su
  revisión activa se apuntan entre sí: la clave que ata la revisión activa a su definición es diferida
  (`DeferActiveRuleSetKey`, ver [backend](../architecture/backend.md#migraciones)).
- **La carpeta.** «Agregar» recibe `folderId` opcional: la copia y los catálogos propios que crea van a esa carpeta y, sin
  ella, a la del estándar (`Folder`: su clave y su nombre por idioma), en la misma transacción que el borrador. La del
  estándar es una carpeta del sistema (ver [Carpetas](#carpetas--cp1-a-a-cp5-a-2026-11-09)); una elegida que se borró en
  el medio da 404 `Forms.Folder.NotFound`.

## Lectura de documentos y validaciones fijas — DOC1, 2026-11-11

Lo que un formulario necesita para leer documentos (DOC1, [documentos por WhatsApp](../plans/2026-10-22-documentos-por-whatsapp.md));
leerlos, cargarlos y la bandeja llegan con las tareas siguientes del plan.

- **La lectura en el borrador.** `draft.documentReading` es un ajuste del formulario, como «Dónde se completa»: si está prendida,
  «Cómo reconocerlo» (`recognition`, sin espacios alrededor, hasta 1.000 caracteres y obligatorio prendida) y hasta cinco cruces
  con catálogos (`lookups`): `questionId`, la pregunta de elegir una de un catálogo, fuera del bloque, que se completa;
  `sourceQuestionId`, otra pregunta del borrador cuya lectura se busca (un texto, un número o una fecha), y `field`, qué se
  compara del catálogo: `code` o `text` de la opción, sólo contra un texto, o el id de un campo. Un cruce nombra sus preguntas
  como un filtro: por `fieldId` o, si es nueva, por su `localId`. Sin lectura se omite, y apagada sin texto ni cruces también;
  apagada con texto conserva lo escrito. Va en `FormDefinitions.DocumentReadingJson`, con el borrador, y entra en la huella del
  chequeo y en la comparación con lo guardado.
- **Al publicar.** El chequeo frena un cruce cuyo campo no está activo en el catálogo de su pregunta o no se compara con lo leído
  (un texto con un texto, un número con un decimal, una fecha con una fecha): `Forms.Publication.DocumentLookupInvalid` en
  `draft.documentReading.lookups[i].field`. La aceptación sella la lectura prendida en `BusinessFormVersions.DocumentReadingJson`
  y el detalle dice `activeVersion.documentReading: true`. Los documentos los lee un canal (el editor oculta la lectura «Sólo en
  la app»): una publicación sólo en la app no revisa los cruces, que la frenarían sin nada en pantalla para corregir, ni sella la
  lectura, y el borrador conserva lo guardado. La migración es `AddFormDocumentReading`; su `Down` rechaza, antes del DDL, un
  borrador o una versión con lectura.
- **Las validaciones fijas.** Una pregunta atada de texto corto puede traer del estándar su `validation`: `bankAccount` o
  `taxId`. Sólo una atada la tiene (si no, `Forms.Draft.QuestionConfigurationInvalid` en `draft.fields[i].validation`), no cambia
  (`Forms.Draft.BoundQuestionChanged`), se sella con el esquema de la versión y vale en toda captura (la app, WhatsApp y las
  correcciones): una cuenta que no es una CBU o una CVU con sus dos verificadores, ni un alias de 6 a 20 letras, números, puntos o
  guiones, da `Forms.Record.BankAccountInvalid` («Revisá la cuenta: los números no dan. Copiala del comprobante.»), y una CUIT o
  un CUIL sin su verificador, `Forms.Record.TaxIdInvalid`, en su pregunta (y su ítem, en el bloque). No se guarda la respuesta,
  sólo el recibo rechazado; lo escrito que pasa se guarda tal cual, y la respuesta de la captura dice `fixedValidation` (desde la revisión de la
  tarea 9c también su replay: el recibo guarda `FixedValidation`), para que un canal pueda volver a mandar el formulario: WhatsApp lo hace desde la tarea 9c, con el motivo y
  lo que la persona mandó en un envío nuevo, y a un repetido (`repetition`) sólo le avisa
  ([WhatsApp](whatsapp.md#rechazos-y-reintentos-de-formularios--2026-10-13)). Desde la tarea 9b de documentos, una respuesta
  que su persona confirma en un canal desde un documento recibido o una fila (`origin.document` sin `documentRevision`) se vuelve a revisar
  al capturar con lo corregido: si los datos que marcan un repetido del estándar (`DuplicateKeys`, todos respondidos) coinciden con una
  respuesta guardada del formulario, da `Forms.Record.Repeated` y la captura dice `repetition` (su número y cuándo llegó; el recibo lo guarda en
  `RepeatedRecordNumber` y `RepeatedAtUtc` y su replay lo vuelve a decir, así un canal cuya propia UoW falló después de la captura igual
  contesta, una vez), así dos
  comprobantes mandados juntos no se guardan los dos (`FormRecordRepetitionPolicy`, en la UoW de la captura y después del lock del
  documento). Lo que pasa quien revisa desde la bandeja no se revisa: lo decide esa persona. Los valores son del núcleo (`Domain/ValueObjects`):
  `CuitNumber` y `BankAccountKey`, que da la cuenta normalizada, su tipo y el código de su entidad (los dígitos 1 a 3 de una CBU y
  los 4 a 7 de una CVU). Los dígitos pueden venir separados por cualquier espacio (también el que no corta) o guion (también la
  raya y el signo menos); 22 dígitos son siempre una CBU o una CVU y 11 una CUIT, que no es una cuenta: nunca se leen como alias,
  y una clave de puros ceros no vale. Si un alias puede ser sólo números no se pudo confirmar con el BCRA, así que se acepta con
  otro largo. Avisar el rechazo por WhatsApp y reenviar el formulario llega con la tarea 9.
- **«Comprobante de transferencia»** (`forms.transfer-receipt`, versión 1, prefijo `REC`) lo trae formularios como datos
  (`BusinessFormStandardSource`, con los JSON incrustados de `Resources/Standards`): 15 preguntas atadas en tres secciones
  (origen, destino y transferencia), con la cuenta y la CUIT o el CUIL de cada lado validados; monto, moneda (Pesos o Dólares),
  fecha y el comprobante (un documento) obligatorios, y la hora como texto corto de hasta 16 caracteres, como la escriba el
  comprobante. Los nombres de sus preguntas entran en un Flow de WhatsApp (hasta 20 caracteres en un texto, un número o una lista):
  una copia se publica también para WhatsApp. Va a su carpeta «Transferencias» con su catálogo
  «Bancos y billeteras» (`forms.bank-entities`), que «Agregar» crea si no existe con la tabla de entidades: el código de entidad
  como código, un nombre corto como texto y la denominación oficial como detalle. La tabla (`bank-entities.json`) lleva su fecha
  y su fuente: los 59 bancos del registro de cheques del BCRA (`api.bcra.gob.ar/cheques/v1.0/entidades`, leído el 2026-11-11),
  que deja afuera a los bancos digitales que no compensan cheques; los proveedores
  de CVU los asigna COELSA y no se pudo leer una lista oficial, así que no se cargó ninguno. El estándar suma `Reading`: «Cómo
  reconocerlo» y el resumen para WhatsApp por idioma (con datos entre llaves, `{amount}`), los datos que marcan un repetido
  (operación, monto y fecha) y la moneda de cada monto (`amount → currency`). «Agregar» deja la copia leyendo documentos, con
  «Cómo reconocerlo» en el idioma de la copia y las validaciones en sus preguntas.
- **La biblioteca** suma `reads` (`[{ key, name }]`: cada dato que lee un estándar con lectura, en el orden del estándar, por la
  clave del mapa `standard.bound` de su copia y con el nombre de su pregunta en el idioma del menú) y `validations`
  (`[{ name, kind }]`), sólo en el estándar que los tiene, y `upcoming`: los estándares de las etapas siguientes, sin borrador,
  que la página muestra como «Próximamente» (nota de crédito o débito, recibo, remito, ticket de gasto, orden de compra,
  presupuesto, cheque o eCheq, constancias, liquidaciones, carta de porte y DTE) y que un `IStructuredFormStandardSource` declara
  en `Upcoming`. Con formularios solo, «Biblioteca» ya aparece (`form-library`). Un estándar con una pregunta de foto o documento
  (el comprobante) se ofrece sólo con almacenamiento privado (`FormLibraryOfferPolicy`), sin el que su copia no se podría publicar:
  sin él no se lista y agregarlo da `Forms.Library.StandardNotFound`.
- **Cómo viene leyendo.** `GET api/forms/definitions/{id}/document-reading` («Ver formularios»; 404 si el formulario no está o se
  eliminó): desde dónde cuenta (`sinceUtc`, 30 días atrás), cuántos documentos se confirmaron (`documents`), por pregunta que la
  lectura prellenó (lo propuesto que la captura incluyó, `origin.includedFieldIds`) cuántos valores prellenó y cuántos se
  confirmaron sin cambios (`questions`: `questionId`, `read` y `unchanged`), la que más
  se corrigió (`mostCorrected`: `questionId` y `corrections`, o null) y las preguntas que marcan un repetido
  (`duplicateQuestionIds`, de `Reading.DuplicateKeys` por el mapa sellado de la copia; vacío en un formulario propio). Con «Ver
  respuestas», revalidado antes de responder, suma `examples`: las últimas diez correcciones, de la más nueva a la más vieja
  (`recordId`, `recordNumber` como «REC-000123», `confirmedAtUtc`, `questionId`, y lo leído y lo confirmado como texto, como lo
  muestra un aviso: el número en el idioma de quien pide, la fecha con números, sí o no y una opción por su texto); sin ese
  permiso la clave no va. Se calcula en SQL con las respuestas cuyo origen de captura trae el documento del que salieron
  (`origin.document`, que suma la tarea 9): hasta entonces cuenta cero y las listas van vacías.

## Carpetas — CP1-A a CP5-A, 2026-11-09

Las carpetas ordenan formularios y catálogos en un solo árbol, con subcarpetas, y un mismo elemento puede estar en varias.
Lo eligió el usuario en los tableros CP1 a CP5 (el [plan](../plans/2026-11-03-carpetas-de-formularios-y-catalogos.md)).
Están el backend y el front (2026-11-11).

- **Sólo ordenan.** No dan ni quitan acceso, y no hay permisos nuevos. Hasta cinco niveles; el nombre (hasta 80
  caracteres) no se repite en una misma carpeta sin distinguir mayúsculas. Profundidad, ciclos y nombres se revisan contra
  el árbol entero bajo el lock `forms:folders`, en una transacción por operación. Las tablas son `FormFolders` y
  `FormFolderMemberships` (migración `AddFormFolders`).
- **El árbol.** `GET api/forms/folders?kind=forms|designs|catalogs` lee el árbol de un listado con el permiso de ese
  listado. Quien administra (`formtemplates.manage` o `formcatalogs.manage`) ve todas las carpetas, también las vacías;
  los demás, sólo las que tienen algo visible para ellos, con sus madres.
- **Administrar** (`formtemplates.manage` o `formcatalogs.manage`): `POST api/forms/folders` crea, `PUT
  api/forms/folders/{id}` renombra o mueve, `GET api/forms/folders/{id}/deletion` cuenta subcarpetas, formularios y
  catálogos antes de borrar y `DELETE api/forms/folders/{id}` borra la carpeta con sus subcarpetas y sus membresías, nunca
  un formulario ni un catálogo. Renombrar, mover, borrar y el menú llevan la revisión de la carpeta y responden 409
  `Forms.Folder.RevisionConflict`.
- **Las carpetas de un elemento** (CP2-A): `GET`/`PUT api/forms/definitions/{id}/folders` y `api/forms/catalogs/{id}/folders`.
  Leer pide el permiso de lectura del elemento; guardar, el de administrarlo, y reemplaza el conjunto entero si sigue
  siendo el que se leyó (`expectedFolderIds`); si no, 409 `Forms.Folder.SetChanged`.
- **Los listados** (`api/forms/overview`, el de diseños y el de catálogos) reciben `folderId` (la carpeta con sus
  subcarpetas, con una consulta recursiva) o `withoutFolder`, nunca los dos. Se filtra en SQL antes de contar, ordenar y
  paginar, y un elemento aparece una sola vez. Un elemento borrado oculta sus membresías.
- **El menú** (CP5-A). `PUT api/forms/folders/{id}/menu` elige el lugar de una carpeta: ninguno, un menú propio con uno de
  doce íconos genéricos (la carpeta por defecto), «Gestión de formularios» o «Administración». `GET api/forms/menu` lo lee
  cualquier cuenta activa, ya podado: sólo formularios activos que esa persona puede abrir, en orden alfabético, sin nada
  vacío. Un menú propio lleva sus formularios y un grupo por subcarpeta con todo lo que está debajo (nunca más de dos
  niveles); una carpeta en un panel es un grupo con todos los formularios de abajo.
- **Las carpetas del sistema.** `FormSystemFolderLinker` archiva lo que crea el sistema (los catálogos de ARCA, la copia de
  un estándar) en la carpeta que recuerda por su clave, aunque la hayan renombrado o movido; si la borraron, la crea otra
  vez arriba de todo y fuera del menú, y adopta una del primer nivel con el mismo nombre y sin clave. Corre dentro de la
  transacción de quien lo llama y no tiene ruta propia.
- **Fuera del alcance:** las carpetas no viajan a WhatsApp, al asistente ni a «Formularios que faltan».

## Formularios sólo en la app — SA1-A a SA5, 2026-11-09

Un formulario puede completarse sólo en la app, sin pasar por WhatsApp. Lo eligió el usuario en los tableros SA1 a SA5 (el
[plan](../plans/2026-11-09-formularios-solo-en-la-app.md) y las [pantallas](../../../ArquitecturaBaseFront/docs/design/formularios-solo-en-la-app.md)).
Están el backend (2026-11-09) y el front (2026-11-11); los ejemplos HTTP de un formulario de la app ya se capturan y entran con la exportación conjunta del cierre.

- **El borrador.** `draft.appOnly` es «Dónde se completa»: `true` para «Sólo en la app»; si no, se omite (se guarda como false
  en `FormDefinitions.AppOnly`), así lo que existía conserva sus bytes y sus huellas. Se guarda con el borrador y su revisión,
  y entra en la comparación del chequeo y de la aceptación y en la huella del comprobante: un comprobante de un modo no sirve
  para el otro.
- **Sin canal.** Se publica sólo en la app cuando el borrador lo dice o cuando no hay ningún canal de formularios instalado
  (SA5): el chequeo ya no da `ChannelUnavailable`, que queda sólo para el estado de una publicación vieja cuyo canal no está.
- **El chequeo** no llama al canal y aplica sólo las reglas de la app: sin los ítems de WhatsApp (`RowsOverChannelLimit` y
  `MinRowsOverChannelLimit`), sin el tope de 30 caracteres del título de una opción, sin `DependsOnAnswer` y sin el exceso
  sobre 200 de `OptionsPerRecipientHigh` (sigue el aviso de que nadie ve una opción). Responde `LocallyValidated` y
  `Available` con `channels` vacío.
- **La aceptación** revisa lo local (almacenamiento de archivos, diez archivos, catálogos) y, sin efectos fuera del sistema
  ([ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md)), activa la versión en el mismo límite: la operación nace
  `Published` y el worker no la ve. La versión sella `AppOnly` y, como idiomas, el original y el secundario completo. El
  recibo dice `Published` (también al repetir el pedido); el de un canal sigue diciendo `Accepted`. Reactivar es igual.
- **Las lecturas.** `appOnly` aparece sólo cuando es true: en `publication.activeVersion` del detalle, en el listado (la versión
  activa o, sin versión, el borrador) y en el borrador; el ciclo de vida suma `publishedOnlyInApp` cuando toda versión fue de
  la app. Los idiomas de una versión de la app salen de los que selló, también al leer sus respuestas (planilla, detalle, correcciones, aprobaciones y acciones). `canPreview` exige una versión de un canal.
- **Los canales no la ofrecen.** `FormOfferableVersionQuery.ChannelVersions` deja afuera las versiones de la app en «Enviar
  formulario», el asistente (candidatos, prellenado y aclaración) y los destinos de una sugerencia; un destino que pasó a la
  app figura en las opciones como no disponible (`AppOnly`): «Qué sugerir después» lo muestra «Sólo en la app» y, como uno dado
  de baja, pide quitar la sugerencia para guardar. La carga desde la app no cambia. Un pedido por WhatsApp de un
  formulario de la app va a «Formularios que faltan».
- **El front** sabe que hay un canal por `form-channels` en `installedFeatures`.

## La pregunta «Ubicación» — UB1-A a UB4-A, 2026-11-11

Un formulario puede pedir la ubicación de quien lo completa, en el momento de completarlo: un punto, no un recorrido. Es lo
que informa el dispositivo; no prueba presencia y nada lo contrasta. El usuario eligió UB1-A a UB4-A el 2026-11-11 (el
[plan](../plans/2026-10-22-documentos-por-whatsapp.md#la-pregunta-ubicación) y las
[pantallas](../../../ArquitecturaBaseFront/docs/design/ubicacion-en-formularios.md)). La Etapa 1 es la app; la Etapa 2, por
WhatsApp, eligió W1-A el mismo día: la ubicación se pide antes del Flow ([reglas del canal](whatsapp.md#la-ubicación-antes-del-flow--w1-a-2026-11-11)).

- **El tipo.** `location` (`FormQuestionType.Location`) es sólo obligatoria u opcional: sin opciones, archivos, selección,
  largo máximo, cálculo ni condición propia, y nunca en el bloque de renglones. Cambiar el tipo de una pregunta a ubicación, o
  al revés, es otra pregunta. Ninguna condición ni cálculo se apoya en ella.
- **Por WhatsApp (W1-A).** El chequeo no la frena en un formulario que también completa WhatsApp: el canal la pide antes del
  Flow y la respuesta llega completa, con el punto, en una sola captura. Un formulario pide una sola ubicación por WhatsApp; la
  segunda frena con `WhatsApp.Flow.UnsupportedConfiguration`. El detalle de una respuesta que llegó así suma «Se pidió la
  ubicación» y «Llegó la ubicación» (o «Siguió sin la ubicación») antes de «Se mandó el formulario».
- **La respuesta** llega en el mapa de respuestas como el texto de un objeto `{"latitude":…,"longitude":…,"accuracyMeters":…}`:
  números, sin otras claves. La latitud va de -90 a 90 y la longitud de -180 a 180, redondeadas a seis decimales lejos del cero,
  como las coordenadas del negocio; la precisión es opcional, de 0 a 100.000 m, en metros enteros. Obligatoria es que estén la
  latitud y la longitud; vacía u omitida, sólo si es opcional. Un valor mal formado rechaza la captura entera y queda sólo el
  recibo.
- **Sin migración.** Se guarda en tres filas `DecimalValue` de la pregunta: `Position` 0 la latitud, 1 la longitud y 2 la
  precisión, si vino. `ValuesJson` lleva el objeto.
- **Las lecturas.** La planilla, el detalle y la corrección la leen como un dato (`latitude`, `longitude` y `accuracyMeters`
  si vino); la corrección la devuelve como el texto que manda el formulario. En los filtros de la planilla y en las condiciones
  de las reglas sólo valen «con respuesta», «sin respuesta» y «no se preguntó», y la planilla no ordena por ella.
- **Excel, PDF y mensajes.** El Excel la reparte en tres columnas numéricas, «<Pregunta> · Latitud», «· Longitud» y
  «· Precisión (m)»: vacías sin respuesta y «—» en las tres si la pregunta no existía en esa versión. El PDF escribe el punto
  («-34.603722, -58.381592», con punto decimal y seis decimales en los dos idiomas) y debajo «Ver en el mapa», un enlace del
  PDF a `https://www.google.com/maps?q=<lat>,<lng>`; la vista previa usa un punto de ejemplo. El mensaje de una regla lleva el
  punto y, en el renglón siguiente, ese enlace, porque WhatsApp no tiene texto con enlace.
- **El front.** En la carga, «Usar mi ubicación» pide el punto al tocarlo (alta precisión, sin una ubicación guardada y hasta 20 s)
  y lo muestra en un recuadro como un archivo, con la precisión (en ámbar con su (i) arriba de 100 m), la cruz para quitarlo,
  «Ver en el mapa» y «Volver a pedirla»; si el navegador no lo da, dice por qué. En el editor es el último tipo, sólo con
  «Obligatoria»; la planilla muestra el punto como el enlace al mapa, sin ordenar ni filtrar por él, y el detalle, con «Ver en el
  mapa» y la precisión en gris.
- **Lo que no hace.** El asistente de IA no la propone al armar un formulario ni la prellena o mapea en una sugerencia; sí
  ofrece, sugiere y aclara un formulario que la tenga, y su envío la pide antes del Flow, como el de una persona (W1-A). Un estándar de la biblioteca no la usa. Se ve con `formrecords.read` y se carga con los permisos de carga de hoy.
