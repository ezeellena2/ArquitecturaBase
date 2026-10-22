# Services: casos de uso de WhatsApp

Casos de uso del módulo (`*Service`, detrás de `Interfaces/Services`), sus helpers (`*Policy`, `*Guard`, `*Issuer`, `*Verifier`, `*Linker`, `*Recorder`) y tipos estáticos puros. Los servicios abren el límite; los helpers validan, deciden o traducen sin guardar. Los workers de Infrastructure reservan, marcan el efecto y completan: Meta, IA y almacenamiento quedan entre dos límites.

## Al modificar

- Reservar, marcar el inicio del efecto y completar son UoW separadas, con lease, fence o reserva propia. Lo incierto se consulta antes de repetir; nunca se reenvía a ciegas.
- `WhatsAppWebhookPersistence` y `WhatsAppFlowReplyStateService` son unidades que otro servicio invoca a propósito: no copies ese patrón.

### Acceso, códigos y teléfono

`WhatsAppLoginCodeService`, `ProfileWhatsAppService`, `WhatsAppCodeIssuer`, `WhatsAppCodeQuotaGuard`, `WhatsAppContactLinker`, `WhatsAppIds`.

- `WhatsAppCodeIssuer` valida el país del número interpretado, aplica el tope diario global de `WhatsAppCodeQuotaGuard` (24 h móviles, todos los números) y emite con `LoginCodeIssuer`, dueño de los límites por destino. Conserva el propósito (SignInCode o VerifyPhoneCode) y marca enviado solo si la cola tomó el mensaje; corre en el límite del llamador.
- `EnsureEnabled*` va antes del límite. Confirmar el número usa `OnAnyResult` para gastar el código. Verificación y canje de enlaces son del núcleo; `WhatsAppContactLinker` solo actúa en cambios explícitos.

### Webhook y entrada

`WhatsAppWebhookService`, `WhatsAppWebhookPersistence`, `WhatsAppInboundService`, `WhatsAppInboundPolicy`, `WhatsAppAccessReplyPolicy`.

- El webhook valida la firma, guarda y avisa a la señal después del commit; ante un 23505 reintenta en un scope nuevo. La persistencia siempre devuelve éxito: Meta reintentaría un error por días.
- `ProcessContactAsync` devuelve `Result<bool>`: false si el contacto está tomado o no existe; true si tomó el lock, aunque no haya respondido. El procesador no prepara audio ni asistente con false.
- `WhatsAppInboundPolicy` clasifica y elige la acción de acceso; `WhatsAppAccessReplyPolicy` orienta sin leer identidad. Si la cola rechaza la orientación, se lanza y se revierte.

### Chat de atención y modos

`WhatsAppChatService`, `WhatsAppChatQueryService`, `WhatsAppChatModeService`, `WhatsAppOutgoingPresentationPolicy`.

- Enviar exige Manual, ventana, destino y ninguna salida Pending, Sending o Uncertain; mismo operador y `requestId` devuelven la operación; otro texto, 409.
- Cambiar el modo es otro caso, en una UoW: Manual pide `whatsapp.send`; Assistant, `whatsapp.configureassistant` y IA disponible. Manual no depende de IA, ventana ni salidas pendientes, y cancela después del commit las interpretaciones automáticas no Always.
- Las consultas no aceptan ni recuperan operaciones. La disponibilidad de IA se lee por hilo, no por fila (apagada: `notInstalled`). `IStructuredRecordSource` decide `formrecords.read`: sin él se ocultan cuerpos de avisos, valores y explicación.

### Salida durable y entrega

`WhatsAppChatDispatchService`, `WhatsAppOutgoingSendGuard`, `WhatsAppManualSendGuard`, `WhatsAppDeliveryService`.

- Reservar revalida con `WhatsAppOutgoingSendGuard` según el origen: operador real con Read+Send, destinatario de avisos, o modo, vínculo, contexto y contenido del asistente. Un Sending vencido queda Uncertain; avisos y asistente vuelven a Pending solo si no marcaron el inicio del efecto.
- Avisos y asistente marcan el efecto en otra UoW, revalidando justo antes del POST; completar exige la misma reserva.
- `WhatsAppDeliveryService` registra lo que salió por `IWhatsAppSendQueue` después del HTTP y bloquea el wamid antes de leer: el receipt puede llegar primero.

### Flows: compilación

`WhatsAppFlowCompiler`, `WhatsAppFlowScreenBuilder`, `WhatsAppFlowValueCodec`, `WhatsAppFlowPrefillPolicy`.

