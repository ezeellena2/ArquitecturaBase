# WhatsApp: detalle de los servicios de Application

Es el detalle de la guía [`src/ArquitecturaBase.Application/Modules/WhatsApp/Services/AGENTS.md`](../../src/ArquitecturaBase.Application/Modules/WhatsApp/Services/AGENTS.md). La guía es el índice: tiene las invariantes que cruzan todos los temas y, por tema, un resumen con el enlace a su sección de acá. Antes de tocar ese código, leé la sección de cada tema que toca el cambio; si cambia una regla, se actualiza acá en el mismo cambio.

## Acceso, códigos y teléfono

`WhatsAppLoginCodeService`, `ProfileWhatsAppService`, `ProfileWhatsAppChatService`, `WhatsAppCodeIssuer`, `WhatsAppCodeQuotaGuard`, `WhatsAppContactLinker`, `WhatsAppIds`.

- `ProfileWhatsAppChatService` sólo lee el chat del número de la cuenta para el perfil, con las mismas condiciones que un aviso: vínculo, número confirmado igual al del contacto y ventana de 24 h. No abre límite ni vincula.

- `WhatsAppCodeIssuer` valida el país del número interpretado, aplica el tope diario global de `WhatsAppCodeQuotaGuard` (24 h móviles, todos los números) y emite con `LoginCodeIssuer`, dueño de los límites por destino. Conserva el propósito (SignInCode o VerifyPhoneCode) y marca enviado solo si la cola tomó el mensaje; corre en el límite del llamador.
- `EnsureEnabled*` va antes del límite. Confirmar el número usa `OnAnyResult` para gastar el código. Verificación y canje de enlaces son del núcleo; `WhatsAppContactLinker` solo actúa en cambios explícitos.

## Webhook y entrada

`WhatsAppWebhookService`, `WhatsAppWebhookPersistence`, `WhatsAppInboundService`, `WhatsAppInboundPolicy`, `WhatsAppAccessReplyPolicy`.

- El webhook valida la firma, guarda y avisa a la señal después del commit; ante un 23505 reintenta en un scope nuevo. La persistencia siempre devuelve éxito: Meta reintentaría un error por días.
- `ProcessContactAsync` devuelve `Result<bool>`: false si el contacto está tomado o no existe; true si tomó el lock, aunque no haya respondido. El procesador no prepara audio ni asistente con false.
- `WhatsAppInboundPolicy` clasifica y elige la acción de acceso; `WhatsAppAccessReplyPolicy` orienta sin leer identidad. Si la cola rechaza la orientación, se lanza y se revierte.

## Chat de atención y modos

`WhatsAppChatService`, `WhatsAppChatQueryService`, `WhatsAppChatModeService`, `WhatsAppOutgoingPresentationPolicy`.

- Enviar exige Manual, ventana, destino y ninguna salida Pending, Sending o Uncertain; mismo operador y `requestId` devuelven la operación; otro texto, 409.
- Cambiar el modo es otro caso, en una UoW: Manual pide `whatsapp.send`; Assistant, `whatsapp.configureassistant` y IA disponible. Manual no depende de IA, ventana ni salidas pendientes, y cancela después del commit las interpretaciones automáticas no Always.
- Las consultas no aceptan ni recuperan operaciones. La disponibilidad de IA se lee por hilo, no por fila (apagada: `notInstalled`). `IStructuredRecordSource` decide `formrecords.read`: sin él se ocultan cuerpos de avisos, valores y explicación.

## Salida durable y entrega

`WhatsAppChatDispatchService`, `WhatsAppOutgoingSendGuard`, `WhatsAppManualSendGuard`, `WhatsAppDeliveryService`.

- Reservar revalida con `WhatsAppOutgoingSendGuard` según el origen: operador real con Read+Send, destinatario de avisos, o modo, vínculo, contexto y contenido del asistente. Un Sending vencido queda Uncertain; avisos y asistente vuelven a Pending solo si no marcaron el inicio del efecto.
- Avisos y asistente marcan el efecto en otra UoW, revalidando justo antes del POST; completar exige la misma reserva.
- `WhatsAppDeliveryService` registra lo que salió por `IWhatsAppSendQueue` después del HTTP y bloquea el wamid antes de leer: el receipt puede llegar primero.

