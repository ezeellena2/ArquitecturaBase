# Revisión de arquitectura: WhatsApp — 2026-11-12

Primera corrida de [revisar la arquitectura de un desarrollo](../guides/revisar-arquitectura.md). Es de
solo lectura, sobre el backend en `9c0eb048` y el front en `1b0c45e3`; no incluye los cambios sin
commitear de otras sesiones (ese día seguía en curso la lectura de documentos por WhatsApp).

- **Revisión:** nueve revisores contestaron los 30 criterios, por bloque.
- **Verificación:** siete verificadores intentaron refutar cada hallazgo. Ninguno se refutó entero: uno
  pasó a inventario (separar el módulo), se retiró una causa (la salida durable sí le avisa a la salud
  un problema del token) y la mayoría quedó con ajustes de alcance, horizonte o arreglo, ya incorporados
  abajo.
- **Contraste:** un revisor independiente comparó después este informe con los originales y con el
  código.
- **Sin ejecución:** no se corrió nada. Los tiempos y los techos salen del código, y las pruebas que los
  confirmarían están en [controles y pruebas](#controles-y-pruebas-que-conviene-sumar).

## Resumen

- **Lo fuerte.** Cinco criterios se cumplen en los tres horizontes: estado por pedido, ediciones
  simultáneas, paginado, entradas hostiles e integridad en la base. El estado de cada pedido está bien
  resuelto y probado:
  - cada conversación conserva su contexto aunque el modelo de otra quede colgado;
  - seis webhooks iguales en paralelo dejan un solo mensaje.
- **Bugs funcionales, fuera de los criterios.** Las preguntas con botones de un caso del asistente no
  salían nunca ([B1](#b1-las-preguntas-con-botones-de-un-caso-no-salen)). Al arreglarlo, la revisión
  del arreglo encontró dos más en la misma validación, que quedaron arreglados con él (B2 y B3), y uno
  raro que sigue pendiente (B4). H1 también quedó arreglado el mismo día.
- **Hoy, con una sola copia**, lo que más pesa:
  - el chat se traba con un envío incierto (H1 y H2);
  - con Redis caído se pierden códigos e invitaciones (H3);
  - «Probar» deja leer chats ajenos (H4);
  - si falla el motor de IA, la persona queda sin respuesta (H5);
  - las acciones de una respuesta del asistente salen desordenadas (H6).
- **Con varias copias**, fallan dos cosas: el orden entre copias (H23) y la convivencia de versiones en
  un despliegue (H25), que también toca a una sola copia. Quedan con reservas el trabajo duplicado de
  las respuestas de Flow (H24) y los límites y la salud de cada copia (H26).
- **A gran escala**, el techo está en cinco lugares:
  - las consultas que recorren toda la historia (H27 a H30);
  - la salida de a un mensaje por copia (H31);
  - el lock global de formularios (H32);
  - la consulta a Meta por cada formulario (H33);
  - los índices que se crean bloqueando la tabla (H34).
- **Lo que te toca decidir:**
  - quién puede revisar un envío incierto (H2);
  - si una falla del motor deriva a una persona (H5);
  - si el chat se cifra en la base (H16);
  - si un toque repetido vuelve a mandar la confirmación de lo ya aplicado (H21);
  - si la respuesta de un operador pasa adelante de los avisos de un rol (H31);
  - qué horizonte conviene pagar.

## Ficha

**Entradas**

- **Rutas** (`src/ArquitecturaBase.Api/Modules/WhatsApp/Controllers`):
  - `webhooks/whatsapp`: verificación (GET) y eventos de Meta (POST), anónima y firmada.
  - `account/login-code/whatsapp`: pedir un código de ingreso (POST, anónima).
  - `api/me/whatsapp`: el vínculo de la cuenta con su número.
  - `api/whatsapp/chats`: bandeja, historial, leído, enviar y revisar una salida incierta; además,
    modo del chat, formularios por chat, audio y sugerencias del asistente (cinco controllers).
  - `api/whatsapp/assistant-agent`: configuración y pruebas del asistente.
- **Workers** (`src/ArquitecturaBase.Infrastructure/Modules/WhatsApp`):
  - `WhatsAppSenderBackgroundService`: la cola de acceso (códigos e invitaciones).
  - `WhatsAppOutgoingBackgroundService`: la salida durable.
  - `WhatsAppInboundProcessor`: lo que entra por el webhook.
  - `WhatsAppFlowBackgroundService` y `WhatsAppFlowDeprecationBackgroundService`: publicar y retirar Flows.
  - `WhatsAppFlowReplyBackgroundService`: las respuestas de Flow.
  - `WhatsAppMessageRetentionService`: la retención.
- **Pantallas:** `../ArquitecturaBaseFront/src/features/whatsapp`.

**Datos**

- **Tablas:** `WhatsAppContacts`, `WhatsAppMessages`, `WhatsAppOutgoingMessages`,
  `WhatsAppDeliveryReceipts`, `WhatsAppReadStates`, `WhatsAppFlowBindings`, `WhatsAppFlowOperations`,
  `WhatsAppFlowSessions`, `WhatsAppAssistantAgents` y `WhatsAppAssistantRequests`.
- **Redis:** la lease por destino de `WhatsAppDispatchCoordinator`, que comparten la cola de acceso y
  la salida durable.
- **Cola de acceso cifrada:** `WhatsAppSendQueue`, `WhatsAppQueuedMessageCodec` y `WhatsAppMessageSource`,
  sobre `QueuedDeliveries` en PostgreSQL.
- **Archivos:** las copias de audios y planillas recibidos van al almacenamiento privado; los archivos
  de las respuestas de Flow los guarda BusinessForms.

**Terceros**

- **Meta Graph API:** mensajes (`WhatsAppCloudClient`), Flows (`WhatsAppFlowClient` y
  `WhatsAppFlowEnvironment`) y medios (`WhatsAppMediaClient` y `WhatsAppMediaUploadClient`).
- **Por puertos:** el módulo de IA (asistente, audio y planillas) y BusinessForms (envío y recepción de
  formularios).

**Reglas:** [WhatsApp](../features/whatsapp.md) y sus [servicios](../features/whatsapp-servicios.md); los
ADR [0007](../decisions/0007-whatsapp-como-modulo-opcional.md),
[0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md),
[0015](../decisions/0015-colas-transaccionales-cifradas.md) y
[0018](../decisions/0018-robustez-proporcional-al-efecto.md); el
[diseño del ingreso](../specs/2026-09-22-ingreso-whatsapp-design.md) y los `AGENTS.md` del módulo en cada capa.

**Pruebas:** integración en `tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp` (142
archivos), unitarias de Application (118) y de Domain (22), y arquitectura (5).

**Tamaño:** 372 archivos `.cs` (Api 23, Application 230, Domain 41 e Infrastructure 78), más 12 `.resx`.

## Matriz

| # | Criterio | Hoy | Varias copias | Gran escala | Hallazgos |
| --- | --- | --- | --- | --- | --- |
| 1 | Estado por pedido | Cumple | Cumple | Cumple | — |
| 2 | Ediciones simultáneas | Cumple | Cumple | Cumple | — |
| 3 | Repeticiones | Con reservas | Con reservas | Con reservas | H7 |
| 4 | Orden | No cumple | No cumple | No cumple | H6, H23 |
| 5 | Todo o nada | Con reservas | Con reservas | Con reservas | H21 |
| 6 | Cada uno lo suyo | Con reservas | Con reservas | Con reservas | H2, H4, H20 |
| 7 | Sin estado propio | Cumple | Con reservas | Con reservas | H26 |
| 8 | Trabajos de fondo | Cumple | Con reservas | No cumple | H24, H30 |
| 9 | Arranque, apagado y configuración | Con reservas | Con reservas | Con reservas | H8 |
| 10 | Despliegue sin corte | Con reservas | No cumple | No cumple | H25, H34 |
| 11 | Consultas | Con reservas | Con reservas | No cumple | H27, H29, H30 |
| 12 | Paginado y exportaciones | Cumple | Cumple | Cumple | — |
| 13 | Filtros y búsqueda | Con reservas | Con reservas | No cumple | H4, H28 |
| 14 | Caché | No cumple | No cumple | No cumple | H3 |
| 15 | Trabajo pesado fuera del pedido | Con reservas | Con reservas | Con reservas | H33 |
| 16 | Filas únicas | Con reservas | Con reservas | No cumple | H12, H31, H32 |
| 17 | Plazos y reintentos | Con reservas | Con reservas | No cumple | H7, H11, O1 |
| 18 | Estado incierto | No cumple | No cumple | No cumple | H1, H2, H7 |
| 19 | Degradación | No cumple | No cumple | No cumple | H3, H5, H9, H33 |
| 20 | Límites y costo | Con reservas | Con reservas | Con reservas | H10, H26 |
| 21 | Acceso | Con reservas | Con reservas | Con reservas | H4, H20 |
| 22 | Entradas hostiles | Cumple | Cumple | Cumple | — |
| 23 | Datos sensibles | Con reservas | Con reservas | Con reservas | H16 |
| 24 | Integridad en la base | Cumple | Cumple | Cumple | — |
| 25 | Privacidad y respaldo | No cumple | No cumple | No cumple | H13, H14, H15, H30 |
| 26 | Idioma | Con reservas | Con reservas | Con reservas | H17, H18 |
| 27 | Fechas, zonas horarias y números | Con reservas | Con reservas | Con reservas | H18 |
| 28 | Errores | Con reservas | Con reservas | Con reservas | H5, H9 |
| 29 | Observabilidad | Con reservas | Con reservas | No cumple | H9, H19 |
| 30 | Separabilidad | Con reservas | Con reservas | Con reservas | H22 y el [inventario](#separar-el-módulo-inventario-no-defecto) |

## Hallazgos

Cada hallazgo dice dónde empieza a fallar, el escenario, la evidencia (archivo y línea en `9c0eb048`)
y el arreglo con su esfuerzo. Cuando el comportamiento viene de una decisión documentada, se cita la
fuente: cambiarla lo decidís vos.

### Fuera de los criterios

#### B1. Las preguntas con botones de un caso no salen

- **Dónde falla:** hoy, siempre. Lo encontró un verificador y se confirmó leyendo el código.
- **Escenario:** el asistente contesta con un caso que pregunta «¿Registrar?» con botones. La pregunta
  nunca le llega a la persona: queda fallida con `InvalidProposal`, sin llamar a Meta.
- **Evidencia:** la pregunta se guarda como la salida de su referencia (`WhatsAppAssistantReplyPolicy.cs:160-165,181-182`,
  `WhatsAppAssistantRequest.cs:75-81`). Al despacharla, `WhatsAppOutgoingSendGuard.cs:72-76` la toma por
  un resumen «Crear / Cambiar algo» y sus ids `CASE_BUTTON:` no pasan; la reserva la marca fallida
  (`WhatsAppChatDispatchService.cs:23-27`). Ningún test despacha una pregunta de caso por esa validación.
- **Arreglo:** una rama propia para las preguntas de caso, antes de la de los resúmenes, que valide sus
  ids `CASE_BUTTON:` con `WhatsAppAssistantCaseButtonCodec` contra la referencia y la acción guardada.
  Restringir sólo la rama de los resúmenes no alcanza: la pregunta caería en la de las ofertas de
  formulario (`WhatsAppOutgoingSendGuard.cs:77-89`), que pide de dos a tres botones `FORM_ASSISTANT:`.
  Con un test de integración que despache una pregunta de un botón y otra de tres. Chico.
- **Arreglado el 2026-11-12** con una rama propia: todos los botones tienen que ser `CASE_BUTTON:` de
  esa referencia y cada uno en su lugar, lo que también los acota a tres. No se comparan con la acción
  guardada: si el negocio cambió un botón después de la pregunta, el toque deriva con `button`. Lo
  prueban una teoría sobre el guard con uno y tres botones, de otra referencia, fuera de lugar o
  mezclados, y un test que pasa por el guard real la pregunta que encola `ApplyAsync`; los dos fallan
  sin el arreglo.

#### B2. El resumen de importar no salía

- **Dónde fallaba:** hoy, siempre; lo encontró la revisión del arreglo de B1.
- **Escenario:** llega una planilla con «Importala en Clientes» y el asistente arma el resumen con
  «Revisar importación» y «Cambiar algo». La rama de los resúmenes sólo aceptaba «Crear» y «Cambiar
  algo», así que la salida quedaba fallida sin llamar a Meta.
- **Arreglado el 2026-11-12:** la rama acepta «Cambiar algo» con «Crear» o con «Revisar importación».
  El test de planillas ahora despacha ese resumen con el worker y comprueba que sale.

#### B3. Una pregunta de caso tapaba el último resumen

- **Dónde fallaba:** hoy, en una conversación con un resumen y después una pregunta de caso.
- **Escenario:** el asistente manda un resumen y después el agente contesta con una pregunta de caso.
  La persona toca «Crear borrador» del resumen y recibe «Esa propuesta ya cambió», porque el lector
  tomaba la pregunta, que también tiene botones, por el último resumen.
- **Arreglado el 2026-11-12:** el último resumen es la salida propia con botones y sin el porqué del
  agente, que toda pregunta de caso lleva. Lo prueba un test de integración del lector.

#### B4. Dos botones de un caso pueden salir con el mismo título

- **Dónde falla:** hoy; es raro. Lo encontró la revisión del arreglo de B1 y sigue pendiente.
- **Escenario:** el original del agente es español. Un botón dice «OK» sólo en español y otro dice
  «Listo» en español y «OK» en inglés. En una conversación en inglés salen dos «OK»: el mensaje de
  Meta exige títulos distintos, la salida queda fallida y la conversación no deriva.
- **Evidencia:** la regla del agente compara los títulos en cada idioma sin el respaldo al original
  (`WhatsAppAssistantAgent.Rules.cs:87-91`, y lo mismo en el front, `assistantAgentDraft.ts:181-184`),
  mientras que la respuesta manda el título con ese respaldo (`WhatsAppAssistantReplyPolicy.cs:261`).
- **Arreglo:** validar los títulos como los ve la persona, con el respaldo al original, en el dominio y
  en el front, o derivar cuando dos títulos coinciden. Chico.

### Hoy, con una sola copia

#### H1. Un aviso o una respuesta del asistente inciertos traban el cuadro de respuesta

- **Dónde falla:** hoy, en el front.
- **Escenario:** un aviso de una regla o una respuesta del asistente quedan inciertos. El cuadro de
  respuesta se apaga para todos los operadores («Hay un envío pendiente o sin confirmar»), y también
  «Enviar formulario» y la sugerencia, aunque la API aceptaría el envío. Un aviso se destraba desde el
  detalle de la respuesta; uno del asistente no tiene ninguna salida. El peor caso es el mensaje fijo
  de derivar, que sale justo cuando la conversación pasa a una persona.
- **Evidencia:** `replyAvailability.ts:4` del front cuenta las salidas de cualquier origen; el servidor
  sólo bloquea por las de origen Conversation (`WhatsAppConversationRepository.cs:36-39`). La regla
  dice que «las acciones inciertas no bloquean la atención manual» (`docs/features/whatsapp.md:260`).
- **Arreglo:** contar sólo `origin === "Conversation"`, con su test. Una línea.
- **Arreglado el 2026-11-12** en el front, con un test de la regla que falla sin el arreglo.

#### H2. Un envío incierto traba el chat y sólo lo destraba quien lo mandó

- **Dónde falla:** hoy, con más de un operador.
- **Escenario:** Ana responde, Meta no contesta y el mensaje queda incierto; Ana termina su turno.
  Nadie más puede responder, mandar un formulario ni aceptar una sugerencia en ese chat (409), y la
  pregunta con botones del asistente automático termina en silencio. Si a Ana la dan de baja, el chat
  queda trabado para siempre: lo incierto no vence.
- **Evidencia:** el bloqueo mira las salidas de todos los operadores (`WhatsAppConversationRepository.cs:36-39`,
  usado en `WhatsAppChatService.cs:61`, `WhatsAppFormSendService.cs:61`, `WhatsAppAssistantAcceptancePolicy.cs:82`
  y `WhatsAppAssistantAutomaticPolicy.cs:73`); la revisión, sólo las propias (`WhatsAppChatService.cs:94`;
  `ChatTimeline.tsx:63` en el front). Cada regla está escrita por separado (`docs/features/whatsapp.md:53,56`);
  su combinación, no.
- **Arreglo:** que pueda revisar cualquiera con `whatsapp.send`, guardando quién lo hizo, o que lo
  incierto deje de bloquear a los demás después de un plazo. Chico. **Lo decidís vos:** cambia una
  regla documentada.

#### H3. Con Redis caído se pierden códigos e invitaciones

- **Dónde falla:** hoy, ante cualquier corte o cambio de nodo de Redis, o ante el turno que deja tomado
  una copia que se cortó de golpe (60 s).
- **Escenario:** Redis se reinicia unos segundos con mensajes en cola.
  - La cola de acceso no consigue el turno para escribirle a esa persona (la lease, en Redis), lo toma
    como un error del programa y descarta el mensaje: el código no llega (y cuenta para el tope diario)
    y la invitación queda fallida.
  - Los avisos y el mensaje de derivar pasan a fallidos sin haberse intentado.
  - Los mensajes manuales y del asistente quedan pendientes y, como la fila es única, frenan a los avisos
    que vienen detrás.
  - Si la espera vence por el turno que dejó tomado una copia cortada, también los mensajes manuales y
    del asistente quedan fallidos sin haberse intentado.
- **Evidencia:** `CachingRegistration.cs:24-28` (`FailFast`), `WhatsAppDispatchCoordinator.cs:18-28`,
  `WhatsAppSenderBackgroundService.cs:53,63-69`, `DurableMessageSource.cs:36,49-50` y
  `WhatsAppOutgoingBackgroundService.cs:72,104-107,116-117`. Ni el ADR 0009 ni el 0015 lo vuelven
  intencional: se sabe que Meta no se llamó, y el dominio ya sabe devolver una reserva a pendiente
  (`WhatsAppOutgoingMessage.cs:252-258`).
- **Arreglo:** si no se llamó al proveedor, devolver la fila a pendiente con una espera; en la cola de
  acceso, reintentar el turno en vez de descartar. Mediano.

#### H4. «Probar» deja leer chats ajenos y buscar correos sin permiso

- **Dónde falla:** hoy, con un rol propio que tenga «Configurar el asistente» y no tenga `whatsapp.read`
  (lee el chat) o no tenga `users.read` (ve y busca correos). Con los roles del seed no pasa.
- **Escenario:** alguien con ese rol elige a Ana en «Probar» y pregunta «¿qué te conté hoy?». El modelo
  lee hasta diez mensajes de las últimas 24 horas del chat real de Ana, avisos con valores de
  formularios incluidos. La prueba devuelve su motivo, el texto y el prellenado. Además, el buscador
  filtra por correo aunque lo oculte, y una cuenta sin nombre aparece con su correo como nombre.
- **Evidencia:** la ruta pide sólo `whatsapp.configureassistant` (`WhatsAppAssistantAgentController.cs:19`);
  el historial no pide permisos (`WhatsAppAssistantAgentCapabilitySource.cs:51-52`); la respuesta trae
  motivo, texto y valores (`WhatsAppAssistantAgentTestModels.cs:30-32`); el lector busca por correo sin
  saber quién pregunta (`WhatsAppAssistantAgentReader.cs:23-31`). Que «Probar» use el historial real es
  una decisión del plan de casos; que alcance con «Configurar el asistente», no.
- **Arreglo:** exigir también `whatsapp.read` en el contexto de «Probar» (`WhatsAppAssistantAgentTestPolicy.cs:41`),
  que el motor ya controla; sin lectura de registros, no devolver motivo ni campos; buscar y mostrar el
  correo sólo con `users.read`. Chico.

#### H5. Si falla el motor de IA, la persona queda sin respuesta y nadie se entera

- **Dónde falla:** hoy.
- **Escenario:** la conversación está en modo Asistente y el proveedor de IA falla una vez. Quien
  preguntó «¿A qué hora abren?» no recibe nada, el chat no pasa a «Necesita una persona» y el hilo no
  muestra el fallo. El motor no reintenta, porque cada intento se paga.
- **Evidencia:** el pedido termina bloqueado sin desenlace guardado (`WhatsAppAssistantSuggestionPolicy.cs:51-59`,
  `WhatsAppAssistantRequest.cs:57-64`), y el hilo sólo muestra desenlaces (`WhatsAppChatReader.cs:88`).
  Derivar sólo ocurre sobre una operación completada (`WhatsAppAssistantReplyPolicy.cs:170,240`). Un
  aviso general de disponibilidad lo ve sólo quien puede configurar el asistente (`ChatThread.tsx:42,98`
  en el front).
- **Arreglo:** cuando un pedido automático termina bloqueado por una falla del motor, derivar con
  `WhatsAppAssistantHandoffPolicy` en la misma unidad, con un código estable nuevo. Chico a mediano.
  **Lo decidís vos:** derivar pasa la conversación a Atención manual.

#### H6. Las acciones de una respuesta del asistente salen en cualquier orden

- **Dónde falla:** hoy.
- **Escenario:** el asistente contesta con dos o tres textos. Todos se encolan con la misma hora y los
  desempata el Id, un Guid v7 que es aleatorio dentro de un mismo milisegundo: con dos acciones, el
  orden es cara o cruz.
- **Evidencia:** un solo `nowUtc` para todas (`WhatsAppAssistantReplyPolicy.cs:134,160-165`) y el orden
  por `RequestedAtUtc, Id` (`WhatsAppConversationRepository.cs:67,73`). El repo ya lo resolvió sólo para
  «Seguir sin ubicación» (`WhatsAppFlowLocationPolicy.cs:102-104`). El test del orden mira una lista
  falsa, no el despacho.
- **Arreglo:** sumar a `RequestedAtUtc` el lugar de cada acción en milisegundos, como ya hace
  `SkipRequestedAt`, con un test con PostgreSQL que despache una respuesta de tres acciones. Chico. Con
  varias copias hace falta además H23.

#### H7. El cliente de mensajes no distingue «no salió» de «no se sabe»

- **Dónde falla:** hoy, poco frecuente; más con Meta lento o con picos.
- **Escenario:** tres casos, del mismo origen.
  - Meta acepta un código pero contesta después de los 10 s: la cola de acceso lo toma como una falla
    pasajera y lo manda hasta tres veces en total (dos reenvíos), y cada plantilla se paga.
  - Al revés, cuando seguro no salió (DNS caído, conexión rechazada, circuito abierto, límite 130429),
    la salida durable lo deja incierto o fallido en lugar de reintentarlo. Con el circuito abierto,
    vacía la cola en inciertos.
  - La cola de acceso registra como no enviado lo que quedó incierto: el administrador ve la invitación
    fallida y la reenvía.
- **Evidencia:** `WhatsAppCloudClient.cs:50-63,87`, `WhatsAppSendResult.cs:35,44-45,87`,
  `WhatsAppSenderBackgroundService.cs:103-124` y `WhatsAppOutgoingBackgroundService.cs:99,144`; lo fijan
  los tests `Network_failures_and_timeouts_are_transient` y `Transient_failures_wait_the_configured_delay_before_retrying`.
  El reintento viene del [diseño del ingreso](../specs/2026-09-22-ingreso-whatsapp-design.md), anterior
  a la regla del módulo que dice que nunca se repite a ciegas un POST.
- **Arreglo:** que el cliente separe dos casos:
  - «No salió» (131056, 130429, circuito o límite, error de conexión, DNS o TLS): se reintenta con
    espera. Un 5xx con un código que Meta manda reintentar (131000, 131016) también puede ir de este lado.
  - «No se sabe» (timeout, respuesta cortada, 5xx sin código): queda incierto, y lo incierto no deja la
    invitación como fallida. Ese estado nuevo de la invitación cambia lo que lee el front, así que se
    revisa con el front.

  Chico en la cola de acceso; mediano en la salida durable, que necesita una columna de próximo intento.

#### H8. Cada despliegue corta los envíos en curso

- **Dónde falla:** hoy, en cada despliegue o reinicio.
- **Escenario:** la copia se apaga en medio de un envío.
  - Si un texto manual estaba hablando con Meta, o Meta acababa de aceptarlo, queda en «Enviando» y a
    los dos minutos pasa a incierto, aunque cabía en los 30 s de apagado.
  - Si estaba esperando su turno para escribirle a esa persona, queda incierto sin haber mandado nada.
  - En la cola de acceso, un código que esperaba su reintento queda tomado por la copia que se apagó y
    no sale nunca, y la invitación queda pendiente para el administrador.
- **Evidencia:** el token de apagado entra en la llamada (`WhatsAppOutgoingBackgroundService.cs:73`,
  `WhatsAppSenderBackgroundService.cs:54,124`); sólo avisos y asistente confirman con un plazo propio
  (`WhatsAppOutgoingBackgroundService.cs:112-114`); `DurableMessageSource.cs:49-50` y `QueuedDelivery.cs:29-35`.
- **Arreglo:** con el apagado pedido, no tomar trabajo nuevo, terminar la llamada en curso con su plazo
  y confirmar con un token acotado en todos los orígenes, como ya hacen los Flows. Chico.

#### H9. Un envío fallido no dice por qué, ni en la pantalla ni en los logs

- **Dónde falla:** hoy.
- **Escenario:** un mensaje a un número sin WhatsApp (131026) y otro frenado por el límite de Meta
  (130429) quedan igual, «No se pudo enviar», y el operador no sabe cuál repetir. Faltan además otras
  señales:
  - en los logs no queda el código de error de Meta;
  - si algo explota antes de llamar, el log sale sin el id y sin la excepción;
  - con el token vencido y sólo tráfico entrante, `/health` sigue sano mientras se pierden los archivos
    recibidos (H11);
  - el módulo no publica métricas.
- **Evidencia:** `WhatsAppOutgoingBackgroundService.cs:94-110,145` y `WhatsAppChatDispatchService.cs:90`
  (`Fail(null)`). El código de falla no llega a la respuesta (`WhatsAppChatReader.cs:100-109`,
  `WhatsAppChatResponse.cs:51-57`), y el front no usa el `errorCode` de los estados (`chat.ts:41`). El
  cliente de medios convierte un 401 en «no disponible» (`WhatsAppMediaClient.cs:28,44`). La cola de
  acceso, en cambio, sí loguea el código de error de Meta.
- **Arreglo:**
  - un código estable por tipo de rechazo, declarado en Domain y traducido, con su campo en el DTO
    (aditivo) y su texto en el front;
  - loguear el id del envío, el código de error de Meta y la excepción;
  - que todos los clientes de Meta avisen al chequeo de salud (`/health`) cuando el token falla;
  - métricas de pendientes, inciertos y fallidos con su `AddMeter`, como la IA.

  Chico a mediano.

#### H10. Una sola persona agota el cupo diario de códigos

- **Dónde falla:** hoy; más rápido con varias copias (H26).
- **Escenario:** alguien con cualquier cuenta pide desde Mi perfil códigos para números ajenos. Desde
  una sola IP llega en una hora a los 100 códigos diarios de toda la instalación, y sostenerlo le cuesta
  unos 100 pedidos por día. Con el registro abierto, lo mismo se hace sin cuenta, desde el pedido de
  código de ingreso. Mientras siga, nadie puede entrar ni verificar su número por WhatsApp; el correo y
  Google siguen andando.
- **Evidencia:** `MeWhatsAppController.cs:34-36` (sólo el límite por IP), `ProfileWhatsAppService.cs:71-82`
  (sin tope por cuenta), `WhatsAppLoginCodeController.cs:20,26-27` y `WhatsAppCodeIssuer.cs:79,90-94`
  (el pedido anónimo comparte el cupo), y `WhatsAppCodeQuotaGuard.cs:32-48`. El tope global es una
  decisión de costo, global para no revelar qué números tienen cuenta; ninguna fuente acepta su efecto
  sobre la disponibilidad.
- **Arreglo:** un tope diario por cuenta para los códigos del perfil (las filas ya guardan quién los
  pidió) y, con el registro abierto, un tope diario por IP en el pedido anónimo; además, una alerta al
  80 % del cupo. Chico.

#### H11. Lo recibido con archivos se pierde si Meta o el token fallan más de 15 minutos

- **Dónde falla:** hoy.
- **Escenario:** alguien manda un formulario con fotos mientras los medios de Meta, el token o el
  almacenamiento privado fallan 20 minutos. Después de seis intentos en unos 15 minutos, la respuesta
  queda rechazada, y un audio o una planilla quedan como no disponibles, sin copia. A la persona no le
  llega nada; el operador sólo puede reenviar el formulario.
- **Evidencia:** la escala de 30, 90, 210, 450 y 930 s (`WhatsAppFlowSessionReader.cs:21-24`,
  `WhatsAppAudioReader.cs:29-31`, `WhatsAppSpreadsheetReader.cs:20-23`) y el cierre
  (`WhatsAppMessage.cs:86-95`). Es una decisión documentada («al menos 15 minutos»,
  `docs/features/whatsapp.md:240,248`), que no evalúa una caída más larga. La respuesta cifrada sigue
  guardada, así que reprocesarla sería posible.
- **Arreglo:** alargar la ventana a horas, que es compatible con «al menos», o permitir reintentar a mano
  los rechazos por no disponible; separar 401 y 403 en el cliente de medios. Chico.

#### H12. La espera de 6 s entre mensajes a una persona frena a todos

- **Dónde falla:** hoy, como pausas. Deja de alcanzar con unos 10 mensajes extra por minuto a una misma
  persona, por copia.
- **Escenario:** el asistente le manda dos acciones a Ana, o el pedido de ubicación y «Seguir sin
  ubicación». Entre dos mensajes a una misma persona hay 6 s, y la copia espera en la cabeza de la fila
  sin atender a nadie más: 6 s de pausa para todos los chats, 12 s con tres acciones.
- **Evidencia:** `WhatsAppDispatchCoordinator.cs:18-25,76` y `WhatsAppConversationRepository.cs:62-75`.
- **Arreglo:** reservar salteando los contactos con una salida en curso o en espera, con la hora del
  último envío guardada en PostgreSQL. Chico a mediano.

#### H13. La ubicación y el texto del envío no vencen

- **Dónde falla:** hoy.
- **Escenario:** alguien manda su ubicación para un formulario. A los 90 días se borra del mensaje, pero
  queda para siempre en el envío, aunque no haya completado el formulario. Pasa lo mismo con el texto
  del operador que acompaña al formulario.
- **Evidencia:** `WhatsAppFlowSession.cs:41,124,131-141`, en columnas en claro
  (`WhatsAppFlowSessionConfiguration.cs:26-27`); de la sesión, la retención sólo limpia el contenido del
  asistente (`WhatsAppMessageRetentionRepository.cs:60-66`).
- **Arreglo:** vaciarlos al capturar o al vencer, por lotes dentro de una unidad (la sesión es auditable
  y no admite `ExecuteUpdate`), conservando el estado de la ubicación; y ampliar la regla en
  [WhatsApp](../features/whatsapp.md). Chico.

#### H14. No se puede borrar ni anonimizar a una persona

- **Dónde falla:** hoy, apenas haya datos reales.
- **Escenario:** alguien pide que borren sus datos. Su cuenta se borra lógicamente, pero su contacto
  (número, BSUID y nombre), el número en cada salida y sus mensajes quedan, sin ningún recorrido para
  borrarlos. Las FK impiden hacerlo a mano sin borrar antes lo que depende del contacto.
- **Evidencia:** `UserAccessService.cs:79-98`, `WhatsAppMessageRetentionService.cs:12-14` y
  `WhatsAppMessageRetentionRepository.cs:53-57`; test `Contacts_are_not_touched`. Ya figura como
  pendiente en [verificación y producción](../plans/verificacion-y-produccion.md), y no es sólo de WhatsApp.
- **Arreglo:** un procedimiento transversal, con un puerto donde cada módulo anonimiza lo suyo, como
  `IPhoneLinkParticipant`. Mediano.

#### H15. El respaldo no tiene plazo ni ensayo

- **Dónde falla:** hoy.
- **Escenario:** después de un mal despliegue se restaura la base a unas horas antes. Al levantarla, el
  worker les vuelve a mandar a los clientes lo que en la copia seguía pendiente. Nada fija cuántos días
  de copia hay.
- **Evidencia:** `docs/deploy/azure-setup.md:71` crea el servidor sin fijar la retención de respaldos;
  el ensayo de restauración sigue abierto en [verificación y producción](../plans/verificacion-y-produccion.md);
  `WhatsAppOutgoingBackgroundService.cs:20-28` toma cualquier pendiente (la ventana de 24 horas lo acota
  si se restaura más atrás).
- **Arreglo:** sólo documentación. Fijar el plazo de restauración a un momento dado (Azure da 7 días por
  defecto y hasta 35) y la redundancia al crear el servidor, y levantar una copia restaurada con
  `WhatsApp:ProcessOutgoingInBackground=false` hasta revisar lo pendiente.

#### H16. Los chats se guardan en claro

- **Dónde falla:** hoy.
- **Escenario:** quien llega a una copia de la base, por ejemplo un respaldo o un usuario de reportes,
  lee 90 días de chats en claro:
  - textos y transcripciones;
  - ubicaciones y filas de planillas;
  - avisos con valores de formularios que la app oculta a quien no tiene `formrecords.read`.
- **Evidencia:** columnas sin conversor (`WhatsAppMessageConfiguration.cs:32-33,36-37,50,63` y
  `WhatsAppOutgoingMessageConfiguration.cs:16,28`). En cambio, la respuesta de Flow, la sesión y el
  asistente de Inicio sí se cifran. El [ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md)
  usa justamente una copia de PostgreSQL como amenaza para cifrar las colas.
- **Arreglo:** **lo decidís vos, con un ADR.** Hay dos caminos:
  - cifrar con Data Protection al menos avisos, transcripciones y planillas; es mediano, porque los
    lectores filtran por el cuerpo en SQL;
  - dejar escrito que el chat queda en claro, protegido por los 90 días de retención y el acceso a la base.

#### H17. La paridad de textos no cubre los del asistente de WhatsApp

- **Dónde falla:** hoy, como hueco del control; las claves coinciden.
- **Escenario:** alguien agrega un motivo de rechazo sólo en español. Los tests pasan, y a quien tiene la
  cuenta en inglés le llega en español, sin ningún error.
- **Evidencia:** las partes de WhatsApp comparan `Permissions`, `Access` y `FlowTexts`
  (`ResourceParityTests.WhatsApp.cs:11-21`, `ResourceParityTests.FlowTexts.cs:6`), pero no `AssistantTexts`,
  `AssistantTerminalTexts` ni `AssistantAgentValidation`.
- **Arreglo:** un test que recorra cada `.resx` de Application con su `.en.resx`, para que uno nuevo no
  quede afuera. Trivial.

#### H18. Dos textos no respetan el idioma o la región de quien recibe

- **Dónde falla:** hoy.
- **Escenario:** dos casos.
  - La invitación le llega en inglés a una cuenta en inglés, pero al tocar el botón, el texto con el
    enlace le llega en español. Sólo pasa con cuentas en otro idioma que el de la instalación.
  - En un formulario en español, la ayuda de una pregunta con decimales dice «usá punto para los
    decimales», cuando en es-AR se escribe con coma; el formulario igual acepta la coma.
- **Evidencia:** `WhatsAppAccessReplyPolicy.cs:20`, que es el costo de una decisión documentada
  (`docs/features/whatsapp.md:22` y el [ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md),
  custodiados por `IdentityBoundaryTests`): lo que entra no lee cuentas. La ayuda decimal está en
  `FlowTexts.resx:9` y va contra la regla de números de [`AGENTS.md`](../../AGENTS.md#otras-reglas-fijas);
  el codec acepta coma (`WhatsAppFlowValueCodec.cs:16`).
- **Arreglo:** llevar el idioma en el id del botón de las invitaciones nuevas, sin leer cuentas y
  aceptando los botones ya enviados; y corregir la ayuda decimal en español. Chico.

#### H19. No queda quién cambió el modo ni una traza de punta a punta

- **Dónde falla:** la auditoría, hoy; la traza, sobre todo con varias copias.
- **Escenario:** un cliente reclama que lo atendió el asistente. Nadie puede saber quién pasó el chat a
  Asistente ni cuándo, ni unir en una traza el webhook con la respuesta.
- **Evidencia:** `WhatsAppContact.cs:76-93` y `WhatsAppChatModeService.cs:25-47`; no hay `ActivitySource`
  en `src`, y los workers no abren actividad.
- **Arreglo:** guardar quién cambió el modo y cuándo, también al derivar (no sirve `IAuditable`, porque
  cada mensaje entrante modifica el contacto), y guardar el contexto de traza en la fila para enlazarlo.
  Chico la auditoría; mediano la traza.

#### H20. «Crear» desde un resumen actúa antes de revisar el vínculo

- **Dónde falla:** hoy; severidad baja.
- **Escenario:** a Ana le roban el teléfono y el administrador desvincula su WhatsApp. Quien tiene el
  teléfono toca «Crear borrador» en el último resumen y se crea, a nombre de Ana, el borrador que ella
  había pedido. No se publica nada ni sale ningún mensaje.
- **Evidencia:** la política crea (`WhatsAppAssistantAuthoringPolicy.cs:36`) antes de que el servicio
  compare modo y vínculo (`WhatsAppAssistantAuthoringService.cs:23,30-34`); los botones de casos comparan
  antes (`WhatsAppAssistantReplyPolicy.cs:208-216`).
- **Arreglo:** comparar modo, cuenta y revisión del vínculo antes de crear, y repetirlo en la
  transacción. Chico.

#### H21. La confirmación de un toque puede perderse

- **Dónde falla:** hoy; severidad baja.
- **Escenario:** la persona toca «No la emitimos». El dueño anula y guarda, y algo falla antes de
  encolar la confirmación: un corte, o un error con otro toque de la misma vuelta. La anulación quedó
  hecha, pero la persona no recibe «FAC-000012 quedó anulada», porque la repetición se ignora.
- **Evidencia:** `WhatsAppFormActionReplyService.cs:46-74` y `FormIssueDecisionRecorder.cs:30-31`. Es
  una decisión documentada: el texto sale a lo sumo una vez (`docs/features/whatsapp.md:258`).
- **Arreglo, si se quiere:** pasarle al dueño el id del toque para que devuelva el mismo texto ante el
  mismo toque, y encolar cada confirmación apenas vuelve su dueño. Chico. **Lo decidís vos:** cambia
  una regla documentada.

#### H22. La guía para quitar WhatsApp olvida una tabla

- **Dónde falla:** hoy, en la documentación.
- **Escenario:** quien saca el módulo ve que la migración borra diez tablas. La guía dice que son
  exactamente nueve y que, si aparece otra, «algo se borró de más»; además, su exportación deja afuera
  la configuración del asistente.
- **Evidencia:** [quitar WhatsApp](../guides/quitar-whatsapp.md) no nombra `WhatsAppAssistantAgents`,
  que crea `20261112021526_AddWhatsAppAssistantAgent.cs:76-77`, y en dos lugares dice «ocho».
- **Arreglo:** sumar la tabla y unificar la cuenta. Trivial.

### Con varias copias

#### H23. Dos salidas a la misma persona se cruzan entre copias

- **Escenario:** una copia reserva un aviso con PDF y lo sube antes de pedir el turno para escribirle a
  esa persona; otra reserva el texto siguiente para la misma persona, consigue antes el turno y el texto
  llega primero. Pasa también con las acciones 2 y 3 de una respuesta mientras la persona está en su
  espera de 6 s.
- **Evidencia:** la reserva no excluye contactos con otra salida en curso (`WhatsAppConversationRepository.cs:62-75`),
  y la subida va antes del turno (`WhatsAppOutgoingBackgroundService.cs:43-72`). El test de dos hosts
  sólo prueba que no manden a la vez.
- **Arreglo:** reservar sólo la cabeza de un contacto que no tenga otra salida en curso, con un
  `NOT EXISTS`. Chico a mediano.

#### H24. Todas las copias procesan las mismas respuestas de Flow

- **Escenario:** con atraso, o con respuestas que traen archivos (hasta 90 s cada una), cada copia toma
  las mismas 50 pendientes: baja los mismos archivos de Meta e intenta la misma captura. El registro se
  guarda una sola vez, pero sumar copias no suma capacidad. Además, procesar 50 respuestas en un mismo
  contexto produce choques de concurrencia espurios que gastan intentos.
- **Evidencia:** `WhatsAppFlowSessionReader.cs:10-19` (sin reserva ni `SKIP LOCKED`),
  `WhatsAppFlowReplyBackgroundService.cs:16-29` y `WhatsAppFlowReplyService.cs:25-47,167-200`.
- **Arreglo:** reservar cada respuesta con `SKIP LOCKED` y un vencimiento, como la salida, y procesarla
  en su propio scope. Mediano.

#### H25. La versión vieja no entiende lo que escribe la nueva

- **Dónde falla:** con una sola copia, en los segundos en que conviven sus workers en cada despliegue
  (con efecto chico: una publicación que hay que repetir) y al volver a la imagen anterior, que es raro
  pero serio; con varias copias, durante todo un despliegue gradual.
- **Escenario:** mientras conviven las dos versiones:
  - una copia vieja recibe la respuesta de un formulario que usa una capacidad nueva (una condición, un
    cálculo), no entiende el formulario que guardó la versión nueva y cierra la respuesta como no
    reconocida, sin guardarla;
  - un formulario que una versión dejó listo y la otra publica queda fallido;
  - una salida manual o un aviso de un tipo nuevo sale como texto, y una del asistente queda fallida;
  - un mensaje de un tipo nuevo, como el documento recibido que se está sumando, hace fallar a la
    versión vieja al leerlo: el historial de ese chat da error;
  - una entrega de acceso que no se decodifica se borra.
- **Evidencia:** lectura estricta (`WhatsAppFlowProtector.cs:12-13`), cierre como no reconocida
  (`WhatsAppFlowReplyService.cs:93-103`), `WhatsAppFlowPublicationService.cs:30-37`,
  `WhatsAppOutgoingBackgroundService.cs:35-42`, `WhatsAppOutgoingSendGuard.cs:57,77`,
  `WhatsAppMessageConfiguration.cs:27` con `docs/guides/despliegue.md:39`, y `DurableMessageSource.cs:38-47`.
  Es diseño, no algo observado: el despliegue todavía no está activado.
- **Arreglo:** que una copia deje para otra lo que su versión no entiende (pendiente, sin cerrar la
  sesión), con un tope, y escribir cada campo o valor nuevo un despliegue después del que ya sabe
  leerlo. Chico a mediano.

#### H26. Los límites de pedidos y la salud son de cada copia

- **Escenario:** con tres copias, un operador o una IP pueden hacer el triple de pedidos por minuto, y
  el cupo de códigos (H10) se llena en un cuarto de hora. Un token rechazado deja degradada sólo la copia
  que lo vio. Los topes que cuestan dinero viven en la base y siguen valiendo.
- **Evidencia:** el limitador es en memoria (`RateLimitingExtensions.cs:50-58`, `WhatsAppApiRegistration.cs:58-67`);
  la salud también, y está documentado (`WhatsAppHealth.cs:40-44`).
- **Arreglo:** documentarlo y dimensionar los límites por copia, como ya hace la
  [guía de IA](../guides/configurar-ia.md). Un limitador compartido tendría que pasar por `RedisCache`,
  y sumaría Redis a cada pedido de código. Chico.

### A gran escala

#### H27. «No leídos» cuenta toda la historia en cada sondeo

- **Escenario:** cada 5 s, cada bandeja abierta cuenta, chat por chat, lo que el operador no leyó; en un
  chat que nunca abrió, es todo el historial entrante. Con millones de mensajes, cada sondeo recorre
  cientos de miles de filas.
- **Evidencia:** `WhatsAppChatReader.cs:33,129-134`; `WhatsAppChatPage.tsx:66` en el front.
- **Arreglo:** calcular los no leídos como `LastInboundSequence` del contacto menos el puntero del
  operador. La secuencia no tiene huecos (`WhatsAppWebhookPersistence.cs:113`), y conviene fijar esa
  condición con un test. Chico.

#### H28. La bandeja busca y ordena sin índice

- **Escenario:** cada sondeo corre cinco consultas: la página, el total y tres conteos. La página se
  ordena por el último mensaje entrante, sin índice, y la búsqueda «contiene» por nombre y número tampoco
  tiene índice. Ya hoy, un texto con dígitos («Juan 3») trae además todos los números con un 3.
- **Evidencia:** `WhatsAppChatReader.cs:24,26,30-35,116-121`; no hay `pg_trgm` en el repositorio.
- **Arreglo:**
  - un índice por `(LastInboundAtUtc, Id)`;
  - buscar dígitos sólo cuando lo escrito es un número;
  - evitar el total repetido y no recalcular los conteos en cada sondeo;
  - un índice trigram para los nombres, que sería la primera extensión de Postgres del repositorio:
    pide una migración y habilitarla en Azure.

  Mediano.

#### H29. Buscar lo pendiente recorre toda la historia

- **Escenario:** el procesador entrante da una vuelta cada 2 s mientras hay pendientes, en cada copia.
  En cada vuelta, la búsqueda de audios pendientes recorre todos los mensajes, y la del asistente revisa
  contacto por contacto. Además, al listar formularios, las sesiones sin respuesta, que vencen pero
  quedan como pendientes para siempre, crecen sin fin.
- **Evidencia:** `WhatsAppAudioReader.cs:21-33`, sin índice útil; `WhatsAppAssistantReader.cs:166-205`;
  `WhatsAppFormChannelStateReader.cs:18-29`.
- **Arreglo:**
  - un índice parcial para los audios;
  - para el asistente, un marcador de pendiente por contacto, porque un índice parcial no alcanza: lo no
    procesado queda nulo para siempre;
  - para las sesiones, un índice por definición y un estado final para las vencidas.

  Mediano.

#### H30. La retención vacía todo de una vez, en cada copia

- **Escenario:** con millones de textos vencidos, una sola sentencia intenta vaciarlos todos. Si pasa
  los 30 s, se deshace, y la próxima corrida arrastra un día más. La falla corta además la retención del
  contenido del asistente, y cada copia lanza el mismo barrido al arrancar.
- **Evidencia:** `WhatsAppMessageRetentionRepository.cs:21-57` y `WhatsAppMessageRetentionService.cs:39-49,66-81`;
  sin un plazo de consulta propio (`docs/guides/despliegue.md:74`). `docs/features/whatsapp.md:30` dice
  lo contrario: que si un vaciado falla, la próxima corrida completa el otro.
- **Arreglo:** vaciar en lotes por clave con un índice parcial, guardar una marca de la última corrida,
  que la retención del asistente corra aunque falle la otra, y corregir ese texto. Chico. Ya es el
  pendiente 4 del [panel de conversaciones](../plans/2026-10-04-panel-conversaciones-inicio.md).

#### H31. La salida manda de a uno, en una sola fila

- **Escenario:** un aviso con PDF a un rol de 300 personas sube y manda el mismo archivo 300 veces, de a
  uno, y la respuesta de un operador a otro cliente espera detrás: ya hoy son minutos. Con Meta simulado,
  cada copia saca unos 4 mensajes por segundo; con la latencia real, menos.
- **Evidencia:** `WhatsAppOutgoingBackgroundService.cs:47-66,139-147`. La fila única, sin prioridad, es
  una decisión documentada (`docs/features/whatsapp.md:268`), con su costo subestimado para PDF y roles
  grandes.
- **Arreglo:** subir el PDF una sola vez por corrida, saltear los contactos en espera (H12) y mandar
  varios mensajes a la vez por copia. **Lo decidís vos:** una prioridad para el operador cambia la
  decisión escrita. Mediano.

#### H32. Un lock global de formularios

- **Escenario:** cada envío de formulario, cada propuesta automática y cada captura esperan de a uno la
  misma traba hasta terminar de guardar. Con transacciones cortas, el techo está en unas decenas por
  segundo; un catálogo con filtros lento puede frenar a toda la instalación hasta 3 s por consulta.
- **Evidencia:** `WhatsAppFormSendPolicy.cs:74,88`, `BusinessFormRecordRecorder.cs:120` y
  `FormAdvisoryLockKeys.cs:7`. Es una decisión documentada con su deuda anotada
  (`docs/features/formularios.md:63` y [planes pendientes](../plans/README.md)).
- **Arreglo:** medir primero, como pide la deuda; después, numerar con un lock por definición y que
  envíos y propuestas tomen un lock compartido. Mediano.

#### H33. Cada formulario le pregunta a Meta por la cuenta

- **Escenario:** cada formulario enviado le pide dos veces a Meta los números de la cuenta de WhatsApp
  Business: en el pedido o la propuesta, y otra vez al despacharlo. Si esa API de Meta se pone lenta,
  «Enviar» tarda hasta 30 s y los workers se frenan; si se cae, no salen formularios aunque la de
  mensajes ande.
- **Evidencia:** `WhatsAppFormSendPolicy.cs:61-62`, `WhatsAppFormDispatchPolicy.cs:33` y
  `WhatsAppFlowEnvironment.cs:17-25`.
- **Arreglo:** sacar la consulta del pedido y de la propuesta, que no está documentada, y dejar sólo la
  del worker, que sí lo está (`docs/features/formularios.md:147`). Chico.

#### H34. Los índices se crean bloqueando la tabla

- **Escenario:** ocho migraciones crean índices sobre `WhatsAppMessages` sin `CONCURRENTLY`, y ninguna
  guía pide lo contrario. Cuando la tabla tenga millones de filas, la próxima migración que cree un
  índice así va a frenar las escrituras del webhook mientras se construye.
- **Evidencia:** por ejemplo, `20261111212806_AddWhatsAppPendingTapsIndex.cs:13-17`.
- **Arreglo:** crearlos de forma concurrente con la anotación de Npgsql, fuera de transacción, y anotarlo
  en la [guía de migraciones](../guides/migracion.md); un test de arquitectura puede vigilarlo. Chico.

### Observaciones menores

- **O1. La publicación de Flows reintenta sin espera creciente.** Con Meta caído, reintenta cada 15 s,
  sin alargar la espera y sin cortar nunca (`WhatsAppFlowOperation.cs:39`). El retiro, en cambio, espera
  5, 15, 30 y 60 minutos (`WhatsAppFlowBinding.Deprecation.cs:124-125`).

## Separar el módulo: inventario, no defecto

Si WhatsApp pasara a ser un servicio con su propia base, habría que cambiar tres cosas:

- los lectores que unen cuentas y roles del núcleo (`WhatsAppFormRecipientReader.cs:16-27`,
  `WhatsAppFlowSessionReader.cs:53`, `WhatsAppRecordOriginReader.cs:31`);
- el código de ingreso, la cola de acceso y la invitación, que guardan datos del núcleo junto con los suyos;
- la confirmación de un número, que corre dentro del guardado del núcleo.

Ninguna regla vigente lo prohíbe, y los módulos BusinessForms e IA también leen y escriben datos del
núcleo así. El [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md) descarta separar el módulo
por prematuro. La frontera de WhatsApp con BusinessForms y con IA, en cambio, está limpia: puertos con
IDs opacos y sin transacciones compartidas. Separarlo pediría un ADR nuevo.

## Lo que está bien

- **Estado por pedido.** Cada conversación conserva su contexto, su propuesta y su destino aunque el
  modelo de otra quede colgado (`WhatsAppAssistantIsolationIntegrationTests`), y lo cifrado queda atado a
  su dueño (`WhatsAppFlowSessionProtector.cs:12-27`).
- **Ediciones simultáneas.** Configuración y modo comparan la revisión bajo su lock y responden 409;
  enviar frena con 409 si queda otra salida sin resolver, y el vínculo se pone en fila por destino, con
  el único del teléfono de respaldo. Está probado con dos cuentas que confirman el mismo número a la vez.
- **Lo que entra se guarda una vez.** Seis webhooks iguales en paralelo dejan un mensaje y un contacto,
  y los estados de entrega sólo avanzan, aunque lleguen desordenados.
- **La salida durable** valida operador, cuenta, vínculo, modo y ventana al reservar; los avisos y las
  respuestas del asistente, otra vez justo antes de llamar a Meta. Nunca repite un POST incierto.
- **Los Flows** se publican y se retiran con lease, fence y consulta antes de repetir; el retiro está
  probado con dos hosts.
- **Entradas hostiles:**
  - la firma HMAC se compara en tiempo constante antes de leer, con un tope de 5 MB;
  - los medios se bajan sólo de los dominios de Meta, sin redirecciones, y se verifican el tipo real y
    el SHA-256;
  - el asistente sólo lee o propone, y lo redactado se revisa contra lo leído.
- **Fechas.** Todo se guarda en UTC con el reloj inyectado, y los cortes de día usan la zona del negocio;
  está probado con un salto de horario de verano.
- **Integridad.** FK, únicos e idempotencia están respaldados por la base, con locks compatibles con las FK.
- **Paginado.** El historial pagina con cursor y desempate, probado con mensajes de la misma hora.
- **Logs.** No llevan cuerpos, códigos de ingreso, tokens ni números completos, y está probado en nivel
  Trace.
- **Estado compartido.** La cola de acceso vive cifrada en PostgreSQL, los archivos en Azure Blob y las
  claves de Data Protection en la base: nada depende del disco de una copia.

## Controles y pruebas que conviene sumar

- **Controles automáticos, para que no vuelva:**
  - la paridad de todos los `.resx` (H17);
  - los índices concurrentes sobre tablas grandes (H34).
- **Pruebas que faltan:**
  - despachar de punta a punta, con el agente y Meta simulado, una pregunta de caso de un botón y otra
    de tres (B1 ya tiene la prueba del guard sobre lo que encola `ApplyAsync`);
  - dos operadores con un envío incierto en el mismo chat (H2);
  - buscar por correo en «Probar» con un rol sin `users.read` (H4);
  - despachar con PostgreSQL una respuesta de tres acciones (H6);
  - un toque en «Crear» después de desvincular el número (H20);
  - un corte entre la decisión del dueño y la confirmación de un toque (H21);
  - dos hosts con respuestas de Flow (H24) y con salidas a la misma persona (H23);
  - apagar Redis con mensajes en cola (H3);
  - apagar la copia en medio de un envío manual (H8);
  - la convivencia de dos versiones (H25).
- **Pruebas reales:**
  - carga del webhook y de la bandeja con millones de filas, midiendo con `EXPLAIN ANALYZE`;
  - Meta lento o caído;
  - varias copias levantadas;
  - las plantillas de código e invitación en inglés, aprobadas en la cuenta real de Meta;
  - restaurar un respaldo (H15).

## Límites de esta revisión

- **Alcance.** Es de solo lectura sobre `9c0eb048`, y no incluye los cambios sin commitear de ese día.
- **Sin ejecución.** No se corrió nada: los tiempos, los techos y las frecuencias salen del código y se
  confirman con las pruebas de la sección anterior.
- **Despliegue.** El despliegue a Azure todavía no está activado, así que lo de despliegue y varias
  copias es diseño, no algo observado.
- **Front.** Se leyó en `1b0c45e3`, sólo donde hacía falta para un criterio.

## Cómo seguir

- **Antes de la prueba integral:** B1, B2, B3 y H1 quedaron arreglados el 2026-11-12. B4 es chico y
  raro, y queda para después.
- **Tus decisiones:** H2, H5, H16, H21, la prioridad de H31 y el horizonte.
- **El resto de «Hoy»:** son arreglos chicos o medianos, y conviene juntarlos en un plan. Los de varias
  copias van antes de sumar réplicas; los de gran escala, cuando se elija ese horizonte.
- **Lo que ya estaba anotado:**
  - H14 y el ensayo de H15, en [verificación y producción](../plans/verificacion-y-produccion.md);
  - H30, en el [panel de conversaciones](../plans/2026-10-04-panel-conversaciones-inicio.md);
  - la deuda de H32, en [planes pendientes](../plans/README.md).
