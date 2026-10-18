# WhatsApp: coordinación funcional

Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`WhatsAppInboundService` toma el contacto y el único límite; `WhatsAppInboundPolicy` clasifica metadata y selecciona acciones estructuradas; `WhatsAppAccessReplyPolicy` produce orientación pública sin leer identidad. `WhatsAppAccessActions` conserva IDs externos, y los textos viven en Access.resx/es/en. Perfil y registro conservan recorridos explícitos: `WhatsAppCodeIssuer` usa LoginCodeIssuer y la cuota común, con SignInCode o VerifyPhoneCode. Manual no interviene en acceso. El núcleo conserva el canje de enlaces pendientes.

## Al modificar

- ChatService coordina permisos HTTP, lectura propia y salida durable; DispatchService abre un límite separado para reservar/completar/recuperar. La llamada a Meta queda afuera de las transacciones. Las reglas están en «Chat de atención» de la fuente funcional.
- `WhatsAppPermissionCatalogSource` aporta códigos y textos del módulo al puerto genérico; el núcleo no los nombra.

- El procesamiento no crea cuentas, confirma teléfonos, vincula contactos ni emite enlaces secretos. No inferir acceso por frases. Policies y helpers no guardan.
- Los códigos usan el emisor común y una cuota propia por número; conservá propósito y límites aunque el pedido provenga de otro flujo.
- Los adaptadores de `Channels` implementan puertos del núcleo sin mover la implementación al núcleo.
- El módulo se registra en su bloque; no introducir referencias del núcleo a sus clases.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

- WhatsAppStructuredFormChannel aporta IStructuredFormChannel con ChannelId neutral phone. Consulta pura y disponible sin transporte habilitado; no acredita cuenta ni habilita aceptación.
- WhatsAppFlowCompiler es dueño del JSON 7.3, presupuesto, mapping y hash wire; perfil flow-static-7.3-r7 para compilaciones nuevas; choices con `source.catalog` usan un Dropdown dinámico con datos de pantalla declarados. Proyecciones selladas r3/r4 conservan sus bytes y codec versionado. ValueCodec preserva decimal exacto/string, DateOnly y ausencia distinta de false.
- Boolean usa RadioButtonsGroup Sí/No sin inicial; decimal TextInput text + gramática; fecha CalendarPicker single/required. es/en resuelve cada texto en su cultura exacta; nombre y ayudas presentes también completos, sin fallback ni traducción automática.
- Excesos se bloquean por identidad/ruta; no truncar ni dividir pantallas. Perfil conservador UTF-16, ayuda de Dropdown bloqueada sin variante calificada. Validación local no es upload/publicación/round-trip.

2b: canal también consume servicios propios Accept/Status/Preview. Accept verifica cuenta fuera de su UoW y sella proyecciones por es/en idempotentes. Dispatch reserva, marca el inicio del efecto y completa en UoW propios, con permisos actuales y fences; sólo marca InFlight justo antes del proveedor y libera rechazos seguros sin efecto. Nunca mantiene transporte dentro del límite. Sólo puertos neutrales hacia BusinessForms, IDs opacos sin tipos del otro módulo. Status/preview verifican owner/asset/perfil/health; diagnósticos por mapping, sin mensaje bruto. Revocación conserva IDs/Uncertain existentes.

WhatsAppFormService resuelve sólo vínculo explícito/cuenta/idioma actuales y consulta IStructuredFormSource neutral; no hereda permisos del operador. Revisión determinista de destinatario incluye actor/LinkRevision/estado/idioma/stamps con roles ordenados; no es autorización ni permite identidad/cultura cliente. Filtro sin resultados no altera elegibilidad general. Sin UoW para selección.

WhatsAppFlowReplyService valida token/sesión/proyección y decodifica sólo mediante el mapping sellado y ValueCodec antes de llamar a IStructuredRecordSink. Mensaje y sesión reciben estado final separado; errores de datos quedan terminales, fallas de infraestructura se reintentan. ChatQuery usa IStructuredRecordSource para valores y aplica formrecords.read sin nombrar BusinessForms.

