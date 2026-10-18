# WhatsApp: proveedores, workers y persistencia

Implementa Meta, firma, parsing del webhook, cola de acceso transaccional cifrada, workers, historial de entregas y persistencia del módulo. El registro reúne sus adaptadores.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Validá firma e idempotencia antes de aplicar efectos; no registrar cuerpos sensibles, números completos ni logs de HttpClient con secretos.
- `WhatsAppSendQueue` persiste códigos, invitaciones y orientación en la transacción del llamador; sólo el consumidor durable llama a Meta después del commit. El chat manual usa una salida durable separada; no repetir automáticamente Sending/Uncertain. Ambos workers comparten lease Redis por destino, detrás de RedisCache. Los scopes de reservar, llamar a Meta y completar son separados; la llamada externa no mantiene una transacción abierta. Los receipts preservan estados que llegan antes del registro del saliente.
- Las consultas EF de negocio usan lectores o repositorios propios. La persistencia se registra en `WhatsAppInfrastructureRegistration`.
- Probá reintentos, fallas, carreras y privacidad con el nivel de logs usado por los tests.

## Verificación y ejemplos

- [Pruebas técnicas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Configuración local](../../../../docs/guides/whatsapp-en-local.md).
- [Eliminación del módulo](../../../../docs/guides/quitar-whatsapp.md).

Flows 2b: WabaId opcional de la misma conexión; pertenencia antes de efectos, sin nuevo requisito de arranque de texto. Operación/bindings propios, lock whatsapp-flow y FK compuesta interna; protector de manifiesto/proyección por propósito e identidad. Worker verifica la cuenta, marca el efecto en su propio límite inmediatamente antes del proveedor, acota la llamada al lease/deadline y completa con un token independiente y acotado; un rechazo preenvío seguro limpia InFlight, mientras una cancelación posterior conserva incertidumbre. No reintentar POST ni Create/Publish inciertos. Sin ID consulta candidatos y conserva revisión, sin adoptar por nombre. Cliente sin logs/bearer en CDN/redirects, respuestas limitadas; preview oficial HTTPS y vencimiento explícito. Pruebas sólo Meta simulado.

Los diagnósticos de Flows reconocen índices y propiedades permitidas tanto con puntos como con corchetes de Meta. No transportar mensajes brutos ni deducir IDs desde texto; las identidades vienen del mapping sellado.

Verify lee health_status de Flow y del teléfono exacto por separado, requiere AVAILABLE en ambos y conserva owner/perfil/hash. Assets sólo HTTPS443 sin userinfo desde *.fbcdn.net o mmg.whatsapp.net exacto; sin bearer, redirects ni más de 1 MiB. Bloqueos de cuenta no se eluden con cambios de flags.

El webhook protege response_json de nfm_reply por mensaje/contacto y el worker de captura corre independiente del procesador de texto. Antes de cada Create/Upload/Publish verifica pertenencia paginada de PhoneNumberId al mismo WabaId; los tests cubren un mismatch confirmado antes del efecto con cero llamadas externas. La recepción no llama a Meta; sólo usa puertos de captura neutrales.

Medios: Graph consulta con PhoneNumberId; sólo HTTPS443 de lookaside.fbsbx.com o mmg.whatsapp.net recibe el bearer de descarga, sin redirecciones ni logs HTTP. Valida hash/tipo/tamaño de metadata y límite de lectura de 25.000.000 bytes; el contenido real se valida en el consumidor. No persiste URLs de descarga ni crea recuperación durable de uploads.

`WhatsAppRecordOriginReader` proyecta sesión, salida y contacto por lote para el puerto neutral de origen. Enmascara el número y sólo incluye el enlace a conversación cuando el servicio lo autoriza; no recompila Flows ni consulta Meta.

G6 reutiliza WhatsAppOutgoingMessages y su worker: Origin FormAction, destinatario fijo, operador nullable e índice único RunId+RecipientUserId. Reserva, marca y finalización toman contacto antes de salida; los tokens invalidan workers viejos. PDF: lee la copia privada, verifica tamaño/SHA256, sube multipart a phone/media, persiste mediaId y manda document.id sin URL ni SAS. El inicio de messages se confirma antes del POST; una respuesta conocida se guarda con token de finalización independiente y acotado aunque se apague el host. Nunca reintentar mensajes inciertos ni confirmados. El estado de una acción incierta sólo consulta evidencia durable.

La revisión humana bloquea y refresca la fila de salida, además del lock por run/destinatario, sin tomar luego contacto. Así no pisa la aceptación de un worker concurrente ni invierte contacto→salida. Decisión, actor y fecha quedan durables aunque luego llegue un ID confirmado.

D2 proyecta SenderKind=Manual para las sesiones existentes, creadas por el envío manual. Conserva OperatorUserId/DisplayName y la protección del enlace a conversación; no infiere Assistant de un operador ausente ni agrega consultas. El envío Assistant deberá fijar su origen durable cuando exista ese productor.

WhatsAppFormActionReader proyecta metadata local de salidas FormAction con LEFT JOIN a receipts por wamid, sin necesitar WhatsAppMessage. Filtra pares pedidos exactos y devuelve un lote; las columnas nullable legadas respaldan sólo la fecha del último estado realmente conocido.

