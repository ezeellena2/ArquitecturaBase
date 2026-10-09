# Formularios, etapa 1: contrato propuesto

Estado: **PROPUESTA para revisión del coordinador**, 2026-10-09, incremento 0. Los DTO, rutas, estados de operaciones y puertos siguientes todavía no están implementados. El único bloque implementado es el modelo puro indicado al final. No se declara terminada la etapa 1.

Fuentes: [plan principal](2026-10-07-formularios-dinamicos-y-whatsapp-flows.md), [arranque](formularios-etapa-1-arranque.md), [arquitectura](../architecture/backend.md), [WhatsApp](../features/whatsapp.md), [Roles](../../src/ArquitecturaBase.Application/Services/Roles/RoleService.cs) y [diseño del front](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md). Se revisaron las doce capturas del tablero y el demo como referencia, sin modificarlo ni adoptar su SQL como migración. Las variantes sugeridas del tablero requieren elección; no constituyen una aprobación implícita.

## Decisiones recibidas y límites

- FieldId y ChoiceId definitivos los asigna el servidor al guardar. El cliente trabaja con identidades provisionales y recibe un mapa canónico; conservar una identidad exige que pertenezca al borrador autorizado.
- El prefijo se congela en el commit que acepta la primera publicación durable, aunque luego falle o se recupere la operación.
- El botón de apertura es el texto traducido estándar «Abrir formulario» / «Open form». `submitLabel` es el texto personalizado del pie del formulario.
- Leer valores requiere `forms.records.read` además de `whatsapp.read`. Los nombres literales de permisos tienen una incompatibilidad pendiente con la convención actual.
- Las entidades futuras usarán `Entity` e `IAuditable` y los interceptores existentes. No se crea otro AuditLog ni caché de formularios; Redis requiere una necesidad demostrada.
- No se autoriza en este incremento publicar, enviar ni llamar a Meta, agregar controllers, EF/migraciones, workers o integración del chat.

## FormDefinitionJson formato 1

JSON de contenido de negocio independiente del payload de Meta. `format` vale `1`; un formato desconocido falla explícitamente. `culture` identifica el idioma elegido; inicialmente `es` y `en`. Textos localizables por idioma y fallback requieren confirmar su política antes de consumo. No traducir automáticamente lo escrito por un empleado.

| Tipo JSON | Dato | Configuración propuesta |
| --- | --- | --- |
| `shortText` | string | `required`, `maxLength` |
| `longText` | string | `required`, `maxLength` |
| `decimal` | decimal representado como string en registros | `required`, `precision`, `scale`, `min`, `max` |
| `date` | DateOnly, `YYYY-MM-DD` | `required`, `min`, `max` |
| `boolean` | bool | `required`; false es una respuesta válida |
| `choice` | ChoiceId de una opción | `required`, opciones manuales |

Seis tipos manuales habilitados. `group` y `source.catalog` se reservan y permanecen deshabilitados: no expandirlos ni sustituirlos silenciosamente. No hay reglas, catálogos remotos, condiciones ni fuentes dinámicas en esta etapa. `Number` en el enum de Domain representa el tipo decimal; el nombre JSON se mapeará explícitamente.

Ejemplo ilustrativo: los UUID representan identidades asignadas por servidor. Las cotas son ejemplos de autoría, **no límites calificados de Meta**.

```json
{
  "format": 1,
  "culture": "es",
  "submitLabel": {"es": "Enviar pedido", "en": "Submit request"},
  "fields": [
    {"fieldId":"019a0000-0000-7000-8000-000000000001","key":"field_1","type":"shortText","label":{"es":"Referencia"},"required":true,"maxLength":80},
    {"fieldId":"019a0000-0000-7000-8000-000000000002","key":"field_2","type":"longText","label":{"es":"Observaciones"},"required":false,"maxLength":600},
    {"fieldId":"019a0000-0000-7000-8000-000000000003","key":"field_3","type":"decimal","label":{"es":"Cantidad"},"required":true,"precision":8,"scale":2,"min":"0"},
    {"fieldId":"019a0000-0000-7000-8000-000000000004","key":"field_4","type":"date","label":{"es":"Fecha"},"required":true},
    {"fieldId":"019a0000-0000-7000-8000-000000000005","key":"field_5","type":"boolean","label":{"es":"Retira personalmente"},"required":true},
    {"fieldId":"019a0000-0000-7000-8000-000000000006","key":"field_6","type":"choice","label":{"es":"Turno"},"required":true,"options":[
      {"choiceId":"019a0000-0000-7000-8000-000000000007","label":{"es":"Mañana"}},
      {"choiceId":"019a0000-0000-7000-8000-000000000008","label":{"es":"Tarde"}}
    ]}
  ]
}
```

