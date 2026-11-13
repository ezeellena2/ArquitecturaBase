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
- Una composición que necesita el módulo de formularios empieza con `Assert.SkipUnless` sobre `IStructuredFormPublicationDriver`, en el test o en su escenario, y sin el módulo se omite. El inventario deja afuera, sin él, las siete rutas de `WhatsAppRouteFeature.Forms`.
- Un perfil nuevo de Flows se prueba con los bytes ya sellados (r3, r8) como recursos: se comparan proyecciones, bindings, hashes, payload y huella de sesión, sin recompilar ni reescribir el legado.
- La retención de audio parte de un `Body` no nulo: un descriptor sin transcripción no prueba que se limpie.
- Con `FakeTimeProvider`, observá el registro del timer antes de avanzar el reloj, sin demoras arbitrarias.
- Las técnicas sin Docker (SQL compilado sin conexión, HTTP o scopes simulados) no reemplazan las de PostgreSQL y Redis, que corren en el cierre; no se atribuyen verdes a tests no ejecutados.
- Los cuerpos HTTP se exportan intactos y con texto sintético sólo con su variable (`WHATSAPP_FORM_ACTIONS_HTTP_OUTPUT`, `WHATSAPP_AUDIO_HTTP_OUTPUT`, `WHATSAPP_ASSISTANT_HTTP_OUTPUT`); `ASSISTANT_LATENCY_OUTPUT` guarda sólo tiempos y conteos. Ninguno acredita entrega a Meta, transcripción del proveedor ni decisiones de un modelo.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md). Qué prueba cada clase está en la sección de su tema: [envío y cultura](../../../../docs/features/whatsapp.md#envío-durable-de-formularios), [catálogos](../../../../docs/features/whatsapp.md#catálogos-de-formularios--k7), [retiro de Flows y estado neutral](../../../../docs/features/whatsapp.md#deprecación-durable-de-flows--2026-10-21), [avisos](../../../../docs/features/whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6), [sugerencias](../../../../docs/features/whatsapp.md#sugerencias-al-operador-y-explicación--etapa-5), [audio](../../../../docs/features/whatsapp.md#audio-recibido--etapa-5-h6), [planillas](../../../../docs/features/whatsapp.md#planilla-recibida--ia-etapa-2-2026-11-10), [ofrecimientos y autoría](../../../../docs/features/whatsapp.md#ofrecimientos-automáticos--etapa-5), [espera y latencia](../../../../docs/features/whatsapp.md#espera-del-asistente-y-avisos-locales--2026-10-21) y [perfiles de Flows](../../../../docs/features/whatsapp.md#selección-múltiple-y-perfil-r9).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).

Casos del asistente: `WhatsAppAssistantAgentEndpointsTests` usa una base propia (el agente es una fila de la instalación) y prueba las cinco rutas (401 y 403), el 409, el 400 por ruta, el cuerpo cerrado y Probar con un motor controlado que guarda lo enviado; `WhatsAppAssistantAgentMigrationTests` prueba Up/Down/Up y la negativa del Down con el agente y con una conversación derivada. `WHATSAPP_ASSISTANT_AGENT_HTTP_OUTPUT` conserva los once cuerpos de la cohorte `whatsapp-assistant-agent-http` (también el chat del AS4: la lista, el hilo y la sugerencia con texto), sin acreditar decisiones del modelo.

Documentos (tarea 8): `WhatsAppReceivedDocumentPostgresTests` usa `WhatsAppDocumentProbe` y una bandeja neutral capturadora, y `WhatsAppReceivedDocumentInboxTests` la real de formularios; `WhatsAppDocumentWebhookReaderTests` y `WhatsAppReceivedDocumentPipelineTests` corren sin Docker, y `WhatsAppChatTests.Document` deja `document-thread` en `WHATSAPP_AUDIO_HTTP_OUTPUT`.

Documentos (tarea 9): `WhatsAppAssistantDocumentIntegrationTests` usa la misma sonda y bandeja, del webhook a la pregunta y su toque; `WhatsAppAssistantFileCatalogTests` cuenta también las definiciones con un documento, y `WhatsAppDocumentQuestionsMigrationTests` prueba Up/Down/Up y la negativa del Down con una lista.

W3 (tarea 22): `WhatsAppContactWebhookReaderTests` corre sin Docker con los cuerpos de Meta; `WhatsAppSharedContentMigrationTests` prueba Up/Down/Up y la negativa del Down, y `WhatsAppChatTests.Contact` pasa por el webhook firmado y deja `contact-thread` en `WHATSAPP_AUDIO_HTTP_OUTPUT`.
