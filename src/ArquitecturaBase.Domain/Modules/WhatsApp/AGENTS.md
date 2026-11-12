# WhatsApp: dominio del módulo

Define contactos, mensajes y errores del módulo. Es independiente de Meta, HTTP, EF y las implementaciones del núcleo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Nuevos contactos arrancan Manual. Purpose distingue Business/Access/Unclassified; solo Access lleva AccessEventKind. No inferir propósito por el cuerpo ni reclasificar historia por frases.

- Chat usa modo/revisión del contacto, secuencia entrante, lectura por operador, salida Pending/Sending/Accepted/Failed/Uncertain/Reviewed y receipts tempranos. Reviewed habilita atención sin afirmar entrega ni reenviar.

- Las reglas puras y transiciones se prueban en el dominio del módulo.
- Conservá consentimiento, identidad de mensajes y los estados que permiten idempotencia y entrega.
- Una dependencia del núcleo puede ser compartida; el núcleo no depende de estas entidades.

## Verificación y ejemplos

- [Pruebas del dominio](../../../../tests/ArquitecturaBase.Domain.UnitTests/Modules/WhatsApp).
- [Cómo quitar el módulo](../../../../docs/guides/quitar-whatsapp.md).

FlowErrors declara errores puros de representabilidad/codec del canal. No contiene JSON del proveedor ni referencias a BusinessForms; también declara errores seguros de publicación. Sus códigos se traducen en Errors.resx/en.

WhatsAppFlowOperation/Binding modelan estados/fases/lease/fence del módulo, sin importar BusinessForms. Begin reserva el paso; MarkEffectStarted confirma el efecto durable inmediatamente antes del proveedor. Sólo una falla que demuestra que no se envió puede limpiar InFlight y reintentar. IDs confirmados sobreviven fallos, fence tardío no completa y Published requiere verificación. Incertidumbre parcial tiene prioridad sobre fallo; revocación no transforma un efecto desconocido en certeza.

LinkRevision detecta cambios efectivos de vínculo, incluido unlink/relink a la misma cuenta. No incrementarla por replay o entrantes; el consumidor compara contexto además de UserId.

Una respuesta Flow completa sus estados de sesión y mensaje; el mensaje conserva su propio resultado para que una repetición alterada no herede el registro de la respuesta original. Pending/Completed/Rejected/Unrecognized son estados internos, sin cuerpos ni valores externos.

G6: las salidas FormAction fijan RunId como RequestId, destinatario, contacto, número y revisión del vínculo, sin operador. Reserva y comienzo del POST de mensaje son fases distintas con token propio: vencer durante upload vuelve a Pending y conserva el mediaId; después del comienzo queda Uncertain. Sólo Failed confirmado admite retry; el retry descarta un mediaId rechazado y Accepted jamás se repite. El historial conserva Origin para aplicar privacidad de valores, incluso después de recargar. Un aviso con opciones (`WhatsAppFormActionChoice`, hasta tres, sin documento) fija al crearse sus botones (`QuickReplies`) con el id `FORM_ACTION:<salida>:<opción>`: un reintento o un reenvío manda los mismos, y un toque se lee de vuelta por ese id.

Un FormAction incierto puede recibir revisión humana con decisión, actor y fecha: salió queda Reviewed, no salió queda Failed reintentable. Ninguna revisión inventa un wamid ni un receipt; una aceptación tardía puede confirmar también el fallo revisado, sin ampliar transiciones de fallos manuales.
Review y Retry comparten LastFormActionControlRevision: un control igual o anterior no actúa sobre otro intento del mismo run.

WhatsAppDeliveryReceipt acumula DeliveredAtUtc y ReadAtUtc reales antes del fence de último estado, incluyendo eventos atrasados. Preserva primero la fecha conocida de una fila legada sin columnas pobladas; nunca deduce entrega desde lectura ni desde una revisión humana.

Etapa5: los modos vigentes son Manual y Assistant; Manual sigue siendo el default. ModeRevision sólo avanza al cambiar. La migración de datos convierte Bot previo a Manual, sin activar el asistente ni reconstruir la historia. ConfigureAssistant es el permiso nuevo del catálogo; Send permite tomar Manual.

Las salidas Assistant fijan destinatario, número, vínculo y ModeRevision sin operador; Text, Flow y QuickReplies comparten la cola. Las respuestas rápidas conservan IDs y títulos cerrados al encolar. Assistant y FormAction comparten las transiciones de reserva/POST con tokens propios y origen obligatorio: antes del POST vencido vuelve a Pending, después queda Uncertain. No hay retry automático de Assistant incierto.