K7: el GET de preparación exige Read+Send y deriva usuario/cultura del vínculo. Devuelve sólo campo, etiqueta, cantidad y problemas seguros; nunca las opciones del destinatario. Al encolar se resuelve de nuevo y el payload protegido fija la oferta; el transporte la lleva en los datos iniciales del Flow. El codec dinámico valida IDs de esa oferta. Un rechazo de opción Manual inactiva conserva sólo el nombre de pregunta; sin número ni valores, y sin reenvío automático.

Cierre de revisión: el adaptador del canal traduce UnsupportedConfiguration/PayloadMismatch del Accept a Failed sin reconciliación; no clasificar los errores de otros módulos por sus códigos. La captura registra un contador saturado en cinco fallas, incluidas respuestas Unavailable del sink; sigue reintentando al menos 15 minutos desde la primera falla persistida y sólo al cumplir ambos límites guarda Rejected/ProcessedAtUtc. Cancelar el worker no cuenta como falla. El rechazo sigue visible sin sesión y no muestra valores ni número.

F2: PhotoPicker/DocumentPicker abren pantalla, con preguntas posteriores en ella; el complete final referencia cada formulario por su pantalla. Nunca mandar archivos en navigate ni init-values. El mapping sellado conserva mínimos/máximos/origen; los diagnósticos usan pantalla + posición, no un índice global inventado. r3–r5 siguen sellados; el tope de instalación se fija al aceptar para que un cambio de configuración no altere una publicación en curso. Metadatos de archivos tienen un presupuesto separado de 64 KiB, sin ampliar el de respuestas escalares.

F3 toma metadatos de arrays del complete, los vincula al mapping sellado y descarga por IWhatsAppMediaClient antes del sink neutral. Sin bytes en Answers ni llamadas externas dentro de UoW. Una lectura no disponible usa el contador/edad existentes y conserva una razón segura; otros fallos de contenido rechazan. La marca HasFlowFiles permite mostrar Downloading/RetryingFiles sin valores.

WhatsAppFlowMediaRecorder acota a 90 segundos la captura completa. Los reintentos vencidos se filtran en el lector antes de Take, usando contador y primera falla existentes. El DTO de respuesta transporta cantidad de archivos y fecha recibida; el nombre de pregunta rechazada se obtiene de la proyección sellada, sin cambiar sus bytes ni el perfil.

Etapa 3: `WhatsAppStructuredRecordOriginSource` adapta el origen neutral por lote; sólo entrega ConversationId si conserva whatsapp.read antes y después. El canal, remitente e instante enriquecen respuestas ya autorizadas por el consumidor. No referencia BusinessForms ni envía mensajes al abrir una conversación.

G6: WhatsAppStructuredRecordActionChannel implementa el puerto neutral phone mediante WhatsAppFormActionService. Deduplica por RunId y usuario destinatario antes de resolver cambios de teléfono, y revalida cuenta activa, número confirmado, vínculo fijado y ventana de 24 h. Las salidas del sistema no requieren atención Manual ni permisos de un operador inexistente. ChatQuery oculta cuerpos FormAction sin lectura de registros; la lista siempre usa un preview genérico. Los límites del dispatch usan OperationLog y no llaman a Meta dentro de la transacción.

ReviewAsync aplica la decisión humana por ejecución/destinatario bajo su propio límite y lock de fila; no consulta Meta ni envía. Reviewed como salió satisface la acción por decisión humana y preserva la evidencia; no salió habilita sólo el retry explícito posterior.

El estado neutral conserva la revisión humana original en su salida por destinatario, separada de los receipts del proveedor. El consumidor decide si un fallo conocido admite retry: el aviso a un rol no ofrece reintento parcial.

WhatsAppFormActionService.ObserveAsync lee estados y receipts locales mediante IWhatsAppFormActionReader, un lote por canal. Reutiliza el mapeo neutral de estados, sin UoW ni contacto/proveedor; no transporta cuerpo, teléfono, wamid ni claves.

WhatsAppChatModeService es el caso separado de configuración: valida la cuenta activa y el permiso del destino (Send para Manual, ConfigureAssistant para Assistant), bajo una única UoW. Manual no depende de disponibilidad de IA, ventana24h ni salidas pendientes/inciertas. Assistant exige disponibilidad local del puerto neutral IAssistantOperations; la elegibilidad del destinatario y los límites se validan al ofrecer.

ChatQuery proyecta assistantAvailability obligatorio mediante lectura local IAssistantOperations.GetAvailabilityAsync; no acepta ni recupera operaciones al abrir chat y no consulta disponibilidad por cada fila de lista. IA apagada es notInstalled explícito.