`key` es una clave técnica estable asignada por servidor; nombre/label no identifican columnas ni preguntas. Prefijo, revisión, estado de publicación y datos del canal quedan fuera del JSON. Separar hash de contenido de negocio y hash del Flow generado. Validar duplicados, identidad, opciones, cotas y tipo antes de aceptar el borrador. Cero, false y texto vacío no se confunden con ausencia; obligatoriedad y normalización requieren una regla explícita. Unidad de longitud Unicode, trim y cotas numéricas siguen pendientes; no adoptar el decimal(18,4) del demo por accidente.

## Identidad y concurrencia

Revisión inicial `1`; un guardado válido con `expectedRevision` igual a la actual avanza exactamente una vez. Revisión vieja devuelve conflicto sin modificar el borrador. Nombre, prefijo y contenido pertenecen al mismo límite de concurrencia propuesto. No comparar contra `updatedAtUtc`.

El pedido distingue `localId` del cliente y `fieldId`/`choiceId` canónicos. Una pregunta nueva manda identidad canónica null. El servidor asigna UUID v7, key y opciones en el guardado y responde el mapa. Una identidad no perteneciente a esta definición/borrador, o una opción trasladada a otra pregunta, se rechaza; un UUID elegido por el cliente nunca se adopta como nuevo ID definitivo.

Cambiar etiqueta, ayuda, orden o configuración del mismo tipo conserva identidad. Cambiar tipo crea otra FieldId/key y opciones, aunque luego se vuelva al tipo previo. Copiar una definición crea todas las identidades nuevas y no copia versiones, publicación, bindings ni contador. El borrador no crea Fields de planilla; esa materialización pertenece a la primera publicación/versionado posterior.

La versión activa y el borrador son conceptos distintos. Propuesta: luego del sellado `draft` puede ser null; la siguiente edición parte del snapshot y de la revisión vigente. Descartar cambios locales vuelve a GET; no equivale a borrar la definición. Ante un guardado incierto, conservar el borrador local y releer/comparar; falta decidir recuperación del mapa perdido. Este incremento no agrega comprobante durable ni promete retry seguro del guardado.

## DTO del editor y listado

Contratos HTTP propios en Api (`*HttpRequest`, `*Query`), mapeados a mano a modelos de Application. Redactar `ToString()` de cuerpos con datos privados. Actor proviene de CurrentUser; auditoría de interceptores, nunca del cuerpo.

### Crear

`POST /api/forms/definitions`, cuerpo propuesto:

```json
{"name":"Pedido","prefix":"PED","culture":"es","submitLabel":{"es":"Enviar pedido"}}
```

201 con GUID de la definición y Location, como Roles; el editor obtiene GET después. Creación persiste definición/borrador vacío revisión 1; no llama al canal.

### Obtener

`GET /api/forms/definitions/{id}`, ejemplo propuesto:

```json
{
  "id":"019a0000-0000-7000-8000-000000000010",
  "name":"Pedido","prefix":"PED","prefixLocked":false,"revision":1,
  "draft":{"format":1,"culture":"es","submitLabel":{"es":"Enviar pedido"},"fields":[]},
  "activeVersion":null,"publication":null,"recordCount":0,
  "capabilities":{"canManage":true,"canPublish":false,"canDelete":true},
  "createdAtUtc":"2026-10-09T12:00:00Z","modifiedAtUtc":null
}
```

Capabilities orientan la interfaz; servidor vuelve a autorizar cada operación. GET no genera ni publica. No entregar tokens, payloads o credenciales del canal.

### Guardar

`PUT /api/forms/definitions/{id}/draft`, ejemplo parcial de una pregunta nueva:

```json
{
  "expectedRevision":1,"name":"Pedido","prefix":"PED",
  "draft":{"format":1,"culture":"es","submitLabel":{"es":"Enviar pedido"},"fields":[
    {"localId":"tmp-question-a","fieldId":null,"type":"choice","label":{"es":"Turno"},"required":true,
     "options":[{"localId":"tmp-choice-a","choiceId":null,"label":{"es":"Mañana"}}]}
  ]}
}
```

200 devuelve `definition` con la proyección completa de GET y `identityMap`. Fragmento ilustrativo:

```json
{
  "identityMap":{
    "fields":[{"localId":"tmp-question-a","fieldId":"019a0000-0000-7000-8000-000000000006","key":"field_1","replacesFieldId":null}],
    "choices":[{"fieldId":"019a0000-0000-7000-8000-000000000006","localId":"tmp-choice-a","choiceId":"019a0000-0000-7000-8000-000000000007"}]
  }
}
```

La respuesta canónica contiene revisión 2 y IDs definitivos; el cliente actualiza identidades y base de concurrencia juntos. Cambios de tipo informan `replacesFieldId`. `localId` sólo correlaciona el pedido; no persiste como identidad de negocio.

### Listado y facetas

GET de listado devuelve directamente `PagedResult<FormDefinitionRow>`: `items`, `totalCount`, `page`, `pageSize`, `totalPages`, `hasPrevious`, `hasNext`; no agregar otro envelope. Fila propuesta: id, name, prefix, questionCount, recordCount, modifiedAtUtc, activeVersionNumber, publication/displayState y capabilities.

Ejemplo mínimo de `GET /api/forms/definitions?page=1&pageSize=20&search=Pedido&sort=name&publicationFilter=All`:

```json
{"items":[{"id":"019a0000-0000-7000-8000-000000000010","name":"Pedido","prefix":"PED","questionCount":0,"recordCount":0,"modifiedAtUtc":null,"activeVersionNumber":null,"publication":null,"displayState":"Draft","capabilities":{"canManage":true,"canPublish":false,"canDelete":true}}],"page":1,"pageSize":20,"totalCount":1,"totalPages":1,"hasPrevious":false,"hasNext":false}
```

Facetas propuestas `GET /api/forms/definitions/facets?search=Pedido`: `{"all":1,"published":0,"drafts":1}`. Confirmar nombres de query y shape antes de consumo.

`PagedRequest` conserva page 1..1.000.000, pageSize 1..100 y búsqueda hasta 100 caracteres. `SortableFields` propuestos: name, questionCount, recordCount, modifiedAtUtc. Default name, desempate Id único; nulabilidad/orden de fechas pendiente. Infrastructure aplica ApplySort y ToPagedResultAsync. Búsqueda por nombre, case-insensitive y literal para `%`/`_`, sin SQL del cliente.

Filtro propuesto All / Published / Drafts: Published significa tener versión activa aunque falle una publicación posterior; Drafts no tiene activa e incluye primera publicación pendiente/fallida. Facetas cuentan con autorización/búsqueda y los demás filtros, omitiendo su propia dimensión. Para questionCount se propone borrador, último snapshot sellado y activa, en ese orden; confirmar semántica. recordCount cero no expone una planilla inexistente ni habilita navegación todavía.

## Inventario futuro de HTTP: fuera del primer contrato de consumo

El primer contrato revisable se limita a crear/listar/obtener/guardar borrador y sus facetas. Copia/eliminación y las operaciones posteriores se inventarían aquí desde el plan, sin ampliar su API ni autorizar implementación. Todos los prefijos son `/api`; no cambia BackendPrefixes. Las 13 combinaciones nuevas requieren inventario explícito y tests antes de implementarse. Permisos siguientes son los **nombres del plan aún en conflicto**, no políticas registradas.

