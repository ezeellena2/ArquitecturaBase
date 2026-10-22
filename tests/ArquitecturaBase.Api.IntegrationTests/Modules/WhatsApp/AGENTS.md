# WhatsApp: tests de Api.IntegrationTests

Prueba webhook, envío y registro de entregas, clasificación/acceso explícito, cuota, vinculación, rutas y convivencia con el núcleo sobre PostgreSQL y Redis. La cola de acceso sigue en memoria. El chat manual acredita salida durable, revisión de resultados inciertos, lectura por operador, receipts tempranos, migración con mensajes previos y leases entre hosts; Meta está simulado.

## Al modificar

- `WhatsAppTestModule` aporta settings, servicios e inventario al arnés común. ApiFactory conserva estado propio, pero no implementa un partial void compartido con otros módulos (ADR 0013).

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Probá firma, idempotencia, concurrencia y privacidad del webhook con el arnés existente.
- Las rutas e inventarios del módulo se amplían en sus partes; desvincular el número propio sigue siendo del núcleo.
- Restaurá configuración de proveedores y verificá los escenarios con el módulo apagado cuando corresponda.
- Los tests de Flows usan `IStructuredFormChannel` y `IStructuredFormSource` neutrales, habitualmente con snapshots/fuentes ficticios; no importan BusinessForms. Las pruebas de composición acceden al módulo opcional por HTTP/puertos neutrales, sin reemplazar sus fuentes o captura reales. Los dobles del borde están en `TestFeatures/Modules/WhatsApp` y se retiran con el módulo.
- Verificá la pertenencia WABA/PhoneNumberId antes de persistir el inicio de Create/Upload/Publish; un rechazo previo puede reintentar el mismo paso, una cancelación luego de invocar sólo reconcilia.
- La captura local recorre webhook firmado, protección y lectura autorizada con puertos neutrales. BusinessForms prueba sink/source reales con PostgreSQL; las composiciones que los usan lo declaran expresamente. Ninguno de estos tests acredita round-trip físico con Meta.

WhatsAppFormLanguageIntegrationTests verifica tres versiones reales por HTTP con cuenta destinataria en inglés: original español, suma de inglés y pérdida del inglés. Ambos workers publican por el arnés existente con Meta simulado; apertura predeterminada, CTA y sesión siguen es/en/es. Las respuestas de sesiones anteriores llegan después de activar la tercera versión, con captura y lectura reales por los puertos neutrales. Compara bindings, hashes y bytes sellados anteriores. El cierre de backend ejecutó este recorrido con PostgreSQL/Redis; no acredita entrega física a Meta.

WhatsAppFlowProfileUpgradeTests reutiliza los bytes r3 y r8 existentes como recursos para abrir una sesión antes de publicar r9 y capturarla después. El snapshot r8 incluye su catálogo y la oferta previa; compara proyecciones, hashes, payload y huella de sesión sin recompilar ni reescribir el legado. Se prepara por corte y se ejecuta con PostgreSQL/Redis en el cierre conjunto; no acredita Meta físico.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

G6 usa WhatsAppFormActionScenario con base aislada y Meta/almacenamiento privados simulados. Verifica upload y document.id, idempotencia entre hosts, orden de locks, teléfono cambiado, confirmación tras shutdown, retención y convivencia con atención manual. Los cuerpos HTTP reales se capturan sólo con WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT; no acreditan entrega real en Meta.

WhatsAppFormActionReadQueryTests compila el SQL sin conexión; WhatsAppFormActionReceiptTests usa persistencia real del webhook para receipts tempranos sin historial y filas legadas. WhatsAppReceiptTimelineMigrationTests comprueba Up/Down aditivos, sin backfill ni historia inventada; sólo las dos últimas requieren Docker.

H6 separa pruebas técnicas sin Docker (parser Audio, retención configurada y cliente MIME Ogg/Opus) de WhatsAppChatTests.Audio con PostgreSQL/HTTP (Read mínimo, idempotencia entre operadores, pertenencia, reproducción Range privada y referencias/purga). Sus escenarios se compilan por corte y se ejecutan con el cierre conjunto. WHATSAPP_AUDIO_HTTP_OUTPUT exporta bytes HTTP de pedido e hilo Pending/Completed con texto sintético, sin acreditar transcripción del proveedor.

Los técnicos C3 de protector/payload/retención no levantan PostgreSQL: verifican propósito e identidades de los sellos, mapas wire y purga privada desde el límite existente. Las pruebas de filas, concurrencia y HTTP real se compilan y quedan para la integración conjunta; no se atribuyen verdes a tests no ejecutados.

C3 usa WhatsAppAssistantHttpTests sobre una base aislada y los servicios reales de pedido, lectura, descarte, aceptación y despacho. WHATSAPP_ASSISTANT_HTTP_OUTPUT conserva nueve respuestas HTTP intactas (seis estados, recibo e hilos con/sin lectura de registros). El transporte Meta y Operations están controlados; la fuente de formularios es neutral según la independencia del módulo. No acredita decisiones de un modelo ni entrega real a Meta. Las restricciones y el replay entre operadores se verifican con permisos y persistencia reales.

WhatsAppAssistantOriginQueryTests observa la traducción SQL real del tipo de sender sin abrir una base; los escenarios HTTP de cierre comprueban receipt e historial con la misma explicación privada.

