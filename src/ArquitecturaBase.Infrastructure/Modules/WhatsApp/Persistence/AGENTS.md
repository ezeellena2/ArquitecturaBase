# Persistence: modelo y consultas de WhatsApp

Configuraciones EF, repositorios, lectores y claves de lock del módulo, sobre `Set<T>()` del contexto único. Se registran siempre, también con el transporte apagado: retención, estado de invitaciones y estado neutral de formularios los usan. Rigen además las guías de [persistencia del núcleo](../../../Persistence/AGENTS.md).

- **Contactos:** `WhatsAppContacts`; `WhatsAppContactRepository`, `WhatsAppFormRecipientReader` (cuenta, idioma y huella de roles del destinatario) y `WhatsAppContactReader` (el chat vinculado a una cuenta y si un número ya tiene chat, para el perfil).
- **Historial:** mensajes, receipts y lecturas por operador; `WhatsAppMessageRepository`, `WhatsAppMessageReader`, `WhatsAppChatReader` (bandeja, no leídos, previews e hilo) y `WhatsAppMessageRetentionRepository`.
- **Salida durable:** `WhatsAppOutgoingMessages` de chat manual, asistente y avisos de reglas (`Origin`); `WhatsAppConversationRepository`, `WhatsAppFormActionRepository` y `WhatsAppFormActionReader` (también los toques en sus botones).
- **Flows:** operaciones, bindings por versión y cultura (publicación y retiro) y sesiones de envío; `WhatsAppFlowRepository/Reader`, `WhatsAppFlowSession*`, `WhatsAppFormChannelStateReader` (puerto `IStructuredFormChannelStateSource`) y `WhatsAppRecordOriginReader`.
- **Asistente y audio:** `WhatsAppAssistantRequests` y las columnas de audio del mensaje; `WhatsAppAssistantRepository/Reader` y `WhatsAppAudioRepository/Reader`.
- **Planillas:** las columnas `Spreadsheet*` del mensaje, con el índice filtrado de las pendientes; `WhatsAppSpreadsheetRepository/Reader`. El lector aplica la espera escalonada del audio antes de `Take`, y la retención las vacía con el cuerpo.
- **Documentos recibidos:** columnas `Document*`, índices filtrados de los que esperan y del pedido; `WhatsAppReceivedDocumentRepository/Reader`.
- **Casos del asistente:** `WhatsAppAssistantAgents` (una fila principal, índice único filtrado por `IsMain`, casos y textos en JSONB) con `WhatsAppAssistantAgentRepository` (lock `whatsapp-assistant-agent:main`) y `WhatsAppAssistantAgentReader` (también las cuentas de Probar); `WhatsAppAssistantHistoryReader` lee el historial de una sola conversación, acotado en SQL, cuenta las respuestas del día y el inicio de una ráfaga. La marca de derivar vive en el contacto (`Handoff*`, índice filtrado), el porqué en `AssistantReplyJson` de salidas y mensajes y en `OutcomeJson` de la referencia, y las otras acciones de una respuesta son referencias con `OperationRequestId` y `ActionOrdinal`, únicas con contacto, mensaje y tipo.

## Al modificar

- `AsNoTracking` sólo en lectores. Los locks de fila usan `FromSql` sin componer y exigen la transacción; como EF devuelve la instancia ya seguida sin refrescarla, se recarga después del lock donde el scope pudo leerla antes. Las tablas fijan su nombre con `ToTable`: el contexto no tiene DbSet del módulo.
- Orden: claves de contacto (BSUID y wa_id) y después las de mensaje, en otra llamada; la fila del contacto antes que la salida, el mensaje de audio o la referencia del asistente. Procesar un contacto usa SKIP LOCKED, desvincular NOWAIT y cambiar el número FOR NO KEY UPDATE ordenado por Id. El texto de `WhatsAppLockKeys` no cambia.
- Sin FK ni joins hacia BusinessForms: sus IDs son opacos.
- `WaMessageId` único es la última defensa contra duplicados. Los pendientes entrantes, las respuestas de Flow pendientes y los toques sin atender tienen índices filtrados; consultalos por esos predicados.
- Retención: sólo `ClearExpiredBodiesAsync` usa `ExecuteUpdate`, idempotente y en autocommit (el mensaje no es auditable ni de borrado lógico). Vacía texto, botones, respuesta protegida procesada y descriptor, contexto y copia del audio aunque `Body` sea null; no toca salidas Pending ni Sending. El contenido privado del asistente se limpia con entidades seguidas en lotes, dentro de la UoW del servicio, sin tocar el payload técnico.
- El chat proyecta sólo metadata de la sesión y el blob protegido de la explicación; los previews de acceso, avisos, asistente y audio salen genéricos desde SQL. Application aplica permiso y vencimiento.

### Salida y avisos

- Únicos: chat manual por contacto/operador/RequestId, asistente por contacto/RequestId y avisos por run (guardado en `RequestId`) y destinatario. `GetNextPendingAsync` toma el contacto con SKIP LOCKED (H12, H31) y después su salida más vieja, el mismo orden que marcar y completar.
- Los avisos toman el lock run/destinatario y el contacto antes de la salida; el token de reserva deja afuera a un worker viejo. La revisión humana toma el lock run/destinatario y la fila de salida, sin tomar después el contacto: no pisa a un worker ni invierte contacto→salida.
- `WhatsAppFormActionReader` une salidas FormAction y receipts por wamid con LEFT JOIN, filtra los pares pedidos exactos y usa las columnas legadas sólo como fecha del último estado conocido. Los receipts son únicos por wamid aunque lleguen antes de la salida.

### Flows

