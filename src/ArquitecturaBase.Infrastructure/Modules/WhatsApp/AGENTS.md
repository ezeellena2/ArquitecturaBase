# WhatsApp: Meta, workers y persistencia del canal

Adaptadores del módulo opcional. Leé [WhatsApp](../../../../docs/features/whatsapp.md) antes de tocarlo; el núcleo no nombra este módulo.

- `Persistence/`: modelo, lectores, repositorios y `WhatsAppLockKeys` ([guía](Persistence/AGENTS.md)).
- Clientes de Graph: `WhatsAppCloudClient` (mensajes), `WhatsAppFlowClient` (Flows, preview y retiro), `WhatsAppMediaClient` (descarga), `WhatsAppMediaUploadClient` (documentos) y `WhatsAppFlowEnvironment` (cuenta WABA y número).
- Webhook: firma, lectura del lote, reintento único y señal al procesador. Cola de acceso cifrada: `WhatsAppSendQueue`, codec y `WhatsAppMessageSource`. Protectores de Flows, sesiones y respuestas.
- Workers: cola de acceso (`Sender`), salida durable (`Outgoing`), procesador entrante, publicación y retiro de Flows, respuestas de Flow y retención; `WhatsAppDispatchCoordinator` da la lease por destino.
- Registro, opciones, salud y versiones `Disabled*` para el transporte apagado.

## Al modificar

- Cada worker separa reserva, efecto y confirmación en scopes nuevos, sin transacción durante el HTTP. El inicio del efecto se confirma en su propio límite justo antes de Meta; en Flows y retiro la llamada se acota a lo que queda de la lease de 2 min que fija Application. Flows, retiro, avisos y asistente confirman con un token propio de 10 s. Si se corta después de marcar, queda incierto: nunca se repite a ciegas un POST, Create, Publish ni retiro.
- La lease por destino vive en Redis por `RedisCache` (60 s, renovada cada 10, liberada con 6 s de espera) y la comparten la cola de acceso y la salida durable; no reemplaza los locks de PostgreSQL.
- El procesador entrante corta todo si el contacto quedó salteado por SKIP LOCKED. Si no, retira la interpretación superada, normaliza el audio y puede interpretar la transcripción nueva en la misma vuelta. La interpretación consulta su operación por UUID antes de Submit: sólo NotFound habilita Submit, y en RecoverOnly termina en StateConflict; el audio conserva su recorrido.
- Antes de cada Create, Upload, Publish o retiro se verifica con `phone_numbers` paginado que el número pertenezca al WABA; un mismatch no llama a Meta. Un bloqueo de salud no se elude con opciones.
- HTTP sin loggers ni reintentos de métodos no seguros, con cuerpos acotados y sin redirecciones automáticas en los clientes de mensajes, Flows y medios. El bearer sólo va a Graph y a medios en `lookaside.fbsbx.com` o `mmg.whatsapp.net` por HTTPS 443; los assets de Flows se bajan sin bearer. No se guardan URLs de medios: un documento se sube desde la copia privada verificada y se manda por su ID.
- Los diagnósticos de Flows mapean punteros reconocidos, con puntos o con corchetes, nunca el mensaje de Meta ni IDs deducidos de texto.
- El parser conserva un Audio nuevo aunque su descriptor no sirva (sólo Ogg/Opus) y no reclasifica Media anterior.
- Cada protector tiene propósito propio ligado a sus identidades; no se reutilizan ni se renombran. Los IDs de botones ya enviados no cambian.
- Logs sin cuerpos, códigos, tokens, valores ni números completos. Los tests usan Meta simulado y avanzan los workers apagados a mano; probá reintentos, carreras y privacidad con el nivel de logs de los tests.

## Referencias

- [Reglas](../../../../docs/features/whatsapp.md#reglas), [publicación](../../../../docs/features/whatsapp.md#publicación-durable-de-flows--2b-2026-10-10) y [retiro](../../../../docs/features/whatsapp.md#deprecación-durable-de-flows--2026-10-21) de Flows, [avisos](../../../../docs/features/whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6) y [ofrecimientos](../../../../docs/features/whatsapp.md#ofrecimientos-automáticos--etapa-5).
- [Configuración local](../../../../docs/guides/whatsapp-en-local.md) y [quitar el módulo](../../../../docs/guides/quitar-whatsapp.md).
- [Tests del módulo](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp/AGENTS.md).