Etapa 5 C2: nuevas publicaciones usan flow-static-7.3-r8 y conservan codec1: el wire de respuestas no cambió. Form.init-values usa mapas dinámicos por pantalla, declarados en data de la primera; sólo incluye valores escalares propuestos (todos string según el componente real), nunca archivos ni defaults inventados. WhatsAppFlowPrefillPolicy valida contra el mapping sellado, copia los mapas y rechaza prellenados no vacíos de perfiles anteriores; una sesión antigua sin prellenado conserva sus datos anteriores. No reescribir JSON/hash/bindings publicados r7.

H6: WhatsAppAudioService acepta el POST manual idempotente por Message.Id con sólo Read; el GET del hilo y del audio no aceptan operaciones ni consultan Meta. AudioDispatch adquiere y valida Ogg/Opus+SHA fuera de su UoW, recupera la clave privada determinista WriteOnce y sella el mismo RequestId antes de entregar la operación neutral al procesador existente. Complete guarda el resultado conocido con permisos/vínculo/vencimiento actuales. Sólo Completed publica Body; el modo Manual permite terminar la transcripción y el consumidor decide aparte si interpreta. AudioContent revalida Read y retención después de abrir bytes privados, sin URLs del proveedor. AudioRetentionSource protege también un objeto recuperable anterior al commit.

C3 manual conserva Actor operador y Subject destinatario separados: Request/Get/Discard/Send comparten una referencia durable por mensaje fuente, y el primer envío aceptado devuelve el mismo recibo a otros operadores. La lectura no acepta operaciones ni llama al proveedor. La confirmación revalida permisos/contexto y congela los IDs incluidos; el dueño del formulario valida valores y cultura efectiva completa. FormDispatch revalida después de esperar la cola, deriva init-values sólo del contenido sellado y nunca interpreta ausencia de operador como origen implícito.

AssistantContentRetentionService es el caso de uso del contenido privado: carga el lote seguido y guarda una sola vez; el scheduler anterior de Infrastructure lo invoca junto a la retención del texto. No suma un job ni otra política de plazo.

WhatsAppAssistantTerminalPolicy despacha sólo las propuestas terminales H4/H7 exactas a sus sinks neutrales, fuera de la UoW del canal. Conserva referencia, contexto, propuesta, procedencia y fecha original del mensaje; el dueño valida su permiso y contenido. Devuelve sólo ProposalId, cultura y un acuse fijo localizado (H4 enlaza la ruta normal del catálogo). En Manual no genera mensaje automático; replay del recibo H7 permite que la salida durable existente recupere el mismo acuse.

La presentación del asistente pertenece a WhatsAppAssistantPresentationPolicy: razón y valores de pantalla capturados sólo con lectura de respuestas y contenido fuente vigente. ChatQuery proyecta el puntero actual sin generar; las respuestas rápidas exponen títulos capturados, nunca IDs. La aceptación manual y el recibo compartido usan el mismo guard; Ready derivado del mensaje nuevo conserva Read+Send, mientras C4 histórico exige permiso en BusinessForms.

Captura asistida: FlowReplyService consume el verificador existente para cotejar la cabecera explícita (Conversation con operador real y Format1; Assistant sin operador y Format2), las identidades, el token y la proyección. El contenido privado sólo se lee bajo su propósito y retención vigentes; el origen sellado se pasa intacto a la captura cuyo Actor sigue siendo el destinatario. El legado manual sin blob conserva Origin=null. Un mensaje distinto tras vencimiento/purga queda no reconocido y no altera una sesión o registro ya completados; el mismo wamid procesado conserva su recibo. No se reconstruye metadata vencida ni se agrega recuperación nueva.

C3 automático usa la misma referencia, sesión y cola: revalida modo/vínculo/cuenta al aceptar; consulta primero el replay y cuenta hasta dos referencias por día del negocio. Una pregunta H5 y su Flow elegido cuentan una sola oferta y tienen RequestId de salida distintos. La elección recupera la propuesta existente con Get, nunca Submit, y conserva revisión/identidad originales. Terminales H4/H7 ejecutan al dueño fuera de la UoW tras un fence de contexto local; luego el canal confirma un acuse fijo en su única transacción. Manual registra el resultado sin contestar al celular ni gastar cupo.