- Operación única por operación neutral y por versión; binding único por versión/cultura y por `MetaFlowId`; fences como tokens de concurrencia. El retiro tiene lease, fence, estado y contador de fallas propios; el contador se satura en cuatro y agenda 5/15/30/60 minutos.
- `WhatsAppFlowSessionQuery` es el único predicado de trabajo abierto: sesión Pending vigente, o respuesta entrante pendiente de esa sesión o, sin FK todavía, del mismo contacto dentro de su ventana. Lo comparten el retiro y el estado neutral, con el índice `VersionId+ExpiresAtUtc` de sesiones Pending.
- El retiro sólo toma bindings Published con `MetaFlowId`, aunque otra cultura haya fallado. El estado neutral no descifra ni consulta Meta; NeedsAttention prima sobre Uncertain, y sólo todos Deprecated habilitan la fecha. Pending también describe formularios activos y no acredita que el dueño permita retirarlos.
- Las respuestas pendientes aplican la espera escalonada sobre el mensaje antes de `Take`. `FindReplySessionAsync` proyecta el `OperatorUserId` real y la vigencia del vínculo, sin inferir el origen.
- El origen de una respuesta sale por lote de sesión, salida y contacto, con número enmascarado, enlace al chat sólo si el servicio lo autoriza y emisor tomado del `Origin` sellado de la sesión.
- Sesiones: `TokenHash` único, CHECK Assistant sin operador y defaults de `Origin` y contenido para el intervalo bundle→imagen.

### Asistente y audio

- Referencia única por RequestId, por contacto/mensaje fuente/tipo y por contacto/registro capturado; se bloquea después del contacto y se recarga. Las proyecciones por request y por salida conservan `QuestionOutgoingId`; la salida de una referencia se lee por request, compartida entre operadores, sin atribuir el operador del lector.
- La fuente se elige desde el último recibo de negocio antes de evaluar si sirve: texto, audio Completed con `Body` vigente, FlowReply Completed o planilla Stored con su copia y lo preparado (IA, Etapa 2). Un audio, o una planilla, pendiente o vencido bloquea lo anterior. Su vencimiento es el menor entre retención y contenido del audio.
- La fuente no queda sola: el lector suma el mensaje de negocio anterior y si entre los dos se pidió una respuesta `Assistant` no fallida (por `RequestedAtUtc`), y `WhatsAppAssistantSourcePolicy` decide el emparejado con la planilla ([regla](../../../../../docs/features/whatsapp.md#planilla-recibida--ia-etapa-2-2026-11-10)). Trae la planilla del pedido y el vencimiento menor de los dos; `FindSourceAsync` resuelve igual.
- Una captura usa como fuente su primer recibo confirmado mientras el último confirme el mismo registro; repetirla no reactiva la fuente ni extiende su retención. El puntero Manual acepta un recibo equivalente del mismo registro y el escaneo de capturas corre en ambos modos. El escaneo automático exige `AssistantProcessedAtUtc` nulo y toma el último botón válido de una pregunta pendiente; las fuentes proyectan ese marcador sin excluirlo, para recuperar referencias existentes.
- Las ofertas recientes y los rechazos son consultas separadas con límites en SQL; una salida fallida no cuenta y metadata de botones corrupta falla entera. El cupo diario cuenta por referencia automática: un Flow por la salida de la referencia y una pregunta por la suya, sin duplicarlos; el resumen de armar formularios (botones en la salida propia de la referencia) no es una oferta. `FindLatestAuthoringTapAsync` toma el último entrante sólo si es un toque `FORM_AUTHORING:` sin atender; `FindLatestAuthoringRequestAsync`, la operación del último resumen, y `FindChangeRequestAsync`, el `ChangeRequestId` de la última referencia automática ([cuándo se fija](../../../../../docs/features/whatsapp-servicios.md#asistente)). El rechazo de una planilla no cuenta ahí ni en `ExistsUnresolvedOfferAsync`. El escaneo agenda también el contacto con un toque así.
- Audio: el lector incluye mensajes sin copia privada y aplica la espera escalonada antes de proyectar y de `Take`; las referencias privadas se consultan por lote.
- `ExistsPendingAsync` sólo observa si queda una referencia vigente de asistente, un archivo (audio, planilla o documento) cuyo reintento toca en 30 s (H11) o una fuente que espera su turno (la ráfaga de un agente, con el mismo predicado del escaneo). No reserva ni produce efectos, y la selección conserva íntegra la espera escalonada.
- El toque de un botón de caso (`CASE_BUTTON:`) es una fuente con su título y `FindLatestCaseTapAsync` lo toma si es el último entrante sin atender; la retención vacía el porqué con el cuerpo y el `OutcomeJson` de las referencias vencidas.

## Referencias

- [Reglas y retención](../../../../../docs/features/whatsapp.md#reglas), [retiro de Flows](../../../../../docs/features/whatsapp.md#deprecación-durable-de-flows--2026-10-21), [audio](../../../../../docs/features/whatsapp.md#audio-recibido--etapa-5-h6) y [ofrecimientos](../../../../../docs/features/whatsapp.md#ofrecimientos-automáticos--etapa-5).
- [Locks](../../../../../docs/architecture/backend.md#locks) y [nombres de lectores y repositorios](../../../../../docs/architecture/backend.md#nombres-de-repositorios-y-lectores).
- Pruebas, en los [tests del módulo](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/AGENTS.md): orden de locks (`WhatsAppLockOrderTests`), retención (`WhatsAppMessageRetentionTests`), fuentes del asistente (`WhatsAppAssistantSourceQueryTests`) y estado del retiro (`WhatsAppFlowDeprecationQueryTests`).
