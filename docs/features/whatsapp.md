# WhatsApp: acceso explícito y atención del negocio

Leelo antes de tocar el webhook, el envío a Meta, los códigos e invitaciones, el número de una cuenta, el chat o los formularios del canal. Las reglas generales están en [`AGENTS.md`](../../AGENTS.md) y [backend.md](../architecture/backend.md), los formularios en [formularios](formularios.md) y la UI del chat en `ArquitecturaBaseFront/src/features/whatsapp/AGENTS.md`. Una persona puede crear su cuenta y entrar solo con su WhatsApp ([diseño](../specs/2026-09-22-ingreso-whatsapp-design.md); [pendientes de producción](../plans/verificacion-y-produccion.md)).

## Módulo y rutas

Es un [módulo opcional](../architecture/backend.md#módulos-opcionales) ([ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md)) en las carpetas `Modules/WhatsApp` de cada capa; su registro y cómo quitarlo están en [`quitar-whatsapp.md`](../guides/quitar-whatsapp.md). Se engancha al núcleo con cuatro adaptadores en `Application/Modules/WhatsApp/Channels`: `WhatsAppPhoneChannel`, `WhatsAppPhoneLinkParticipant`, `WhatsAppInvitationChannel` y `WhatsAppInvitationDeliveryStatusSource`.

- Ingreso: `WhatsAppLoginCodeController` (`POST /account/login-code/whatsapp`), `MeWhatsAppController` (`POST /api/me/whatsapp/code` y `PUT /api/me/whatsapp`) y `WhatsAppWebhookController`.
- `ConditionalWhatsAppRouteConvention` saca del registro las acciones `[WhatsAppRoute]`, de mensajería o del webhook cuando esa parte está apagada. Además de las siete rutas del chat quedan cuatro combinaciones: los dos POST de código con WhatsApp prendido, y `GET`/`POST /webhooks/whatsapp` con el webhook prendido. `WhatsAppRouteContractsTests` verifica el registro de rutas, no solo el 404.
- `DELETE /api/me/whatsapp` es del núcleo (`MeController`) y funciona sin el módulo; con el módulo, su participante suelta el contacto del chat.
- `/webhooks` está en los cuatro lugares de la [guía del prefijo](../guides/prefijo-de-backend.md).

## Reglas

- **Códigos con registro cerrado.** En `InviteOnly`, un número sin cuenta igual emite y guarda su `LoginCode` sin encolar nada, como el correo: así se sostienen los límites por destino y el 202 no revela qué números tienen cuenta (`WhatsAppLoginCodeTests`). No se «optimiza».
- **Sin contacto no hay chat.** Un código o una invitación a un número que nunca escribió se registra sin contacto: el ingreso no crea conversaciones.
- **Un mensaje de WhatsApp no autentica ni modifica la identidad.** El webhook y el procesamiento entrante no crean cuentas, no confirman teléfonos, no vinculan contactos ni emiten enlaces secretos (`IdentityBoundaryTests.Inbound_processing_has_no_identity_or_secret_link_dependencies`). Ingreso y registro son pedidos explícitos de la web; la sesión se abre después de verificar el código y del commit. Los enlaces ya emitidos conservan preview y canje ([identidad](identidad.md)).
- **El webhook solo guarda.** Valida la firma (HMAC-SHA256 del cuerpo crudo con `AppSecret`, comparada con `CryptographicOperations.FixedTimeEquals`), guarda contactos, mensajes y estados y responde `200`. Sin firma válida responde `401` sin guardar; con más de 5 MB, `413`. Ignora un `phone_number_id` ajeno. Lo ilegible de un webhook **firmado** se saltea y el lote sigue: un 500 haría que Meta reintente siete días.
- **Los duplicados se cierran en tres capas**: índice único sobre `WaMessageId`, locks por contacto y por mensaje tomados en orden, y `ExecuteInTransactionAsync`, que traduce el 23505 a `UniqueConstraintViolationException` después de soltar los locks. Si igual choca, se reintenta una vez en un scope nuevo. Entre estados gana el timestamp; si empatan, `sent` < `delivered` < `read` < `failed`, y `failed` es final.
- **El límite del webhook** es la excepción a "un servicio no llama a otro que escribe". `WhatsAppWebhookService` no abre límite: `WhatsAppWebhookPersistence` guarda el lote en su transacción, la unidad que `WhatsAppWebhookRetry` repite una vez ante un 23505. Ese trabajo devuelve un `Result` siempre exitoso, solo por los contadores, y el servicio registra con `OperationLog.Handling` y `Handled`. Los hosts atrapan por unidad (un contacto en `WhatsAppInboundProcessor`, un envío en `WhatsAppSenderBackgroundService`) para que un fallo no corte la vuelta.
- **Solo se responden acciones estructuradas.** `WhatsAppInboundPolicy` clasifica `Text`, `Media`, `Other` y botones desconocidos como Business, y `System` como Unclassified; solo un `ButtonReply` con ID de `WhatsAppAccessActions` es Access/AccessRequested. `WhatsAppAccessReplyPolicy` usa el origen público y la cultura de la instalación, sin leer cuentas: WANT_TO_ENTER y HAVE_ACCOUNT llevan a `/login`, CREATE_ACCOUNT a `/registro` (decide la política vigente) y DID_NOT_REQUEST_CODE no recibe respuesta. No se buscan frases en texto libre. Textos: `Access.resx` y `Access.en.resx`. Si la cola rechaza la orientación, el límite `OnSuccess` se deshace y el mensaje sigue pendiente.
- **Se responde la acción de acceso más reciente.** `WhatsAppInboundProcessor` se despierta con el webhook o cada `InboundPollSeconds` (`FOR NO KEY UPDATE SKIP LOCKED`). Marca todos los pendientes, pero responde solo acciones reconocidas de las últimas 24 horas, por fecha e Id; un texto posterior no las reemplaza. Una ráfaga recibe a lo sumo una orientación y un replay no vuelve a responder, en Manual o en Bot.
- **Orden de locks: contactos → cuenta.** `PhoneNumberLinker.LockAsync` toma los participantes antes que la cuenta, y perfil y administración leen la cuenta después. El procesador entrante solo toma el contacto, sin esperar a la identidad; un webhook que incrementa la secuencia de ese contacto puede esperar, pero los demás siguen. Las claves de `WhatsAppLockKeys` no cambian.
- **Los vínculos cambian solo en recorridos explícitos.** Confirmar el número (`WhatsAppContactLinker`) vincula su último contacto existente y suelta el anterior; si no existe, no se inventa ni se vincula cuando llega un mensaje. Una cuenta tiene a lo sumo un contacto, los vínculos históricos se conservan y otro BSUID no traslada la cuenta.
- **Códigos** (reglas comunes en [identidad](identidad.md)). `WhatsAppCodeIssuer` valida número y país; en el ingreso rechaza una cuenta deshabilitada con `403 Auth.Account.Disabled` antes del tope diario. Para los demás destinos controla el tope, emite con `LoginCodeIssuer` y encola; si la cola no lo toma, no hace `MarkSent` ni consume tope. El ingreso abre su límite en `WhatsAppLoginCodeService`; la verificación es la del núcleo.
- **El `to` puede ir sin el 9.** Los celulares argentinos se guardan con el 9, pero el número de prueba de Meta los rechaza (131030); `SendArgentineMobilesWithoutNine` existe **solo** para él: en producción va apagada y el número guardado nunca cambia.
- **Lo que carga un administrador queda sin verificar**, el número también. Desvincularlo desde la administración (`DELETE /api/users/{id}/whatsapp`) **cierra las sesiones si es otra cuenta**; sobre la propia las conserva y exige otro medio de ingreso. Desde el perfil (`ProfilePhoneService`) tampoco las cierra. Los tres recorridos anulan los enlaces pendientes ([administración](administracion.md)).
- **El texto de los mensajes se borra a los 90 días por defecto** (`WhatsAppMessageRetentionService`). Dos UPDATE idempotentes vacían el texto del historial y el de la salida durable salvo Pending y Sending; si uno falla, la próxima corrida completa el otro. `ProtectedFlowResponse` se vacía solo en mensajes procesados, sin tocar BusinessForms. Quedan fecha, dirección, tipo, estado, botón y hora de procesado; los contactos no cambian. Solo `WhatsAppMessageRetentionRepository` usa `ExecuteUpdate`, posible porque `WhatsAppMessage` no es auditable ni de borrado lógico (`TransactionBoundaryTests`). Corre aunque WhatsApp esté apagado. La política pública debe reflejar el plazo; el código no acredita una publicada.
- **La invitación (`invitacion_acceso`) es Marketing** en Meta: la persona no la pidió, y reescribirla no lo cambia. Cuesta más y Meta limita esos mensajes, así que **puede no llegar** (el 131049 llega como `failed`); el respaldo es reenviar o invitar por correo. `lastInvitation.deliveryStatus` da el último estado de Meta para `UserInvitation.ProviderMessageId`, o `Pending` sin id ni aviso.
- **Nunca registrar códigos, tokens, enlaces ni números enteros** ([identidad](identidad.md)). Por eso `Microsoft.AspNetCore` queda en `Warning` (en `Information` se vería el `hub.verify_token`) y las cadenas de conexión no llevan `Include Error Detail` (el DETAIL de un 23505 mostraría el BSUID). El cliente de Meta no usa los logs de `HttpClient`, que en Trace guardan `Authorization`, ni reintenta POST (`RemoveAllResilienceHandlers` y `Retry.DisableForUnsafeHttpMethods()`).
- **Los tests capturan los envíos** con `factory.WhatsApp` (`CapturingWhatsAppSendQueue`), sin red ni ciclo en segundo plano: llaman a `ProcessPendingAsync()`.

## Propósito, migración y recorridos — 2026-10-02

Cada mensaje es Business, Access o Unclassified (`WhatsAppMessagePurpose`). Solo Access lleva `WhatsAppAccessEventKind` (SignInCode, VerifyPhoneCode, Invitation, AccessRequested o AccessInstructions) y el dominio rechaza combinaciones inválidas. Lo que escribe el operador es Business y, sin metadata, Unclassified. En Access, el cuerpo HTTP y el preview son null: el front muestra «Acceso al sistema», nunca recupera credenciales como borrador ni deduce el propósito por palabras.

`SeparateWhatsAppAccessAndBusiness` pasó Bot a Manual (el nuevo default) sin borrar datos; las salidas con `OperatorUserId` quedaron Business y las demás Unclassified, y el Down no inventa qué chats eran Bot.

Recorridos ([ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md)): la **atención** llega una vez al historial, sin respuesta de acceso, y se contesta en Manual; el **acceso** se pide en `/login` y se verifica en la web, sin depender del modo; **registro y teléfono** se confirman en `/registro` y Mi perfil, nunca por un mensaje; la **invitación** conserva plantilla, consentimiento y payload WANT_TO_ENTER, y tocar su botón solo orienta a `/login`, sin confirmar el teléfono ni abrir sesión.

La cola de códigos, invitaciones y orientación es transaccional y cifrada: sobrevive a reinicios, no envía antes del commit y no repite una reserva incierta. Falta probar con un teléfono real y verificar el payload configurado en Meta ([guía local](../guides/whatsapp-en-local.md)).

## Chat de atención — 2026-10-02

- **Alcance.** El chat responde solo conversaciones existentes: no crea contactos, no vincula ni inicia con números nuevos. Solo texto: sin adjuntos, audios, respuestas rápidas, asignaciones ni etiquetas. Admin recibe toda la configuración; para atención conviene crear roles con `whatsapp.read`, `whatsapp.send` y `whatsapp.manage`. La consulta cada 5 s es una decisión de producto, no un SLA: push solo si se mide la necesidad.
Una instalación es un negocio con una sola configuración (`WhatsAppOptions`): no hay conexiones por operador, tenants ni Embedded Signup. El SPA lo abre en `/whatsapp/chat`; IA, contactos y plantillas siguen pendientes.

- **Permisos.** `whatsapp.read` lista, lee y marca leído; `whatsapp.send` responde y revisa sus envíos; `whatsapp.manage` cambia el modo. Los dos últimos exigen lectura. Se componen con `IPermissionCatalogSource` ([ADR 0010](../decisions/0010-catalogo-de-permisos-extensible.md)).
- **Lectura.** Cada entrada nueva recibe una secuencia creciente por contacto, aunque llegue atrasada; un duplicado no la incrementa. `WhatsAppReadState` guarda lo leído por operador, sin retroceder ni pasar la última entrada. Solo la vista reciente y visible marca leído. El listado pagina, busca por nombre o número y cuenta no leídos en la búsqueda; el historial pagina por fecha e Id.
- **Modo.** Compartido y versionado (`ModeRevision`; una revisión vieja da 409). Manual es el default y habilita responder. `Bot` sigue en el contrato, pero no responde: la UI solo ofrece tomar Manual. El acceso no depende del modo y una llamada a Meta ya iniciada puede terminar.
- **Enviar.** `POST /api/whatsapp/chats/{id}/messages` acepta hasta 4096 caracteres y un `requestId`. En una transacción bloquea el contacto, exige Manual, número disponible y ventana de 24 horas, y guarda una salida durable. El mismo contacto, operador y `requestId` devuelven la misma operación; otro texto da 409. Pending, Sending y Uncertain bloquean otro envío y el cambio de modo. El 202 es **guardado en cola**, no entregado.
- **Worker.** `WhatsAppOutgoingBackgroundService` reserva con `SKIP LOCKED`, revalida permisos, cuenta, modo, destino y ventana, y confirma Sending antes de llamar a Meta. Pending sobrevive al reinicio; Sending de más de dos minutos pasa a Uncertain y no se reintenta. Comparte con el worker de la cola una lease por destino en Redis (60 s, renovada cada 10, token propio), que no reemplaza a PostgreSQL ni garantiza una sola entrega si el proceso muere después del POST.
- **Estados.** Los receipts se guardan por wamid aunque lleguen antes de registrarse el envío, que luego aplica el último estado. Sent es aceptado por Meta; Delivered y Read requieren aviso. Uncertain pide revisar el teléfono y confirmar Reviewed, que habilita responder sin reenviar ni afirmar entrega. Un Failed de texto puede pasar su texto al borrador sin enviar ni pisar lo escrito. Un estado desconocido nunca es Sent.
- **Respuesta perdida.** El front congela texto y `requestId` y consulta `GET .../outgoing/{requestId}` antes de repetir; un 404 permite repetir **el mismo** pedido. Consulta y revisión son del operador que envió.

No se registran cuerpos, destinos ni secretos. Las siete rutas admiten 600 lecturas y 120 escrituras por minuto por operador (`RateLimiting:WhatsAppChatReadPermitLimit` y `WhatsAppChatWritePermitLimit`) y no existen con la mensajería apagada.

## Configuración

Todo cuelga de la sección `WhatsApp` y se lee **al arrancar**: cambiar cualquier valor, el token incluido, pide reiniciar la Api.

| Clave | Por defecto | Para qué |
|---|---|---|
| `PhoneNumberId` | — | **el interruptor.** Sin él, WhatsApp queda apagado; con él y sin `AccessToken`, la Api **no** arranca |
| `WabaId` | — | cuenta de la conexión, solo para Flows; sin ella no hay aceptación, pero sí chequeo local |
| `AccessToken` | — | secreto: token del usuario del sistema |
| `AppSecret` y `VerifyToken` | — | secretos que prenden el webhook, **solo juntos**. Sin ninguno, el envío funciona y el webhook queda apagado con un Warning; con uno solo, la Api no arranca |
| `GraphApiVersion` | `v25.0` | versión de la Graph API |
| `Templates:LoginCode` | `codigo_ingreso` | plantilla de autenticación |
| `Templates:Invitation` | `invitacion_acceso` | plantilla de invitación (Marketing) |
| `AllowedCountries` | `["AR"]` | países que reciben códigos (ISO 3166-1 alfa-2 en mayúsculas), también al vincular o cargar un número; cada código se paga. **Sin valor inicial en la clase**: el binder suma lo configurado |
| `DailyAuthCodeLimit` | `100` | códigos en 24 horas móviles, entre todos los números y propósitos; cuenta solo lo que **salió**. Lo aplica `WhatsAppCodeQuotaGuard` antes de emitir; el núcleo no tiene tope |
| `DisplayPhoneNumber` | — | el WhatsApp del negocio, solo dígitos, para "Volver a WhatsApp" |
| `RetryDelaySeconds` | `6` | espera antes de reintentar; nunca menos de 6, el límite de Meta por persona |
| `QueueCapacity` | `100` | cola de envío, como la del correo ([colas transaccionales](../architecture/backend.md#colas-transaccionales)). Con WhatsApp apagado, encolar devuelve `false` con un Warning |
| `InboundPollSeconds` | `30` | cada cuánto se revisan los entrantes pendientes |
| `ProcessInboundInBackground`, `ApplyMessageRetentionInBackground` | `true` | entrantes y retención; en los tests, `false` |
| `ProcessFlowsInBackground`, `ProcessOutgoingInBackground` | `true` | Flows (publicación y respuestas) y respuestas manuales; en los tests se ejecutan de forma controlada |
| `FlowSessionLifetimeHours` | `24` | vigencia de una sesión de formulario desde que entra en cola (1–168) |
| `MessageRetentionDays` | `90` | plazo de borrado del texto; reflejarlo en la política de la instalación |
| `SendArgentineMobilesWithoutNine` | `false` | solo para el número de prueba de Meta |

Además:

- **Con el webhook prendido, la Api no arranca sin `Authentication:Issuer`**, el origen de los botones de orientación. Fuera de Development y Testing es obligatorio siempre, por el correo de invitación.
- El rate limiter `whatsapp-webhook` es por IP: `RateLimiting:WhatsAppWebhookPermitLimit` (600) y `RateLimiting:WhatsAppWebhookWindowMinutes` (1), registrado por el módulo en `AddWhatsAppApi`.
- **El túnel del AppHost** (`DevTunnel:Enabled`) viene apagado y expone toda la Api a internet: se prende para probar el webhook y se apaga al terminar ([guía local](../guides/whatsapp-en-local.md)).
- El health check `whatsapp` es solo de readiness.

## Perfil local de formularios — 2026-10-10

`WhatsAppStructuredFormChannel` implementa `IStructuredFormChannel` con el identificador `phone`. El chequeo es puro (`WhatsAppFlowCompiler` y `ValueCodec`): no usa transporte ni cuenta, no publica y no importa BusinessForms.

El perfil vigente para publicaciones nuevas es `flow-static-7.3-r5`: Flow JSON 7.3, Graph v25.0, codec=1, sin endpoint, una pantalla terminal con Form y Footer. En preguntas de catálogo, el Dropdown usa `data-source: ${data.<key>_options}` y `screen.data` declara la lista de objetos iniciales; la preview del editor usa una lista de ejemplo vacía. El hash es de los bytes UTF-8 exactos y la huella de compilación suma perfil, Graph y codec.

| Tipo | Control y límites |
| --- | --- |
| shortText / longText | TextInput / TextArea con la longitud guardada como máximo numérico; label 20, helper 80 |
| decimal | TextInput de texto con patrón (signo, punto o coma) y helper, hasta 60 caracteres. Codec exacto (96 bits, escala ≤ 28): rechaza redondeo, exponente y separadores de miles |
| date | CalendarPicker single con el required del borrador; yyyy-MM-dd a DateOnly, sin UTC; label 40, helper 80 |
| boolean | RadioButtonsGroup Sí/No sin valor inicial; required obliga a elegir; label 30, descripción 300. Una ausencia opcional no es false |
| choice | Dropdown con ChoiceId como ID para opciones manuales; una pregunta con `source.catalog` usa el Dropdown dinámico de r5. Label 20, título 30; hasta 50 opciones manuales, sin recortar. Una ayuda no representable bloquea |
| Footer | Uno, submitLabel hasta 35; entrega `field_{Guid:N}`, nunca índices ni labels |

Los textos se miden en UTF-16, de forma conservadora. Por pantalla: nombre de 80, 40 preguntas, 50 componentes y 1 MiB de JSON, límites distintos de los de autoría. Un problema conserva identidad y ruta y nunca emite JSON parcial. Cada texto sale de su cultura exacta (es/en), sin fallback al original ni a `SystemSettings`: si falta uno requerido en cualquiera, no se produce ningún Flow, y una ayuda presente en una cultura se exige en la otra. El orden está en los bytes, pero no cambia la identidad.

r3 emite los máximos como números JSON y sus diagnósticos mapean por identidad las rutas de Meta, sin su texto. Las proyecciones ya selladas r3/r4 conservan sus bytes. r5 agrega el esquema de datos iniciales para Dropdowns dinámicos de catálogo. Las revisiones de perfil versionan el contrato técnico del Flow; un cambio de texto de presentación modifica el JSON, el hash y la huella de compilación de nuevas proyecciones, pero no el perfil. Ningún cambio recompila proyecciones previas ni modifica versiones o sesiones, y la captura usa el mapping sellado, aceptando punto y coma. La identidad de cuenta es WABA, número y Graph, sin exigir que el perfil actual sea el sellado. Pending no bloquea Compatibility por sí solo. Cada versión crea un Flow físico nuevo: no se reabre el pooling.

## Publicación durable de Flows — 2b, 2026-10-10

Antes de aceptar y de cada efecto se verifica, con una consulta paginada de `phone_numbers`, que el `PhoneNumberId` pertenece al `WabaId` de la misma conexión; si la consulta falla, se reintenta sin repetir el paso externo. Sin WABA no fallan el arranque ni el texto. Cambiar de conexión obliga a revisar las operaciones previas: nunca se publica en silencio en otra cuenta, y un binding con FlowId cuya cuenta cambió queda Uncertain con ese ID ([cargar el WABA](../guides/whatsapp-en-local.md#qué-va-en-appsettings-y-qué-en-user-secrets)).

`WhatsAppFlowPublicationService` acepta en su límite `OnSuccess`, con replay por `OperationId` y huella. Cada versión y cultura tiene un recurso físico (`WhatsAppFlowBindings`) con su proyección protegida, sin FK ni joins a otro módulo. `WhatsAppFlowDispatchService` avanza Create → Upload → Validate → Publish → Verify bajo el lock `whatsapp-flow`, revalidando permisos, con lease/fence y sin transacciones durante el HTTP. InFlight se confirma justo antes de llamar: un rechazo seguro libera el paso, pero un timeout posterior deja incertidumbre para consultar, nunca un POST repetido a ciegas.

- **Upload**: `validation_errors` bloquea; solo se mapean los punteros reconocidos, sin los mensajes de Meta.
- **Verify**: ID, nombre y WABA, JSON 7.3 sin errores, salud AVAILABLE del Flow y del número exacto (`health_status` sin `phone_number`, que Graph v25.0 rechaza) y SHA256 del asset, bajado solo por HTTPS 443 sin userinfo, de `*.fbcdn.net` o del host exacto `mmg.whatsapp.net` que devuelva Graph, sin bearer ni redirects, hasta 1 MiB.
- **Publish** no activa BusinessForms: hace falta leer PUBLISHED verificado y el commit local.
- **Recuperación**: un Create sin ID confirmado nunca se repite ni se adopta por nombre; queda en revisión. Con un ID durable se consulta el recurso antes de avanzar. Un Publish incierto no se repite; la revocación frena los efectos y, como los fallos, conserva los IDs sin borrar recursos externos.
- **Preview**: solo de versiones publicadas, verificando cuenta, asset y salud; URL de `business.facebook.com`, vencimiento explícito y `ToString` redactado. No se guarda la URL ni se exponen ID, WABA o JSON.

Reglas del canal que el código impone:
- Create recibe el nombre técnico y la categoría OTHER, sin publicar; Upload envía multipart `flow.json`/`FLOW_JSON`. Un Flow en DRAFT no implica por sí solo un bloqueo de salud.
- La lease y el deadline limitan la llamada, y solo el dueño vigente completa, con un límite acotado independiente del apagado.
- La recuperación lista los Flows de la cuenta con cursores y presupuesto, y conserva la clasificación segura de cero, uno o varios candidatos.
- Los bindings se conservan mientras su versión o su historia lo requiera, sin heredar los 30 días de `FormDraftChanges`.

`LinkRevision` del contacto sube con cada vinculación o desvinculación efectiva (no al recibir mensajes) y distingue A → sin vínculo → A. Transporte: [guía de Flows API](https://developers.facebook.com/documentation/business-messaging/whatsapp/flows/guides/flowsapi/).

## Envío durable de formularios

`GET /api/whatsapp/chats/{id}/forms` exige `whatsapp.read` y `whatsapp.send` y ofrece versiones activas solo si la cuenta vinculada (`Contact.UserId`) está activa, tiene idioma es/en y su propio `formrecords.create`. Sin `whatsapp.send`, `linkedAccount` y `formEligibility` son null; el correo exige además `users.read`. `recipientContextRevision`, opaca, cambia con vínculo, estado, idioma o roles del destinatario y no autoriza.

La selección se pagina, busca y ordena en SQL según el idioma del destinatario.

`POST .../form-messages` crea en un commit la sesión y la salida, en el idioma de la cuenta. El mensaje es `interactive.type=flow` (`flow_message_version="3"`, `flow_action=navigate`, `flow_token` aleatorio) con el botón «Abrir formulario» / «Open form». El texto que lo acompaña es obligatorio y admite hasta 1024 caracteres, con validador propio. El token se compara por hash y no se registra.

## Recepción de respuestas de Flow

El `nfm_reply` se procesa sin esperar el Delivered del envío. El webhook guarda la respuesta protegida, fuera del texto del historial, y marca rechazada una de más de 16 KiB (`WhatsAppFlowProfile.MaxResponseJsonBytes`), tope que el compilador ya impide al publicar. `WhatsAppFlowReplyBackgroundService` la procesa aparte y respeta `ProcessFlowsInBackground`.

Las claves `field_{Guid:N}` se decodifican con el codec de la proyección sellada; una respuesta ilegible queda Unrecognized o Rejected. El mensaje y la sesión guardan su estado final por separado, para que un replay alterado no pueda presentarse como completado. La sesión vence según la fecha firmada por Meta. Sin vínculo o vencida, solo se recupera un recibo idéntico (`FindExistingReceiptAsync`). Capturar no le escribe al empleado: la cortesía la escribe el operador. Sin `formrecords.read` se ocultan los valores, pero se ve el número; el preview del listado es el texto fijo «Respuesta del formulario». No se registran token ni valores ([captura](formularios.md)).

## Piloto de formularios

El piloto real con r3 publicó, envió desde el chat y capturó una respuesta con los seis tipos. Falta calificar r4 y, en dispositivos, ambos separadores decimales (la preview aceptó punto y rechazó coma, y el celular respondió un entero), los límites completos y ausencia frente a false: [Etapa 2](../plans/formularios/etapa-2-catalogos.md) y [verificación de cierre](formularios.md#verificación-de-cierre--2026-10-11).

Fuentes de Meta: [Get Preview URL](https://www.postman.com/meta/whatsapp-business-platform/request/t3iym29/get-preview-url) (preview y vencimiento), [WhatsApp Flows Tools](https://github.com/WhatsApp/WhatsApp-Flows-Tools) y su [ejemplo de encuestas](https://github.com/WhatsApp/WhatsApp-Flows-Tools/blob/main/articles/creating-surveys/main.py) (interactive/navigate y `nfm_reply`), la [colección de Flows](https://www.postman.com/meta/whatsapp-business-platform/documentation/y5swede/moved-whatsapp-flows-api) (publicación irreversible, assets inmutables) y [enviar un Flow publicado](https://www.postman.com/meta/whatsapp-business-platform/request/ftdtf2c/send-published-flow-by-id) (datos iniciales en `flow_action_payload.data`, Etapa 2).

## Catálogos de formularios — K7

`GET /api/whatsapp/chats/{id}/forms/{definitionId}/versions/{versionId}/preparation` usa los permisos de lectura y envío del chat. Resuelve la oferta con el contacto y la cuenta vinculada del servidor, nunca con un destinatario enviado por el navegador. La selección indica `hasCatalogQuestions`; el diálogo muestra conteos y bloqueos sin incluir ofertas en el POST. Aceptar el envío vuelve a preparar y sella las opciones; `navigate` las lleva en los datos iniciales del mensaje.

El `nfm_reply` se decodifica contra esa oferta protegida. La baja de una opción Manual muestra un rechazo sin respuesta guardada ni número; la pregunta del aviso queda persistida para la recarga, sin revelar la opción. El histórico agrega catálogo y detalle al texto recibido, bajo `formrecords.read`, y el reenvío es una confirmación manual nueva. Las pruebas locales de K7 se resumen en [formularios](formularios.md#catálogos-al-enviar-y-responder--k7-2026-10-12); Meta real queda pendiente de la prueba conjunta.

## Rechazos y reintentos de formularios — 2026-10-13

El adaptador neutral de WhatsApp devuelve `Failed` para `UnsupportedConfiguration` y `PayloadMismatch` del Accept; no son motivos para sondear indefinidamente. La indisponibilidad temporal mantiene su recuperación y las publicaciones selladas no se modifican.

Las fallas de captura, incluido un sink no disponible, tienen hasta cinco intentos fallidos por mensaje, persistidos en `WhatsAppMessages.FlowReplyFailureCount`. El quinto guarda `Rejected` y `ProcessedAtUtc`, de modo que sale del lote pendiente y queda sujeto a la retención existente. Las cancelaciones del worker no consumen intentos. El chat conserva el rechazo y su motivo seguro incluso sin sesión identificada, sin valores ni número. Una falla transitoria puede completar en el intento siguiente; el recibo existente sigue evitando duplicar registros.

Tests unitarios de rechazo definitivo y recuperación transitoria; PostgreSQL comprueba el contador entre scopes nuevos, su límite, la salida del lote y la proyección rechazada del chat. Suite final **2746/2746** con PostgreSQL/Redis y build sin advertencias. No se llamó a Meta en esta corrección.