C4 posterior a captura usa el mismo dispatcher y procesador entrante. Deduplica por contacto y registro confirmado, conserva el primer recibo y su retención, y elige la primera candidata Always en el orden de configuración. En automático excluye ciclos y definiciones rechazadas antes de elegir; Manual conserva la primera para revisión humana. La aceptación vuelve a comprobar ciclos y rechazos vigentes. Always Manual queda sin Actor/Context hasta la confirmación humana y no consulta Operations; Always automático usa Subject real, el mismo cupo y los guards existentes. Sin Always, sólo una indicación Assistant habilita interpretación con contexto restringido a sugerencias configuradas. El idioma del contexto proviene de la conversación/cuenta; BF elige aparte la cultura completa del destino. No se envía response_json al modelo.

ProcessContactAsync devuelve Result<bool>: false si el contacto está tomado o ausente, true si adquirió el lock aun cuando el acceso ya estaba clasificado. El procesador conserva ese skip antes de preparar audio o asistente; no vuelve a esperar la misma fila ni interpreta true como una respuesta enviada.

ActiveTurn/CapacityLimited son fallas transitorias neutrales: texto y audio mantienen Pending/Transcribing y recuperan el mismo UUID. Una referencia superada se cierra en la UoW del canal y se cancela por IAssistantOperations después del commit; Manual corta la interpretación automática, conserva STT ya aceptado por H6 y el POST a Meta ya iniciado.

Ofertas y rechazos: la lectura recibe un conjunto de hasta cien DefinitionIds de las candidatas y devuelve sólo sus rechazos duraderos, separado de las últimas veinte referencias ofrecidas en veinticuatro horas. El conjunto no concede elegibilidad ni prueba ausencia; los guards vigentes siguen consultando el rechazo real antes de enviar. El envelope JSON completo comparte 16 KiB; se conservan los nombres completos de hasta 120 caracteres y sólo entran las referencias recientes que caben.

C4 fija SourceRecordId desde la captura confirmada al crear el contexto. La atención normal no ofrece capacidades de C4; ForCapturedRecord arma su catálogo explícito y la lectura de sugerencias usa {}. No reconstruir el origen de un contexto guardado ni modificar la atribución de respuestas existentes.

El cupo de dos ofrecimientos del día del negocio se comprueba al crear una referencia automática, antes de devolver trabajo para Submit, y se revalida al encolar. Agotado queda Blocked sin LLM; replay de operación aceptada y elección de aclaración siguen su recorrido previo. Un Pending previo sin cupo devuelve RecoverOnly: el pipeline consulta el UUID, pero NotFound termina Blocked sin aceptar otra operación.

Los fallos permanentes de una fuente nueva (ventana vencida, cuenta o contexto inválidos) se cierran marcando AssistantProcessedAtUtc bajo la UoW del canal. Una falla recuperable del dueño conserva la captura pendiente. Audio automático inválido queda Unavailable con código; Manual guarda la copia sin crear un pedido de IA y STT ya aceptado conserva H6.

Audio: copia privada hasta 25.000.000 bytes, normalización de WhatsApp hasta 1.000.000 antes del Submit. Un exceso de normalización conserva el objeto reproducible, queda Unavailable/TooLarge y no crea trabajo del motor; no cambia el perfil STT ni afirma duración/latencia.

El audio Manual sin solicitud guarda la copia privada sin capturar actor/contexto IA, aunque el contacto no tenga usuario vinculado. La normalización sólo se prepara con un pedido explícito o modo Asistente; el límite de normalización no restringe esa copia.

Las capacidades forms.catalog-filters.metadata/propose se ofrecen sólo al canal Asistente automático; Atención manual no las incluye. Pedidos faltantes y C4 conservan sus recorridos propios.

El cupo por día local usa el primer minuto válido cuando una frontera de medianoche cae en un salto de horario. Se resuelven ambas fronteras por separado en la zona capturada.

El polling de una normalización Transcribing con sello privado válido reutiliza clave/tamaño y el mismo UUID sin abrir el blob; STT verifica el archivo al consumirlo. Las fallas recuperables de descarga esperan la escala de Flows (30, 90, 210, 450 y 930 segundos desde la primera falla) antes de ocupar un lugar del escaneo.
