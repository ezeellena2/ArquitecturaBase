# Persistence: modelo y consultas de WhatsApp

Configuraciones EF, repositorios, lectores y claves de lock del módulo, sobre `Set<T>()` del contexto único. Se registran siempre, también con el transporte apagado: retención, estado de invitaciones y estado neutral de formularios los usan. Rigen además las guías de [persistencia del núcleo](../../../Persistence/AGENTS.md).

- **Contactos:** `WhatsAppContacts`; `WhatsAppContactRepository` y `WhatsAppFormRecipientReader` (cuenta, idioma y huella de roles del destinatario).
- **Historial:** mensajes, receipts y lecturas por operador; `WhatsAppMessageRepository`, `WhatsAppMessageReader`, `WhatsAppChatReader` (bandeja, no leídos, previews e hilo) y `WhatsAppMessageRetentionRepository`.
- **Salida durable:** `WhatsAppOutgoingMessages` de chat manual, asistente y avisos de reglas (`Origin`); `WhatsAppConversationRepository`, `WhatsAppFormActionRepository` y `WhatsAppFormActionReader`.
- **Flows:** operaciones, bindings por versión y cultura (publicación y retiro) y sesiones de envío; `WhatsAppFlowRepository/Reader`, `WhatsAppFlowSession*`, `WhatsAppFormChannelStateReader` (puerto `IStructuredFormChannelStateSource`) y `WhatsAppRecordOriginReader`.
- **Asistente y audio:** `WhatsAppAssistantRequests` y las columnas de audio del mensaje; `WhatsAppAssistantRepository/Reader` y `WhatsAppAudioRepository/Reader`.

## Al modificar

- `AsNoTracking` sólo en lectores. Los locks de fila usan `FromSql` sin componer y exigen la transacción; como EF devuelve la instancia ya seguida sin refrescarla, se recarga después del lock donde el scope pudo leerla antes. Las tablas fijan su nombre con `ToTable`: el contexto no tiene DbSet del módulo.
- Orden: claves de contacto (BSUID y wa_id) y después las de mensaje, en otra llamada; la fila del contacto antes que la salida, el mensaje de audio o la referencia del asistente. Procesar un contacto usa SKIP LOCKED, desvincular NOWAIT y cambiar el número FOR NO KEY UPDATE ordenado por Id. El texto de `WhatsAppLockKeys` no cambia.
- Sin FK ni joins hacia BusinessForms: sus IDs son opacos.
- `WaMessageId` único es la última defensa contra duplicados. Los pendientes entrantes y las respuestas de Flow pendientes tienen índices filtrados; consultalos por esos predicados.
- Retención: sólo `ClearExpiredBodiesAsync` usa `ExecuteUpdate`, idempotente y en autocommit (el mensaje no es auditable ni de borrado lógico). Vacía texto, botones, respuesta protegida procesada y descriptor, contexto y copia del audio aunque `Body` sea null; no toca salidas Pending ni Sending. El contenido privado del asistente se limpia con entidades seguidas en lotes, dentro de la UoW del servicio, sin tocar el payload técnico.
- El chat proyecta sólo metadata de la sesión y el blob protegido de la explicación; los previews de acceso, avisos, asistente y audio salen genéricos desde SQL. Application aplica permiso y vencimiento.

### Salida y avisos

- Únicos: chat manual por contacto/operador/RequestId, asistente por contacto/RequestId y avisos por run (guardado en `RequestId`) y destinatario. `GetNextPendingAsync` toma el contacto con SKIP LOCKED y después la salida, el mismo orden que marcar y completar.
- Los avisos toman el lock run/destinatario y el contacto antes de la salida; el token de reserva deja afuera a un worker viejo. La revisión humana toma el lock run/destinatario y la fila de salida, sin tomar después el contacto: no pisa a un worker ni invierte contacto→salida.
- `WhatsAppFormActionReader` une salidas FormAction y receipts por wamid con LEFT JOIN, filtra los pares pedidos exactos y usa las columnas legadas sólo como fecha del último estado conocido. Los receipts son únicos por wamid aunque lleguen antes de la salida.