- Funciones puras: JSON 7.3, presupuestos, mapping y hash de los bytes exactos. Lo nuevo usa `WhatsAppFlowProfile.Revision` con codec 1; nunca recompiles ni reescribas JSON, hash o bindings sellados.
- Un exceso se bloquea con identidad y ruta, sin truncar ni repartir pantallas. Cada pregunta de archivo abre pantalla; nunca archivos en navigate ni init-values. Un Flow con archivos presupuesta 64 KiB y uno escalar sigue en 16 KiB; el tope por archivo se sella al aceptar.
- El codec conserva decimal exacto, DateOnly y ausencia distinta de false. MultiChoice usa CheckboxGroup (1–20 opciones), rango sellado y GUID D distintos en orden; [] explícito se distingue de omisión. Selection null se omite en mapping y proyección.
- El prellenado valida sólo escalares contra el mapping y omite mapas vacíos; varias no declara init-values ni acepta claves. Reconoce PrefillRevisions r8/r9; lo nuevo compila r9 y otros perfiles conservan envío sin prellenado. Nunca reescribir JSON, hashes ni bindings r8.

### Flows: publicación, estado y retiro

`WhatsAppFlowPublicationService`, `WhatsAppFlowPublicationPolicy`, `WhatsAppFlowDispatchService`, `WhatsAppFlowStatusService`, `WhatsAppFlowDeprecationService`.

- Aceptar verifica la cuenta fuera de su UoW, sella manifiesto y una proyección por cultura, y repite la respuesta por `OperationId` y huella.
- Publicar avanza con permisos vigentes, fence e InFlight marcado justo antes del proveedor; un rechazo seguro libera el paso. Un payload ilegible se pospone sin tocar la incertidumbre.
- Los diagnósticos se mapean por pantalla y posición al mapping sellado, nunca con el texto de Meta.
- Retirar toma el lock de selección del dueño antes que el de Flow y revalida, al reservar y antes del POST, la versión no usada y las sesiones o capturas pendientes. Usa el manifiesto sellado, sin permisos del actor ni escribir en BusinessForms; su lease y fence no tocan la publicación. Todo 4xx del retiro exige GET antes de otro POST. NeedsAttention y el contador durable dan espera creciente a rechazos persistentes, cuenta no disponible/distinta y manifiesto ilegible, sin borrar InFlight ni habilitar reenvío por un fallo de cuenta.

### Envío de formularios

`WhatsAppFormService`, `WhatsAppFormRecipientRevision`, `WhatsAppFormSendService`, `WhatsAppFormSendPolicy`, `WhatsAppFlowSessionIssuer`, `WhatsAppFlowSessionVerifier`, `WhatsAppFormDispatchPolicy`, `WhatsAppFormMessageDispatchService`.

- Selección y preparación usan la cuenta vinculada y sus permisos vía `IStructuredFormSource`, sin UoW; nunca toman destinatario ni cultura del navegador y no devuelven opciones de catálogo. Un filtro sin resultados no cambia la elegibilidad.
- `recipientContextRevision` es determinista y no autoriza. Enviar vuelve a preparar, toma el lock de selección, revalida y sella oferta, cultura preparada y token por hash en la sesión.
- Al despachar se revalida tras la cola por revisión de cuenta, sin exigir la misma cultura; init-values salen solo del contenido sellado. Sin operador solo vale el origen Assistant.

### Respuestas de Flow

`WhatsAppFlowReplyService`, `WhatsAppFlowReplyStateService`, `WhatsAppFlowMediaPolicy`, `WhatsAppFlowMediaRecorder`.

- Verifica token, cabecera (Conversation con operador y formato 1; Assistant sin operador y formato 2), sesión y proyección, y decodifica con el mapping sellado y la oferta antes del sink neutral. El Actor es quien completa; origen y cultura de sesión viajan intactos.
- Los arrays se clasifican por mapping: multiChoice exige strings GUID D ofrecidos, distintos y dentro del rango; sólo photo/document pasan objetos a MediaPolicy. Claves ajenas, escalares sustituidos y mezclas se rechazan antes de I/O. El contador preliminar excluye strings; [] ambiguo conserva presupuesto64, pero sólo un conteo > 0 marca descarga.
- Mensaje y sesión guardan su estado final aparte. Datos o contenido inválidos son terminales; medio o sink no disponibles y fallas técnicas usan el contador existente. Los bytes van como archivos, nunca en las respuestas, y se descargan fuera de UoW en 90 s por captura.
- Vencida o sin vínculo, solo se recupera un recibo idéntico; un mensaje distinto tras purgar el contenido privado no altera la sesión completada.

### Avisos de reglas

`WhatsAppFormActionService`, `WhatsAppFormActionPolicy`.

- Encolar deduplica por run y destinatario antes de resolver el contacto; exige cuenta activa, número confirmado igual al del contacto, vínculo y ventana, sin Manual ni permisos de operador.
- Revisar aplica la decisión humana bajo lock, sin Meta: «salió» cuenta como aceptado y «no salió» habilita solo el reintento explícito de un Failed. Observar lee estados y receipts locales por lote, sin UoW, cuerpo, teléfono ni wamid.

### Asistente

`WhatsAppAssistantService`, `WhatsAppAssistantDispatchService`, `WhatsAppAssistantSuggestionPolicy`, `WhatsAppAssistantAcceptancePolicy`, `WhatsAppAssistantAccessPolicy`, `WhatsAppAssistantAutomaticPolicy`, `WhatsAppAssistantOfferCapacityPolicy`, `WhatsAppAssistantTerminalPolicy`, `WhatsAppAssistantPresentationPolicy`, `WhatsAppAssistantReplyCodec`.

