# Inteligencia artificial: pruebas

Coordinación con puertos doblados y FakeUnitOfWork. Probar límite/rollback/revisión, deduplicación, cambios de proveedor y catálogo/prueba sin guardar. Catálogo acepta candidata sin modelo y rechaza revisión vieja/clave de otro proveedor antes de llamar al puerto. Recursos bilingües incluidos en la paridad.

Hereda las instrucciones del proyecto. Su fuente funcional es [inteligencia artificial](../../../../docs/features/inteligencia-artificial.md); operación y seguridad, [configurar IA](../../../../docs/guides/configurar-ia.md); independencia, [quitar IA](../../../../docs/guides/quitar-ia.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.

El asistente prueba validación antes de UoW (longitud UTF-16 y Unicode), herramientas desconocidas
o argumentos extras sin consultar fuentes, permisos revocados, evidencia ajena, contrato terminal
estricto y deadline con FakeTimeProvider. Dobles con datos ficticios; no HTTP facturable. Las carreras,
cuotas compartidas, scopes, cifrado, rollback y retención reales pertenecen a integración con Docker.
ContextPolicy verifica es/en con cultura ambiente distinta de la capturada y corta historia
anterior a un hueco vencido sin descifrarla. Dispatch comprueba consumo conocido ante bloqueo
de publicación y la reserva conservadora de permisos. Retention verifica cuenta eliminada,
cancelación durable y purga física posterior a la ventana; el FK independiente se prueba en Docker.

ConversationTitleTests comprueba grafemas Unicode, normalización, contexto original de cifrado
y ausencia de descifrado ante bloqueo/vencimiento con FakeTimeProvider. ToString no expone títulos.

C0 conserva paridad de las cinco consultas de Inicio y prueba catálogo exacto, identidades sin herencia, perfiles/instrucciones/capacidades/terminales sellados y presupuestos por capacidad. Los casos de observación usan lecturas tipadas previas, rechazan otro turno y el mismo batch y recorren Advance→RecordProgress→Complete→codec nuevo con referencias ordinales protegidas. Inicio tampoco puede cancelar/retirar canales ajenos. La propuesta de Inicio sale en su DTO HTTP con el contenido del dueño; ninguna lectura ni snapshot. HomeProposal recorre assistant_answer@2 con y sin propuesta, la propuesta no observada y el dueño quitado; HistoryPolicy, que sólo la última propuesta viaja con su contenido.

AudioOperations/Execution prueban el mismo Submit/Prepare/Dispatch/Complete/Get con perfil STT separado, actor Read sin Send/Home, cifrado y resultado técnico; modelo textual no calificado no bloquea la normalización. Rechazos antes de efecto y cierre tardío preservan consumo sin contenido.

ChannelQuota verifica Inicio y canales separados, interpretación/audio independientes, replay antes de cuota y persistencia del cupo sin contenido; la capacidad global y el límite de instalación conservan sus pruebas.

Dispatch y AudioCompletionSignal comprueban el aviso neutral con ChannelId ficticio opaco después del commit, incluidos cierres terminales por guards. Un rollback o Result fallido no avisa; un cierre conocido puede reiterar el aviso coalescente y los turnos activos conservan la recuperación periódica.

ConversationReservation comprueba Manual con dos ámbitos del mismo actor, con/sin Subject, exclusión del mismo ámbito, Get/Cancel sellados, replay antes de cuota y cuota compartida entre ámbitos. El fallback null conserva la reserva previa; NormalizeAudio mantiene sus controles existentes.

ProposalLookup recorre un turno real de Inicio hasta el cierre con assistant_answer@2: encuentra la propuesta con la hora del turno como procedencia, y la de la conversación propia de un canal (modo Asistente con Actor igual a Subject) con el instante sellado y su reemplazo; otro Id, un turno de canal que no es la conversación propia (sin Subject, Manual u otro Subject) o uno vencido no se encuentran, y sin el instante sellado da ContentUnavailable; sin el permiso de Inicio falla antes de leerla. Sólo una propuesta posterior de la misma capacidad, o de su grupo, la reemplaza: una respuesta o un turno sin terminar, no.

HomeAudio (IA4) prueba la detección por bytes, la clave privada, el guardado único, la retención, el micrófono del bootstrap, el servicio (rechazo antes de guardar, consulta primero y falla con nuevo intento) y la aceptación del turno dictado: la transcripción de la normalización propia y los rechazos sin guardar. ContextPolicy combina lo escrito con lo dicho en el contexto, el hilo y el título.

OperationContinuation recorre la continuación de una operación de canal: el turno en la conversación anterior, el que se suma al final después de un turno cancelado, la espera con una operación activa (ActiveTurn), el replay y el mismo UUID sin la referencia, los rechazos sin guardar ni descifrar (desconocida, otro canal u otra conversación del canal, sin terminar, vencida y reserva perdida) y la validación. ContextPolicy, que el turno continuado manda el pedido y la propuesta anteriores, y una operación nueva, sin historia.

Attachment (planillas) prueba la validación (el mismo archivo), el replay, la herencia, el reemplazo y la que no se descifra, el vencimiento (la propia, la copia de Inicio que toma el del turno nuevo y la que vence sola), la respuesta sin texto de un canal, la herencia leída después del turno activo y la cuota, `RequiresAttachment` en Inicio, en un canal y heredada, lo preparado en el pedido de cada capacidad y qué turnos piden lo sellado; ContextPolicy, el bloque al final del mensaje actual, fuera de la historia y dentro del presupuesto; ProposalLookup, el grupo de reemplazo sin pedir lo sellado de los turnos posteriores, y el descriptor de la planilla del turno de la propuesta, que pide sólo lo de ese turno (ilegible, ContentUnavailable). `FictionalAttachment` arma la planilla preparada en un solo lugar: el motor no depende de su forma.

HomeAttachment (planillas en Inicio) prueba la clave, el nombre, `attachments` del bootstrap por persona (con permiso, sin él y sin almacenamiento), el guardado único y la retención; el servicio sin permiso o sin almacenamiento, quien no puede adjuntar, cada rechazo con su código en `file` (un documento, preparado o rechazado, como ilegible, y sólo la respuesta de planillas del dueño), la copia que no se relee, los campos antes de leer, el reintento igual, distinto, superpuesto, sin copia o ilegible y el que sigue a una aceptación fallida, la copia ajena, audio más planilla y la validación antes de guardar; el título con la segunda consulta, la historia sin texto y `attachment` sólo en el turno propio, también en la página.