### Flows

- Operación única por operación neutral y por versión; binding único por versión/cultura y por `MetaFlowId`; fences como tokens de concurrencia. El retiro tiene lease, fence y estado propios.
- `WhatsAppFlowSessionQuery` es el único predicado de trabajo abierto: sesión Pending vigente, o respuesta entrante pendiente de esa sesión o, sin FK todavía, del mismo contacto dentro de su ventana. Lo comparten el retiro y el estado neutral, con el índice `VersionId+ExpiresAtUtc` de sesiones Pending.
- El retiro sólo toma bindings Published con `MetaFlowId`, aunque otra cultura haya fallado. El estado neutral no descifra ni consulta Meta.
- Las respuestas pendientes aplican la espera escalonada sobre el mensaje antes de `Take`. `FindReplySessionAsync` proyecta el `OperatorUserId` real y la vigencia del vínculo, sin inferir el origen.
- El origen de una respuesta sale por lote de sesión, salida y contacto, con número enmascarado, enlace al chat sólo si el servicio lo autoriza y emisor tomado del `Origin` sellado de la sesión.
- Sesiones: `TokenHash` único, CHECK Assistant sin operador y defaults de `Origin` y contenido para el intervalo bundle→imagen.

### Asistente y audio

- Referencia única por RequestId, por contacto/mensaje fuente/tipo y por contacto/registro capturado; se bloquea después del contacto y se recarga. Las proyecciones por request y por salida conservan `QuestionOutgoingId`; la salida de una referencia se lee por request, compartida entre operadores, sin atribuir el operador del lector.
- La fuente se elige desde el último recibo de negocio antes de evaluar si sirve: texto, audio Completed con `Body` vigente o FlowReply Completed. Un audio pendiente o vencido bloquea lo anterior; no se busca otro mensaje hacia atrás. Su vencimiento es el menor entre retención y contenido del audio.
- Una captura usa como fuente su primer recibo confirmado mientras el último confirme el mismo registro; repetirla no reactiva la fuente ni extiende su retención. El puntero Manual acepta un recibo equivalente del mismo registro y el escaneo de capturas corre en ambos modos. El escaneo automático exige `AssistantProcessedAtUtc` nulo y toma el último botón válido de una pregunta pendiente; las fuentes proyectan ese marcador sin excluirlo, para recuperar referencias existentes.
- Las ofertas recientes y los rechazos son consultas separadas con límites en SQL; una salida fallida no cuenta y metadata de botones corrupta falla entera. El cupo diario cuenta por referencia automática, sin duplicar pregunta y Flow.
- Audio: el lector incluye mensajes sin copia privada y aplica la espera escalonada antes de proyectar y de `Take`; las referencias privadas se consultan por lote.

## Referencias

- [Reglas y retención](../../../../../docs/features/whatsapp.md#reglas), [retiro de Flows](../../../../../docs/features/whatsapp.md#deprecación-durable-de-flows--2026-10-21), [audio](../../../../../docs/features/whatsapp.md#audio-recibido--etapa-5-h6) y [ofrecimientos](../../../../../docs/features/whatsapp.md#ofrecimientos-automáticos--etapa-5).
- [Locks](../../../../../docs/architecture/backend.md#locks) y [nombres de lectores y repositorios](../../../../../docs/architecture/backend.md#nombres-de-repositorios-y-lectores).
- Pruebas: [orden de locks](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/WhatsAppLockOrderTests.cs), [retención](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/WhatsAppMessageRetentionTests.cs), [fuentes del asistente](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/WhatsAppAssistantSourceQueryTests.cs) y [estado del retiro](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/WhatsAppFlowDeprecationQueryTests.cs).