WhatsAppAssistantAutomaticIntegrationTests usa los mismos dispatcher, cola y worker reales sobre bases aisladas para cupo de dos ofertas por día calendario del negocio, replay, toma Manual antes del resultado, revocación y cambios del dueño antes de encolar. La IA no disponible se procesa por InboundProcessor con una consulta previa de recuperación que devuelve NotFound; el único intento de aceptación se rechaza sin consultar después ni encolar un mensaje. Se compila por corte; PostgreSQL/Redis y el transporte Meta simulado se ejecutan en el cierre conjunto.

WhatsAppAssistantOfferQueryTests verifica SQL de pertenencia/vínculo y QuickReplies no fallidas sin abrir una base, y decodifica las opciones almacenadas con el codec compartido. WhatsAppAssistantChoiceIntegrationTests usa PostgreSQL, dispatcher e InboundProcessor reales para pregunta→elección, replay, versión retirada y cupo compartido; sus cuerpos controlados acreditan el circuito WA y no la decisión del modelo.

WhatsAppAssistantStage5MigrationTests parte de EnableWhatsAppAssistant en una base nueva y recorre la última migración conjunta para Up y Down compatible. Conserva sesiones/operadores/receipts confirmados, prueba INSERTs del contrato anterior después de Up y Audio→Media sin transcript en Down. Los hechos no representables parten de AddFormAssistantSuggestionsAndAudio para aislar su guard transaccional explícito: el snapshot completo de filas, columnas e historial de ese esquema permanece idéntico. Se compila por corte y sólo se ejecuta con PostgreSQL en el cierre.

WhatsAppAssistantDeclineIntegrationTests observa opciones QuickReplies mediante Read y Proposal reales de WhatsAppAssistantCapabilitySource, y luego usa el dispatcher real con el resultado controlado. Comprueba rechazo durable, replay y bloqueo posterior del mismo formulario sin una solicitud faltante; queda para PostgreSQL del cierre.

Los técnicos C4 de SourceQuery, ReferenceProjection y CaptureQuery no abren una base: comprueban SQL, cabecera configurada nullable y selección del primer recibo confirmado sin extender su retención. WhatsAppAssistantHttpTests.Suggestions prepara nueve casos PostgreSQL para Always/Assistant, atención Manual/automática, permisos históricos, cupo, bucles y replay después de capturar; comparte el productor HTTP existente y queda para el cierre conjunto.

WhatsAppAudioPostgresDispatchTests ejecuta despacho real en ambos modos: copia Manual sin petición, POST posterior, sello durable, polling sin releer blob y Complete con transcripción guardada. La retención de audio comprueba Body no nulo antes de limpiar; no basta partir de un descriptor sin transcripción. Se ejecutan sólo en el cierre conjunto.

WhatsAppFormChannelStateQueryTests verifica registro scoped con transporte apagado, traducción SQL por lote e índice de sesiones Pending sin abrir una base. WhatsAppFormChannelStateIntegrationTests prepara versiones y orígenes distintos, sesiones vencidas/terminales y respuestas recibidas a tiempo pendientes de captura; usa el puerto neutral y limpia sólo sus IDs. Sus casos PostgreSQL quedan para el cierre conjunto; no usa Meta ni tipos de BusinessForms.

FlowDeprecationClientTests, QueryTests y WorkerTests son técnicos sin Docker: HTTP simulado, SQL sin conexión y scopes falsos comprueban ACK/estado remoto, IDs/owner, guards por versión, fence independiente y finalización tras cancelación. Las composiciones PostgreSQL/Redis y varios hosts quedan para el cierre conjunto, sin acreditar Meta real.

WhatsAppFlowDeprecationIntegrationTests prepara publicación y captura reales de formularios por HTTP/puertos neutrales en bases aisladas, sin imports entre módulos. Conserva sesiones de versiones reemplazadas y dadas de baja, recibe una respuesta a tiempo y la captura tras vencer la sesión antes de retirar esa versión; otra versión abierta se protege de manera independiente. Dos hosts comparten lease/fence y consultan un POST incierto: Deprecated confirma, Published permite un POST posterior y Unknown sólo vuelve a consultar. Un publicado eliminado sin respuestas usa el mismo retiro. Los sellos de publicación/sesión permanecen intactos; Redis conserva su reloj real para el cupo. Se ejecutan con PostgreSQL/Redis sólo en el cierre conjunto y no acreditan Meta físico.

WhatsAppAssistantLatencyIntegrationTests mide tiempo real local con Stopwatch y TimeProvider.System en una base aislada: texto, normalización de audio, nfm_reply y salida. Mantiene coordinador, servicios, reservas y workers productivos; sólo los proveedores externos son simulados. ASSISTANT_LATENCY_OUTPUT guarda tiempos y conteos sin contenidos ni identidades. Es una medición local, no latencia de Meta ni calificación de modelos.
WhatsAppInboundPollingTests prepara el ciclo real con FakeTimeProvider y un aviso local: distingue referencias vigentes de las fuentes nuevas, conserva intervalos configurados más rápidos, espera corta durante retry de audio y vuelve al intervalo habitual sin pendientes. El fixture observa el registro del timer antes de avanzar el reloj, sin demoras arbitrarias. Los QueryTests verifican SQL sin conexión; las variantes HTTP del asistente y el despacho PostgreSQL verifican vigencia y backoff con bases aisladas en el cierre conjunto.