EnableWhatsAppAssistant es una migración de datos generada vacía: Bot previo pasa a Manual y avanza revisión; Down detiene Assistant a Manual. No activa chats ni cambia mensajes, lecturas, vínculos o salidas. Las filas se verifican con PostgreSQL en el cierre conjunto.

H6: el parser conserva Audio nuevo aun si su descriptor no es utilizable; admite MIME Ogg/Opus y SHA256 normalizado, sin clasificar Media anterior. AudioReader filtra pendientes por fecha fuente antes de Take y lista referencias privadas en lote. AudioRepository toma mensaje después del contacto. La descarga autenticada usa el cliente existente; el consumidor verifica bytes y WriteOnce. ClearExpiredBodies retira transcript, descriptor, contexto y referencia privada aun cuando Body sea null. La migración conjunta debe convertir Audio a Media y vaciar su Body en Down antes de quitar columnas, sin tocar Media previo.

Las referencias Assistant se bloquean después del contacto y se refrescan bajo lock. La consulta de recibos enviados es compartida por request, sin atribuir el operador del lector. El contenido privado de sesión usa un propósito propio ligado a sus identidades; sus bytes se limpian con entidades seguidas, lote acotado y una UoW en el servicio de retención existente. El payload técnico anterior permanece intacto. La navegación r8 combina mapas iniciales y ofertas de catálogos selladas, preservando los datos anteriores cuando ambos están vacíos.

El lector del asistente admite texto y Audio Completed con Body vigente sin reclasificar Audio. Conserva Message.Id y limita la fuente al mínimo de retención y vencimiento de normalización. La selección del último recibo de negocio ocurre antes de la elegibilidad: un audio pendiente o vencido bloquea texto y punteros anteriores; no busca otro mensaje hacia atrás.

La lectura del chat proyecta sólo metadata ligada a la sesión y el blob protegido para la explicación; Application aplica permiso y vencimiento antes de responder. Los previews Assistant son genéricos desde SQL. El origen de una respuesta usa el sender de la sesión sellada; Assistant no recibe operador por inferencia.

FindReplySessionAsync proyecta el OperatorUserId real como argumento obligatorio de la cabecera neutral. La captura coteja ese dato con Origin y el payload protegido; no infiere el origen ni cambia sesiones o snapshots anteriores.

ListOffersAsync reúne las últimas veinte referencias de Flows y opciones QuickReplies reales del mismo Subject y vínculo vigente, dentro de veinticuatro horas, con límites SQL antes de materializar. Las opciones conservan RequestId de la pregunta, IDs/versiones del codec compartido y título almacenado. Una salida fallida no cuenta como oferta. Los rechazos son una consulta distinta por el conjunto solicitado de definiciones, sin ventana temporal ni límite del historial. Metadata de botones corrupta falla completa; no se convierte en una lista vacía.

El lector H5 conserva QuestionOutgoingId en las proyecciones por request y salida. El scan procesa el último botón válido de una pregunta pendiente; no vuelve a interpretar un texto anterior. El conteo diario agrupa por referencia automática y considera pregunta o Flow no fallidos dentro del día, sin duplicarlos. El historial reciente de ofertas incluye opciones reales de preguntas y Flows del vínculo actual; el conjunto de rechazos se conserva por definición aunque su oferta haya salido de la ventana reciente.

El lector C4 toma FlowReply Completed como fuente sin cuerpo de conversación. Conserva el primer recibo confirmado de la misma captura y su vencimiento; otro texto, audio o respuesta diferente sigue bloqueando la fuente anterior. El puntero admite un recibo equivalente del mismo registro y el scan recoge capturas pendientes en ambos modos; las proyecciones conservan sólo IDs y cultura reales de la referencia.

El procesador entrante corta todo el pipeline cuando Application informa el contacto salteado por SKIP LOCKED; audio y asistente sólo siguen tras adquirir realmente el contacto. Un resultado sin mensajes nuevos sigue habilitando trabajo durable ya pendiente.

La interpretación consulta primero la operación por su UUID y contexto sellado: pending/completed se recuperan; sólo NotFound habilita Submit con el mismo cuerpo. Otros errores de lectura no aceptan a ciegas. Normalización de audio conserva su recorrido anterior separado dentro del mismo procesador.

El pipeline prepara/retira la interpretación superada antes de normalizar el audio nuevo. Un audio pendiente no deja que siga corriendo el texto anterior; la transcripción recién completada entra a interpretación en la siguiente vuelta, sin repetir la aceptación.

RecoverOnly del trabajo interno permite recuperar Pending/Completed conocidos sin Submit. Si Get devuelve NotFound, completar con StateConflict bloquea la referencia local; su mera existencia no demuestra aceptación de IA. Choice y Always conservan su ruta.

El scan de trabajo automático nuevo exige AssistantProcessedAtUtc nulo. El scan de capturas y FindLatestSource usan el primer recibo confirmado del mismo registro mientras el último confirma esa captura; una repetición no reactiva una fuente marcada ni extiende su retención. SourceRows proyecta ese marcador; Sources no lo excluye globalmente, para conservar recuperación de referencias y sugerencias de capturas existentes.

AudioReader selecciona también mensajes sin copia privada para conservarlos al recibir en Manual. Después de guardar, Manual sin RequestId sale del escaneo; una petición explícita posterior vuelve a habilitarlo sin nueva descarga de Meta.

La espera escalonada de descarga de audio se aplica sobre WhatsAppMessage antes de la proyección y de Take, con el mismo calendario acumulado de Flows.