| Método y ruta | Servicio/responsabilidad | Permiso propuesto |
| --- | --- | --- |
| GET `/api/forms/definitions` | Listar filas paginadas | forms.templates.read |
| GET `/api/forms/definitions/facets` | Contar opciones de filtro | forms.templates.read |
| GET `/api/forms/definitions/{id}` | Obtener borrador/activa | forms.templates.read |
| POST `/api/forms/definitions` | Crear borrador | forms.templates.manage |
| POST `/api/forms/definitions/{id}/copies` | Copiar con IDs nuevos | forms.templates.manage |
| PUT `/api/forms/definitions/{id}/draft` | Guardar con revisión | forms.templates.manage |
| DELETE `/api/forms/definitions/{id}` | Borrar si nunca publicada; expectedRevision | forms.templates.manage |
| POST `/api/forms/definitions/{id}/checks` | Validar snapshot guardado/diff; sin guardado | forms.templates.publish |
| POST `/api/forms/definitions/{id}/publications` | Aceptar publicación durable | forms.templates.publish |
| GET `/api/forms/definitions/{id}/publications/{requestId}` | Releer operación/comprobante | forms.templates.publish |
| GET `/api/forms/definitions/{id}/preview` | Obtener preview autorizado | forms.templates.read |
| GET `/api/whatsapp/chat/contacts/{contactId}/forms` | Elegibles para operador/contacto | whatsapp.read + forms.records.create |
| POST `/api/whatsapp/chat/contacts/{contactId}/form-messages` | Aceptar envío durable | whatsapp.send + forms.records.create |

No asumir que manage o publish otorgan read. La UI deberá conocer combinaciones reales. Borrar devuelve 204 y no permite borrar una definición que ya fue publicada; revisar borrado lógico y políticas de relaciones antes de EF.

Check propuesto: `draftRevision`, `businessHash`, `diff`, `validation`, sin publicar ni congelar prefijo y sin UoW de escritura. Publicación: pedido `requestId`, `expectedRevision`, `businessHash`; respuesta de aceptación 202 con operationId/requestId/definitionId/versionNumber/state/stage/error y Location de consulta. Mismo requestId + mismo pedido recupera operación; mismo UUID + otro cuerpo, conflicto. La huella del Flow generado pertenece al canal y no reemplaza businessHash.

Preview no se fabrica como URL arbitraria: destino permitido, lifetime, CSP y exposición de datos siguen pendientes. El selector de envío usa sólo versión activa, limita búsqueda/página y devuelve versión/idioma autorizado. Envío propuesto lleva requestId, definitionId, versionNumber, expectedModeRevision y texto de acompañamiento; el servidor valida empleado, contacto, vinculación, modo, permisos y versión vigente. El vencimiento visible debe salir del servidor.

Extender las rutas existentes de historial, consulta por requestId y revisión de envío incierto; no duplicarlas. Text y Flow comparten deduplicación/reserva del contacto. Webhook conserva endpoint técnico existente. No crear endpoints públicos de registros ni data_exchange para esta etapa.

## Errores y estados legibles

ProblemDetails conserva title/detail traducidos, code, traceId y errors en validaciones; cuerpo ilegible Request.Invalid sin errors. 401 sin sesión, 403 sin permiso, 404 inaccesible/inexistente, 409 revisión/requestId/estado, 400 validación, 415 formato HTTP, 413 tamaño, 429 rate limit y 500 genérico siguen el borde existente. No comunicar «guardado» ante resultado incierto.

| Código | Situación | Implementación |
| --- | --- | --- |
| Forms.Draft.RevisionConflict | Revisión esperada vieja | Modelo puro, 409 |
| Forms.Draft.QuestionTypeUnavailable | group/tipo desconocido | Modelo puro, validación |
| Forms.Draft.FormatUnsupported / IdentityInvalid | Formato o identidad ajena | Propuesta |
| Forms.Definition.PrefixTaken / PrefixLocked / NotFound | Prefijo o definición | Propuesta |
| Forms.Publication.Pending / RequestConflict / VersionChanged | Concurrencia/publicación | Propuesta |

Los errores definitivos se declaran en Domain y tienen recursos es/en. Errores por pregunta/opción requieren paths conocidos y correlación con localId; no usar un label privado como clave ni filtrar identidad ajena. No agregar límites HTTP/Meta numéricos sin calificación.

| Estado técnico propuesto | Texto es / en | Qué garantiza |
| --- | --- | --- |
| Draft | Borrador / Draft | Existe contenido editable |
| Publishing | Publicando / Publishing | Operación durable aceptada |
| Published | Publicado, versión N / Published, version N | Versión activa confirmada |
| Failed | No se pudo publicar / Publication failed | Falla confirmada de la operación |
| Unconfirmed | Publicación sin confirmar / Publication unconfirmed | Resultado externo incierto |

Separar estado local, etapa de canal, activación y displayState. Precedencia propuesta: incertidumbre, operación en curso, última falla, activa, borrador. Una activa anterior sigue utilizable si falla la nueva; editar no publica. No presentar una aceptación de cola como publicación o entrega externa.