H6: Audio es un tipo nuevo sólo para entrantes nuevos autenticados; Media histórico conserva su tipo. El mensaje fija descriptor, RequestId y actor una sola vez, guarda texto sólo al completar NormalizeAudio y conserva estados inciertos terminales. La adquisición recuperable satura cinco fallas pero sólo termina al cumplir también quince minutos. El contenido vence desde la recepción del mensaje. StoreAudio exitoso, incluso al recuperar la misma copia, limpia contador, primera falla y código de adquisición; un conflicto conserva esa ventana.

Las referencias Assistant conservan decisiones compartidas y la correlación con fuente/salida, sin texto ni herramientas. FlowSession fija Origin explícito y permite operador null sólo en Assistant. El contenido de explicación y prellenado se sella aparte y puede limpiarse al vencer sin alterar token, catálogos ni captura. QuickReplies conserva por opción la revisión de definición capturada; no se sustituye por la revisión actual. IA3-A: una interpretación automática puede continuar una operación anterior (`PreviousRequestId`, una vez y en Pending) y la respuesta a «Cambiar algo» nombra la operación que cambia el mensaje siguiente (`ChangeRequestId`); la pregunta sobre qué armar se nombra a sí misma (`AwaitAnswer`), así que la respuesta continúa su operación, y una interpretación que un mensaje nuevo reemplaza antes de contestar (`Replace`) deja como `ChangeRequestId` lo que continuaba.

Una referencia posterior a captura conserva SourceRecordId, configuración y cultura capturada sin copiar valores. Always permite ProposalId null real y, sólo en Manual previo al humano, Actor/Context ausentes; el envío sella al confirmador real. La identidad de registro es única por contacto y evita otra oferta por un segundo recibo de la misma captura.

IA, Etapa 2: `Spreadsheet` es un tipo sólo de entrada (`Document` es saliente). El mensaje fija una vez el descriptor de Meta y queda Pending, o No disponible con su nombre si no sirve; después guarda una vez la copia y lo preparado, el motivo de un rechazo (cierto sólo la primera vez, la que se contesta) o No disponible. Las fallas de descarga saturan como las del audio. El nombre se normaliza una sola vez (`SpreadsheetName`: sin carpetas ni caracteres de control, hasta 120 con «…» y uno por el tipo si no hay), y lo usan igual el lector del dueño y la operación del asistente. Una referencia nombra una vez, mientras está pendiente, el mensaje de planilla que lleva su pedido (`CarryAttachment`), nunca después de una captura, ni una captura después de nombrarlo (`SetCapturedSuggestion`, además del CHECK `CK_WhatsAppAssistantRequests_Attachment`).

La deprecación de cada binding tiene estado, lease, fence, marcador de efecto y fechas propios; no cambia State/Step/Fence ni los sellos de publicación. Reservar no afirma un POST: MarkDeprecationStarted fija el efecto antes del proveedor. Un lease vencido con efecto iniciado exige consulta; sólo identidad correcta y PUBLISHED observado permiten otro intento posterior, y DEPRECATED confirmado es terminal.

W1-A: `WhatsAppFlowSession` pide la ubicación antes del Flow una vez, con el cuerpo del Flow (`RequestLocation`); la primera que llega mientras espera la completa (`ReceiveLocation`) y `SkipLocation` sólo vale si es opcional. `Location` (entrante, con su punto) y `LocationRequest` (saliente) son tipos nuevos; `WhatsAppFlowLocationReply` es el id del botón «Seguir sin ubicación», con la sesión en formato D.

Casos del asistente: `WhatsAppAssistantAgent` es la fila principal del agente (perfil, casos con su menú cerrado de acciones, «Cualquier otra cosa», límites y acciones permitidas, idioma original y revisión); `Update` normaliza y valida todo junto con `WhatsAppAssistantAgentLimits` y sin cambios no avanza la revisión. `WhatsAppContact.HandOff` pasa a Manual y marca «Necesita una persona» con código ASCII y motivo; la marca se va con `ClearHandOff` o al volver a Asistente. `CreateAssistantHandoff` es la salida del mensaje al derivar, ligada a la revisión de modo Manual. Una salida o un mensaje del asistente guardan una vez su porqué (`AssistantReplyJson`, 8 KiB); una referencia guarda lo que terminó sin mensaje (`OutcomeJson`, 4 KiB) y `CreateAction` crea la segunda y la tercera acción de una respuesta, con la operación de la primera.