## Flows: compilación

`WhatsAppFlowCompiler`, `WhatsAppFlowScreenBuilder`, `WhatsAppFlowValueCodec`, `WhatsAppFlowPrefillPolicy`.

- Funciones puras: JSON 7.3, presupuestos, mapping y hash de los bytes exactos. Lo nuevo usa `WhatsAppFlowProfile.Revision` con codec 1; nunca recompiles ni reescribas JSON, hash o bindings sellados.
- Un exceso se bloquea con identidad y ruta, sin truncar ni repartir pantallas. Cada pregunta de archivo abre pantalla; nunca archivos en navigate ni init-values. Un Flow con archivos presupuesta 64 KiB y uno escalar sigue en 16 KiB; el tope por archivo se sella al aceptar.
- El codec conserva decimal exacto, DateOnly y ausencia distinta de false. MultiChoice usa CheckboxGroup (1–20 opciones), rango sellado y GUID D distintos en orden; [] explícito se distingue de omisión. Selection null se omite en mapping y proyección.
- El prellenado valida sólo escalares contra el mapping y omite mapas vacíos; varias no declara init-values ni acepta claves. Reconoce PrefillRevisions r8 a r13; lo nuevo compila r13 y otros perfiles conservan envío sin prellenado. Nunca reescribir JSON, hashes ni bindings r8.
- El bloque de renglones (r10) arma una pantalla por renglón hasta `WhatsAppFlowProfile.MaxRows` (parte de r10: otro tope es otra revisión): desde el mínimo, el renglón termina con «¿Agregás otro ítem?» en un If con Footer en cada rama, y con mínimo 0 una pantalla pregunta «¿Agregás un ítem?» antes del primero; cada pantalla se titula «Artículos · ítem 2» (RG12-A: los textos dicen «ítem»). Declara `routing_model` con cada navigate, las dos ramas incluidas: el que Meta deduce deja afuera la rama «No» y corta al responderla. Cada renglón responde en `<clave>_r<n>` (la pregunta del bloque en `_r0` y en cada renglón que pregunta). Con bloque presupuesta 64 KiB, los componentes se cuentan por pantalla y el prellenado no toca el bloque ni sus preguntas. `Plan` ordena las pantallas para compilar, prellenar y mapear diagnósticos; sin bloque, r10 compila los mismos bytes que r9. Con el bloque al final, r12 suma una pantalla de cierre (`end`): el «No» y el último renglón navegan a ella y sólo ella completa, porque Meta no acepta que una pantalla que puede seguir al renglón siguiente sea terminal (2026-11-09).
- La pregunta que elige primero por un campo (r11) lleva antes un desplegable de valores (`<clave>_group`); su lista arranca vacía y oculta, la llena el `update_data` que trae el mensaje y es obligatoria sólo mientras se ve ([regla](formularios.md#elegir-primero-por-un-campo--ld1-a-2026-11-07)). No se prellena y `WhatsAppFlowDispatchService.Map` descuenta el desplegable; sin ella, r11 compila los mismos bytes que r10.
- Los títulos de sección (r10, CW15-B) son un `TextSubheading` antes de la primera pregunta de su sección, en su pantalla, o arriba de la primera pantalla del bloque si la sección empieza con él; no suman pantallas. Un título fuera de lugar o sin preguntas debajo es `UnsupportedConfiguration` en `draft.sections`; cuentan para los componentes de su pantalla, y `WhatsAppFlowDispatchService.Map` los busca en los bytes sellados para señalar su título y contar las preguntas sin ellos.
- Cálculos y condiciones (r13). `StructuredFormQuestion.Calculated` saca la pregunta de `Plan` (`WhatsAppFlowSlot.Calculated`): no tiene pantalla, componente, clave en el `complete`, prellenado ni presupuesto, y sigue en el mapping, así `Map` y el prellenado ubican las demás. Con `ShowWhen`, `WhatsAppFlowScreenBuilder.Condition` arma la expresión (los valores con `||`, la cadena de condiciones con `&&` y `${screen.<id>.form.<clave>}` si la pregunta está en otra pantalla) y cada componente va en un `If` propio, sin `else`, así las posiciones que cuenta `Map` no cambian; el `If` suma un componente. Un título con todas sus preguntas con condición va en un `If` (`Either`) y `Map` lo reconoce adentro; uno con sólo cálculos no se compila. El chequeo da `UnsupportedConfiguration` en `draft.fields[i].showWhen` a una condición sobre una pregunta que no va antes, del bloque, que no es elegir una ni sí o no, con valores que no son suyos, o puesta en una foto o un documento. Sin cálculos ni condiciones, r13 compila los mismos bytes que r12.

## Flows: publicación, estado y retiro

`WhatsAppFlowPublicationService`, `WhatsAppFlowPublicationPolicy`, `WhatsAppFlowDispatchService`, `WhatsAppFlowStatusService`, `WhatsAppFlowDeprecationService`.

- Aceptar verifica la cuenta fuera de su UoW, sella manifiesto y una proyección por cultura, y repite la respuesta por `OperationId` y huella. Cada Flow se crea con `WhatsAppFlowTechnicalName`: el nombre del formulario, la versión, el idioma y el final del id, en 80 caracteres y sin caracteres que Meta pueda cambiar, porque es la identidad con que se lo verifica y se lo busca.
- Publicar avanza con permisos vigentes, fence e InFlight marcado justo antes del proveedor; un rechazo seguro libera el paso. Un payload ilegible se pospone sin tocar la incertidumbre.
- Los diagnósticos se mapean por pantalla y posición al mapping sellado, nunca con el texto de Meta. Si ninguno cae en una pregunta, `WhatsAppFlowPublicationPolicy.Status` da un solo problema `WhatsApp.Flow.ValidationUnlocated` en `channel`, en vez de `ValidationFailed`, que promete campos marcados.
- Retirar toma el lock de selección del dueño antes que el de Flow y revalida, al reservar y antes del POST, la versión no usada y las sesiones o capturas pendientes. Usa el manifiesto sellado, sin permisos del actor ni escribir en BusinessForms; su lease y fence no tocan la publicación. Todo 4xx del retiro exige GET antes de otro POST. NeedsAttention y el contador durable dan espera creciente a rechazos persistentes, cuenta no disponible/distinta y manifiesto ilegible, sin borrar InFlight ni habilitar reenvío por un fallo de cuenta.

## Envío de formularios

`WhatsAppFormService`, `WhatsAppFormRecipientRevision`, `WhatsAppFormSendService`, `WhatsAppFormSendPolicy`, `WhatsAppFlowSessionIssuer`, `WhatsAppFlowSessionVerifier`, `WhatsAppFormDispatchPolicy`, `WhatsAppFormMessageDispatchService`.

- Selección y preparación usan la cuenta vinculada y sus permisos vía `IStructuredFormSource`, sin UoW; nunca toman destinatario ni cultura del navegador y no devuelven opciones de catálogo. Un filtro sin resultados no cambia la elegibilidad.
- `recipientContextRevision` es determinista y no autoriza. Enviar vuelve a preparar, toma el lock de selección, revalida y sella oferta, cultura preparada y token por hash en la sesión.
- Al despachar se revalida tras la cola por revisión de cuenta, sin exigir la misma cultura; init-values salen solo del contenido sellado. Sin operador solo vale el origen Assistant.

## Respuestas de Flow

`WhatsAppFlowReplyService`, `WhatsAppFlowReplyStateService`, `WhatsAppFlowMediaPolicy`, `WhatsAppFlowMediaRecorder`.

- Verifica token, cabecera (Conversation con operador y formato 1; Assistant sin operador y formato 2), sesión y proyección, y decodifica con el mapping sellado y la oferta antes del sink neutral. El Actor es quien completa; origen y cultura de sesión viajan intactos.
- Los arrays se clasifican por mapping: multiChoice exige strings GUID D ofrecidos, distintos y dentro del rango; sólo photo/document pasan objetos a MediaPolicy. Claves ajenas, escalares sustituidos y mezclas se rechazan antes de I/O. El contador preliminar excluye strings; [] ambiguo conserva presupuesto64, pero sólo un conteo > 0 marca descarga.
- Mensaje y sesión guardan su estado final aparte. Datos o contenido inválidos son terminales; medio o sink no disponibles y fallas técnicas usan el contador existente. Los bytes van como archivos, nunca en las respuestas, y se descargan fuera de UoW en 90 s por captura.
- Vencida o sin vínculo, solo se recupera un recibo idéntico; un mensaje distinto tras purgar el contenido privado no altera la sesión completada.
- Un bloque de renglones (r10) llega por ítem: `WhatsAppFlowRowsCodec` arma los ítems con `Asks` y `RowCount` del compilador desde `<clave>_r<n>` y la respuesta a «¿Agregás otro ítem?» (`<bloque>_r<n>`, y `_r0` con mínimo 0), y entrega al dueño la clave del bloque con un array JSON de un objeto por ítem; las claves `_r` no pasan sueltas. Una obligatoria que falta en un ítem cargado, una pregunta sin responder, una clave `_r` fuera del bloque o en un ítem que no pregunta y una pregunta del bloque sin sufijo son `InvalidValue`; lo de los ítems después del «No» se ignora. Una proyección que la app no sabe leer queda Rejected con `InvalidValue` (el aviso de siempre), no Unrecognized.
- r13: una clave de un cálculo (también `_r<n>` en un ítem) es `InvalidValue`; el dueño lo calcula. Una pregunta con `ShowWhen` se decodifica sólo si la respuesta ya decodificada de su pregunta es uno de sus valores; si no, lo que llegó se descarta antes del sink (la captura rechaza una oculta con valor), y una pregunta que dependía de ella queda oculta también.

## Avisos de reglas

`WhatsAppFormActionService`, `WhatsAppFormActionPolicy`.

- Encolar deduplica por run y destinatario antes de resolver el contacto; exige cuenta activa, número confirmado igual al del contacto, vínculo y ventana, sin Manual ni permisos de operador.
- Revisar aplica la decisión humana bajo lock, sin Meta: «salió» cuenta como aceptado y «no salió» habilita solo el reintento explícito de un Failed. Observar lee estados y receipts locales por lote, sin UoW, cuerpo, teléfono ni wamid.

## Asistente

`WhatsAppAssistantService`, `WhatsAppAssistantDispatchService`, `WhatsAppAssistantSuggestionPolicy`, `WhatsAppAssistantAcceptancePolicy`, `WhatsAppAssistantAccessPolicy`, `WhatsAppAssistantAutomaticPolicy`, `WhatsAppAssistantOfferCapacityPolicy`, `WhatsAppAssistantTerminalPolicy`, `WhatsAppAssistantPresentationPolicy`, `WhatsAppAssistantReplyCodec`. IA3-A suma `WhatsAppAssistantAuthoringService`, `WhatsAppAssistantAuthoringPolicy`, `WhatsAppAssistantAuthoringText` y `WhatsAppAssistantAuthoringReplyCodec`.

- Una referencia durable por mensaje fuente o captura, con Actor (operador) y Subject (destinatario) separados. En Manual el primer envío aceptado se devuelve a otros operadores; en automático Actor es Subject y no se inventa operador. El canal fija InteractionMode neutral en texto y audio, y comprueba concordancia con su ID privado y tipo durable al revalidar o llamar al dueño; los consumidores reciben el modo sin interpretar el ID.
- Consultar, aceptar o cancelar operaciones de `IAssistantOperations` va fuera de la UoW del canal; lo superado se cancela después del commit. ActiveTurn y CapacityLimited conservan Pending y el UUID.
- Aceptar revalida modo, vínculo, cuenta, contexto, ciclos y rechazos bajo lock. Hasta dos ofertas por conversación y día del negocio (primer minuto válido si la medianoche cae en un salto horario); pregunta y Flow elegido cuentan una y usan salidas distintas. Elegir recupera con Get, nunca Submit; sin cupo, un pendiente previo solo recupera.
- Formulario faltante llama al dueño fuera de la UoW tras verificar el contexto; luego la salida existente deduplica un acuse fijo localizado. Manual no contesta ni gasta cupo.
- Armar formularios y catálogos (IA3-A): sólo en la conversación propia del Asistente automático, con `forms.authoring.*` según los permisos de la persona. Una propuesta de formulario, de catálogo Manual o de personas (H4) no crea nada: `WhatsAppAssistantTerminalPolicy` pide el resumen a `IStructuredAuthoringProposalChannel` y la salida lleva el texto fijo con «Crear borrador» o «Crear catálogo» y «Cambiar algo» (`FORM_AUTHORING:{requestId}:{proposalId}:{create|change}`, en 1024 caracteres: las preguntas que no entran se suman al conteo final); el «no se puede» es un texto fijo con el enlace de lo que existe. El resumen no es una oferta ni gasta cupo. Cada toque lo atiende `WhatsAppAssistantAuthoringService` antes de interpretar: sólo valen los botones del último resumen (si no, «Esa propuesta ya cambió»); «Crear» relee la operación con Get y deja crear al dueño fuera de la UoW, y responde con el enlace; «Cambiar algo» responde pidiendo el cambio y su referencia guarda `ChangeRequestId`. Cada respuesta es una referencia propia del toque, aceptada como un acuse en la misma salida, y el toque se marca en ese commit. El mensaje siguiente lleva `PreviousRequestId` a la operación: el motor suma el turno a esa conversación. Ese mensaje puede ser un audio: la normalización no consulta el cupo cuando hay un cambio pedido. Un pedido que no dice qué armar (`unclear`) pregunta y espera: el texto fijo lleva `AwaitsAnswer` y `AcceptTerminal` deja en la referencia su propia operación como `ChangeRequestId` (`AwaitAnswer`), así que la respuesta continúa esa operación por el mismo camino, sin botones ni cupo.
- Tras una captura, `ForCapturedRecord` fija el registro (al modelo va solo su ID; la lectura de sugerencias no lleva argumentos) y limita capacidades. Always toma la primera elegible del orden configurado; en Manual queda sin Actor ni contexto hasta confirmar.
- Una fuente nueva con falla permanente se marca procesada; una falla recuperable del dueño queda pendiente. La explicación usa contenido privado vigente y `formrecords.read`; el front recibe títulos, nunca IDs.

## Audios

`WhatsAppAudioService`, `WhatsAppAudioDispatchService`, `WhatsAppAudioContentService`, `WhatsAppAudioAccessPolicy`, `WhatsAppAudioStoragePolicy`, `WhatsAppAudioPreparationPolicy`, `WhatsAppAudioPresentationPolicy`.

- Pedir la transcripción es idempotente por mensaje y exige solo Read; los GET no aceptan operaciones ni consultan Meta.
- Descarga, verificación Ogg/Opus y SHA-256 y `WriteOnce` de la clave determinista van fuera de la UoW; si otro host gana la escritura, se relee. Un pedido aceptado que pierde autorización corta antes de adquirir.
- Manual sin pedido solo copia. Copia hasta 25.000.000 bytes y normaliza hasta 1.000.000. Solo Completed publica el texto y `reasonCode` expone solo `tooLarge`. Reproducir revalida Read y retención después de abrir los bytes.

## Planillas

`WhatsAppSpreadsheetDispatchService`, `WhatsAppSpreadsheetPreparationPolicy`, `WhatsAppSpreadsheetReplyPolicy`.

- Quién la baja se decide antes de cualquier I/O y otra vez bajo lock, donde un cambio de vínculo o de modo durante la descarga la deja No disponible. Descarga, verificación, preparación por `IStructuredAttachmentReader` y `WriteOnce` con relectura van fuera de la UoW, que toma contacto y mensaje y guarda una vez lo que salió. Una excepción de cualquiera de ellos cuenta como una falla que puede pasar, con la escala de siempre. El nombre del archivo es el que normaliza el mensaje (`WhatsAppMessage.SpreadsheetName`), el mismo para el puerto y la operación.
- Un rechazo guarda el motivo y contesta con `AcceptTerminal` en una referencia automática propia del mensaje, que nombra su propio pedido como propuesta: el lector la deja fuera de la conversación (`FindChangeRequestAsync`) y de las ofertas sin resolver, así que un cambio o una pregunta pendientes siguen esperando sin copiarlos. Sin ventana, cuenta o idioma, se guarda el motivo sin contestar.

## Retención

`WhatsAppAssistantContentRetentionService` vacía un lote de hasta 500 por UoW; lo invoca la retención diaria de Infrastructure, sin otro job ni plazo.