En mensajes se conservan Pending/Sending/Accepted-Sent/Delivered/Read/Failed/Uncertain/Reviewed del chat; revisión no prueba entrega. Captura propone Processing/Accepted/Rejected/Unrecognized, y Restricted para el lector sin permiso. Restricted omite valores, labels privados y número/id de registro; definir metadata mínima permitida. Cerrar sin completar no crea registro.

## Puertos neutrales y límites de transacción

Sólo contratos propuestos, sin interfaces implementadas ni nombres finales fijados:

| Puerto | Consumidor y dueño | Responsabilidad |
| --- | --- | --- |
| IStructuredFormSource | WhatsApp consume; formularios aporta | Listar elegibles y preparar snapshot de versión fija bajo autorización de empleado |
| IStructuredFormChannel | Formularios consume; WhatsApp aporta | Publicación durable, estado y preview del canal |
| IStructuredRecordSink | Worker del canal consume; formularios aporta | Recibo idempotente, Record/Values/contador en un commit |
| IStructuredRecordSource | Historial consume; formularios aporta | Enriquecer página por lote bajo autorización; sin N+1 |

Los DTO neutrales no contienen Meta, credenciales, flow_token, EF, HttpContext ni permisos del otro módulo. Módulos independientes, núcleo no los nombra; registros por capa sólo al haber tipos de esa capa. Cualquier cambio estructural exige revisar ADR, no usar un puerto como excepción encubierta.

Un UoW dueño de formularios sella snapshot/version/Fields, acepta operación, congela prefijo y avanza revisión en un commit. Canal procesa su operación en otro scope; llamadas externas fuera de transacción. Activación se confirma en límite propio. Envío crea sesión y salida durable existente atómicamente; reserva y worker siguen la lease vigente. No anidar UoW del canal y sink. El sink confirma recibo, registro, valores y contador juntos; debe aceptar replay por la misma evidencia sin duplicar.

Webhook sólo clasifica/persiste evento y responde; worker aplica correlación/normalización. Una respuesta puede preceder a delivered y no debe descartarse por ese orden. Sin respuesta automática. Retención/TTL, cuenta revocada, versión retirada y fencing entre revisiones pendientes necesitan escenarios explícitos antes del consumidor.

## Relevamiento de consumidores existentes

| Pieza real inspeccionada | Restricción para la extensión |
| --- | --- |
| Front ChatComposer | Borrador de texto, freeze durante envío, recuperación y conflicto; agregar selector sin perder texto ni inferir éxito |
| Front ChatTimeline y api/chat.ts | Cursor beforeAtUtc/beforeId, tipos de historial/salida y review; conservar paginado y adaptar discriminante Text/Flow |
| WhatsAppChatService | Autorización por operador, Manual y dedup contact/operator/requestId; Flow debe entrar en la misma reserva |
| WhatsAppChatDispatchService | Pending/Sending/Uncertain bloquean otro envío; reserva revalida permisos; Sending vencido pasa Uncertain, nunca reenvío automático |
| Repositorio de salida y receipts | Receipt puede llegar antes de aceptación; preservar replay, huella del pedido y consulta por UUID |
| WhatsAppWebhookReader y persistencia | nfm_reply hoy cae como Other/Business; reconocerlo antes de que el procesador lo marque manejado |
| WhatsAppContactLinker / PhoneNumberLinker | Contacto→cuenta se enlaza explícitamente, con orden de locks; inbound no crea vinculación |
| WhatsAppChatResponse | Header actual no tiene linkedAccount; agregar proyección autorizada, no adivinarla en front |
| QueuedDeliveries del núcleo | Cola cifrada para acceso/correo, diferente de salida manual WhatsApp; no crear otra outbox por simetría |
| RoleService / UnitOfWork | Un límite explícito por método escritor, helpers no guardan; validación antes y descarte después del commit |

El envío actual es texto con Trim y límite 4096 en front; eso no califica límites del interactive/Flow. Reintento de transporte no está habilitado. El nuevo bodyHash debe incorporar tipo/cuerpo/versión y conservar deduplicación entre texto y formulario. expectedModeRevision es ampliación futura, no garantía actual.