- Una referencia durable por mensaje fuente o captura, con Actor (operador) y Subject (destinatario) separados. En Manual el primer envío aceptado se devuelve a otros operadores; en automático Actor es Subject y no se inventa operador. El canal fija InteractionMode neutral en texto y audio, y comprueba concordancia con su ID privado y tipo durable al revalidar o llamar al dueño; los consumidores reciben el modo sin interpretar el ID.
- Consultar, aceptar o cancelar operaciones de `IAssistantOperations` va fuera de la UoW del canal; lo superado se cancela después del commit. ActiveTurn y CapacityLimited conservan Pending y el UUID.
- Aceptar revalida modo, vínculo, cuenta, contexto, ciclos y rechazos bajo lock. Hasta dos ofertas por conversación y día del negocio (primer minuto válido si la medianoche cae en un salto horario); pregunta y Flow elegido cuentan una y usan salidas distintas. Elegir recupera con Get, nunca Submit; sin cupo, un pendiente previo solo recupera.
- Catálogo propuesto y formulario faltante llaman al dueño fuera de la UoW tras verificar el contexto; luego la salida existente deduplica un acuse fijo localizado (el de catálogo enlaza su ruta normal). Manual no contesta ni gasta cupo.
- Tras una captura, `ForCapturedRecord` fija el registro (al modelo va solo su ID; la lectura de sugerencias no lleva argumentos) y limita capacidades. Always toma la primera elegible del orden configurado; en Manual queda sin Actor ni contexto hasta confirmar.
- Una fuente nueva con falla permanente se marca procesada; una falla recuperable del dueño queda pendiente. La explicación usa contenido privado vigente y `formrecords.read`; el front recibe títulos, nunca IDs.

### Audios

`WhatsAppAudioService`, `WhatsAppAudioDispatchService`, `WhatsAppAudioContentService`, `WhatsAppAudioAccessPolicy`, `WhatsAppAudioStoragePolicy`, `WhatsAppAudioPreparationPolicy`, `WhatsAppAudioPresentationPolicy`.

- Pedir la transcripción es idempotente por mensaje y exige solo Read; los GET no aceptan operaciones ni consultan Meta.
- Descarga, verificación Ogg/Opus y SHA-256 y `WriteOnce` de la clave determinista van fuera de la UoW; si otro host gana la escritura, se relee. Un pedido aceptado que pierde autorización corta antes de adquirir.
- Manual sin pedido solo copia. Copia hasta 25.000.000 bytes y normaliza hasta 1.000.000. Solo Completed publica el texto y `reasonCode` expone solo `tooLarge`. Reproducir revalida Read y retención después de abrir los bytes.

### Retención

`WhatsAppAssistantContentRetentionService` vacía un lote de hasta 500 por UoW; lo invoca la retención diaria de Infrastructure, sin otro job ni plazo.

## Referencias

- Reglas: [chat](../../../../../docs/features/whatsapp.md#chat-de-atención--2026-10-02), [publicación](../../../../../docs/features/whatsapp.md#publicación-durable-de-flows--2b-2026-10-10), [retiro](../../../../../docs/features/whatsapp.md#deprecación-durable-de-flows--2026-10-21), [reintentos de captura](../../../../../docs/features/whatsapp.md#rechazos-y-reintentos-de-formularios--2026-10-13), [archivos](../../../../../docs/features/whatsapp.md#fotos-y-documentos-en-flows--2b-2026-10-15), [avisos](../../../../../docs/features/whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6), [sugerencias](../../../../../docs/features/whatsapp.md#sugerencias-al-operador-y-explicación--etapa-5), [audio](../../../../../docs/features/whatsapp.md#audio-recibido--etapa-5-h6) y [ofrecimientos](../../../../../docs/features/whatsapp.md#ofrecimientos-automáticos--etapa-5).
- Formularios: [idioma](../../../../../docs/features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19), [prellenado](../../../../../docs/features/formularios.md#propuestas-de-prellenado--etapa-5) y [qué sugerir después](../../../../../docs/features/formularios.md#configuración-de-qué-sugerir-después--etapa-5).
- [Pruebas de servicios](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/Services).

## Despertar los workers

Los avisos son locales, sin contenido y posteriores al commit: el webhook despierta entrada y, sólo con `nfm_reply`, captura; una captura Completed despierta entrada para evaluar qué sugerir después. Los resultados terminales de IA y audio llegan al canal mediante el puerto neutral de Application. El estado durable y el polling siguen siendo la recuperación.

La reserva de salida informa progreso sólo al confirmar Pending→Sending o Pending→Failed. Una reserva con progreso y sin operación representa un rechazo persistido, no un envío. No cambiar por esto reservas, leases, fences ni el coordinador de despacho.