Historial futuro propone discriminante y metadata segura (definitionId/version/estado y referencia autorizada). Enriquecer registros en lote desde evidencia del servidor; nunca confiar en IDs de registros enviados por el navegador ni hacer joins entre módulos. Sin ambos permisos de lectura, no devolver valores ni copiarlos a body de texto. No exponer raw nfm_reply o tokens como fallback.

## Decisiones que bloquean consumo del contrato

| Punto | Resolución requerida |
| --- | --- |
| Permisos | Plan usa tres segmentos; [guía, formato](../guides/permiso-nuevo.md) línea 32 y [PermissionsTests.Permissions_follow_the_area_action_format](../../tests/ArquitecturaBase.Domain.UnitTests/Authorization/PermissionsTests.cs) líneas 24–36 exigen `^[a-z]+\\.[a-z]+$`. Catálogo agrupa por primer punto. Alternativa para discutir: áreas de dos segmentos `formtemplates.read/manage/publish` y `formrecords.create/read`; otra alternativa exige ADR/regla nueva. No renombrar ni relajar tests por cuenta propia. El permiso adicional de lectura de valores ya fue confirmado. |
| Prefijo | Política de caracteres, longitud, normalización, unicidad y colisión; sólo momento de congelado confirmado. |
| Texto/idioma/números | Fallback, required/trim/Unicode, cotas y decimal normalizado; autoría no depende de Meta. |
| Guardado/copia/descarte | Recuperar mapa perdido, snapshot base, nombres/copias y enteros seguros para JavaScript; no declarar idempotencia que no existe. |
| Listado | Semántica questionCount, filtros, orden nullable y permiso para contador/navegación. |
| Publicación | Check sobre snapshot guardado, estado compuesto, revisiones de autorización/configuración y fencing; preview/CSP. |
| Meta | Calificar plataforma/perfil, compiler y presupuestos reales antes de generar/enviar. Sin llamadas en este incremento. |
| Sesión/captura | Vencimiento, retención, unlink/revocación, tokens cifrados, metadata restringida y respuesta antes de receipt. |
| Diseño | Dibujar/elegir estados faltantes, selector de versión, fallas/conflictos y variantes sugeridas. Prefijo aparece en captura elegida mientras parte del plan lo describe pendiente: coordinador reconcilia. |

Los cuatro hallazgos ajenos del auditor del front permanecen fuera del alcance. No se modifica el plan principal, el arranque ni el demo; esta propuesta entrega diferencias al coordinador.

Se contrastó la [preparación del frontend](../../../ArquitecturaBaseFront/docs/plans/formularios-etapa-1-preparacion.md). Pendientes adicionales de crear/editar: capability de módulo instalado y ausencia; read/manage no implicados; cuenta/actor/cultura para cache; crear con respuesta perdida todavía no tiene comprobante recuperable; pérdida de permiso/guardado tardío; destino de Guardar (permanecer frente a retornar al listado); ayuda de preguntas y política de duplicados/retirada de opciones. No consumir DTO hasta resolver estas brechas. El prefijo **ya está dibujado en e1-1**: sólo faltan bloqueo y errores, no un control nuevo.

Exploración humana pendiente: reutilizar un Flow físico con slots parametrizados para distintas definiciones (p. ej., clientes/proveedores). No está aprobado cambiar el plan. Mantener identidad/versiones lógicas y tratar binding del canal como decisión separada, sin fijar cardinalidad irreversible 1:1 versión→Flow físico. Evaluar compatibilidad, huellas, autorización y captura más adelante; no implementar pool, catálogos ni nuevo compiler.

## Incremento implementado y siguiente revisión

Sólo `Domain/Modules/BusinessForms`: revisión inmutable, FieldId v7, conservación de identidad para mismo tipo, reemplazo al cambiar tipo, copia con identidad nueva y rechazo de Group/desconocidos. No hay ChoiceIdentity, JSON parser, ownership, entidades, guardado, mapa canónico, permisos, registro de DI, persistencia o endpoints. Las pruebas del modelo no demuestran autorización ni atomicidad futura.

Las reglas implementadas viven en [fuente funcional](../features/formularios.md); TDD, build, suite y revisión en [evidencia](../reviews/2026-10-09-formularios-incremento-0.md). Siguiente incremento requiere revisión del coordinador de este contrato y bloque; no iniciar consumidores por inferencia.
