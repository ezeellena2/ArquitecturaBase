# Inteligencia artificial: configuración y consultas de la instalación

Cada instalación es independiente y tiene una conexión compartida, en Administración → Configuración → Inteligencia artificial (`/configuracion/ia`). Información del negocio es un editor aparte (`/configuracion/negocio`) e Inicio (`/`) admite consultas explícitas y privadas. Guardar configuración o hechos nunca genera, y la conexión no activa sugerencias, bot, contactos ni plantillas. OpenAI y Anthropic/Claude se usan por sus API; una sesión de Codex no es una credencial del producto. La UI (cachés, borradores, cooldowns) sigue `ArquitecturaBaseFront/src/features/artificial-intelligence/AGENTS.md`; claves, límites técnicos y runbook, [configurar IA](../guides/configurar-ia.md).

## Módulo y responsabilidades

`ArtificialIntelligence` vive en `Modules/ArtificialIntelligence` de cada capa, no referencia WhatsApp ni recibe referencias del núcleo, y `Program.cs` compone un registro por capa. Sus permisos se suman con `IPermissionCatalogSource` y sus ocho tablas están en la cadena única de migraciones ([retirada](../guides/quitar-ia.md)). Application coordina la conexión con `AiConnectionCandidatePolicy` y `AiConnectionChangeRecorder`. En Infrastructure, `ILlmClient` lista modelos y prueba (dos adaptadores `ILlmProviderClient`) y `ILlmConversationClient` atiende al asistente con adaptadores propios.

## Contrato HTTP y permisos

La conexión usa `ai.read` y `ai.manage`; Información del negocio, `ai.business.read` y `ai.business.manage`. Ninguno da acceso al otro formulario ni a sugerencias en WhatsApp.

| Verbo/ruta | Permisos | Resultado |
| --- | --- | --- |
| GET `/api/ai/connection` | `ai.read` | proveedor, modelo, `hasCredential`, Disponible/Pausada, revisión y proveedores soportados |
| PUT `/api/ai/connection` | `ai.read` + `ai.manage` | comprobante con `requestId` y metadatos confirmados |
| GET `/api/ai/connection/changes/{requestId}` | ambos | comprobante propio; 404 si no está disponible |
| POST `/api/ai/connection/test` | ambos | revisión de partida y tokens de entrada/salida opcionales |
| POST `/api/ai/connection/models` | ambos | proveedor, revisión de partida e ID y nombre de los modelos visibles para la clave |

Sin fila, la lectura devuelve revisión 0, OpenAI, modelo vacío y Pausada, sin escribir. `ToString` oculta las claves. La prueba no devuelve texto generado, errores crudos, request IDs del proveedor, cabeceras ni secretos; los logs no llevan argumentos y los loggers de ambos clientes HTTP están apagados.

El modelo es un identificador explícito de hasta 150 caracteres. La clave admite hasta 4096 caracteres ASCII imprimibles, sin espacios ni saltos. `Replace` exige clave; `Keep` y `Remove` la exigen nula, y `Remove` pausa. Cambiar de proveedor con `Keep` y una clave guardada falla: una clave nunca va a otro proveedor. Disponible exige modelo y credencial; la prueba es manual y funciona también en Pausada.

### Catálogo de modelos — 2026-10-04

Recibe proveedor, `Keep`/`Replace`, clave candidata opcional y revisión esperada, con la política de credenciales de la prueba; no exige modelo, no guarda la candidata y no abre transacción. La clave va en el cuerpo del POST y el proveedor recibe un GET a `/v1/models`, por los mismos clientes sin redirects, loggers ni reintentos. Recorre hasta 20 páginas y 2000 modelos, y devuelve IDs de hasta 150 caracteres y nombres de hasta 200, sin duplicados y ordenados por ID. JSON inválido, cursor repetido o lista truncada fallan: nunca se presenta una lista parcial como completa. Comparte con la prueba plazo, tope de bytes y slots, y usa el límite de escritura. Un timeout es indisponibilidad, no un cobro incierto. No se consulta desde readiness ni al guardar.

El editor nunca elige un modelo ni reemplaza uno guardado fuera de la lista, y admite entrada manual. Figurar en la lista no acredita compatibilidad ni cuota: lo verifica la prueba. Fuentes: [OpenAI Models](https://developers.openai.com/api/reference/resources/models/methods/list) y [Anthropic Models](https://platform.claude.com/docs/en/api/models/list); falta probar con cuentas reales.

## Guardado, concurrencia y recuperación

El PUT lleva `ExpectedRevision` y un UUID `RequestId` por intento. Valida antes del único límite `OnSuccess`; adentro toma el advisory lock `ai-connection:installation`, busca el comprobante propio, compara la revisión y guarda cambio y comprobante juntos. El índice único de Scope=1 impide dos conexiones, y no hay HTTP dentro del lock.

Un reintento idéntico devuelve el mismo comprobante aunque haya cambios posteriores; otro cuerpo con el mismo UUID da 409. La igualdad usa un HMAC sobre proveedor, modelo, operación, clave candidata, estado, revisión y UUID. El comprobante guarda digest, keyId, operador, metadatos y fecha, nunca la clave, durante **30 días**: las lecturas omiten los vencidos y el siguiente guardado los borra. No hay caché de credenciales. Un 404 **no demuestra rollback**: se consulta otra vez o se repite el mismo cuerpo y UUID. Tras un conflicto se relee y el cambio siguiente usa otro UUID; nunca se compara una clave oculta.

## Cifrado y operación

El cifrado sigue el [ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md): AES-256-GCM con envelope versionado y un AAD de versión, instalación, keyId, propósito y proveedor, de modo que la clave no se descifra para otro destino; HMAC con HKDF y otro propósito. El anillo y el `InstallationId` van **fuera de PostgreSQL**, iguales en las réplicas y distintos en otra instalación, y el arranque valida el anillo aunque no haya clave. Perder una clave histórica vuelve ilegibles sus credenciales y comprobantes, sin fallback ni regeneración.

## Prueba acotada

Prompt fijo `Reply with the single word OK.`, sin datos del negocio. OpenAI: POST `https://api.openai.com/v1/responses` con Bearer y `store:false`, sin streaming ni herramientas. Anthropic: POST `https://api.anthropic.com/v1/messages` con `x-api-key` y `anthropic-version:2023-06-01`. Sin URL arbitraria ni redirects. Plazo total, tamaño de respuesta, tokens de salida y pruebas simultáneas (sin cola) son configuración técnica validada. Un POST facturable se intenta **una vez**; un timeout después del envío da `TestUncertain`, porque pudo haber cobro.

401 y 403 se informan como clave rechazada; modelo incompatible, cuota, 429 e indisponibilidad tienen códigos propios. Un JSON inválido, incompleto o vacío nunca acredita la prueba, y el uso inválido se omite. Si un modelo de razonamiento agota su tope sin texto, la prueba queda incompleta, sin subir el costo.

Los rate limits de lectura, escritura y prueba son por operador, sin cola y después de autenticar. Ellos y el semáforo son **por réplica**: el presupuesto real se fija en el proveedor. Readiness, lecturas y guardados nunca llaman al proveedor. Ninguna suite llama a una API facturable ([evidencia](../reviews/2026-10-02-configuracion-llm.md)).

## Información del negocio — 2026-10-03

Guardar registra hechos confirmados sin llamar al proveedor; Inicio los usa por la fuente `business.information` solo ante una consulta autorizada, nunca para sugerencias ni atención en WhatsApp. Máximos: nombre 120, descripción 1000, horario y ubicación 500, productos y políticas 2000, preguntas libres 4000. Son obligatorios el nombre y al menos un hecho confirmado (texto o detalle estructurado). Se recortan los espacios externos y no se inventan hechos, stock ni precios. No hay documentos, RAG, publicación editorial ni historial, y los comprobantes no copian los hechos. No se cargan claves ni datos personales de clientes; logs y `ToString` no incluyen los textos.

| Verbo/ruta | Permisos | Resultado |
| --- | --- | --- |
| GET `/api/ai/business-information` | `ai.business.read` | campos, `details` opcional y revisión; sin fila, vacíos y revisión 0 |
| PUT `/api/ai/business-information` | lectura + `ai.business.manage` | `requestId` y revisión confirmada, sin texto histórico |
| GET `/api/ai/business-information/changes/{requestId}` | ambos | comprobante propio; 404 si falta o venció |

Usa los rate limits existentes y un cuerpo de hasta 256 KiB. `AiBusinessInformationService.UpdateAsync` valida antes del único límite `OnSuccess`; adentro toma solo `ai-business-information:installation`, deduplica por operador y UUID, compara la revisión (Scope=1 único) y crea `AiBusinessInformationChange` con el cambio, sin LLM, conexión, caché ni HTTP. Mismo pedido, mismo comprobante; otro cuerpo con igual UUID o una revisión vieja dan 409, y un 404 no demuestra rollback. El comprobante dura 30 días y su huella (`ai-business-information-fingerprint:v1`, mismo anillo) cubre campos normalizados, revisión y UUID, y agrega `details:v1` solo si hay `details`, para reconocer pedidos anteriores. Un backup sin el anillo lee los hechos, pero no verifica reintentos ([evidencia](../reviews/2026-10-03-informacion-del-negocio.md)).

### Editor guiado y evolución del contrato

`details` (`location`, `openingHours` y `faq`) va en una columna JSONB nullable de la misma entidad, dentro del mismo límite. Omitirlo o mandarlo null conserva lo actual; para vaciarlo se envían los grupos sin definir, y siguen valiendo nombre y hecho mínimo. Los clientes anteriores conservan la validación de sus textos.

- **Ubicación:** `unknown/local/area/both`; dirección, referencia y zona de hasta 500; PlaceId opcional (hasta 1024 ASCII imprimibles sin espacios) o coordenadas históricas completas, finitas y en rango a seis decimales, nunca ambos. El enlace a Maps se deriva con destino fijo y editar la dirección quita la selección. No se importan direcciones de Google ni se pide la ubicación del dispositivo.
- **Horario:** siete días (lunes=0) en `unknown/open/closed/allDay`, hasta cuatro franjas HH:mm cada 15 minutos y `nextDay` para cruzar la medianoche, sin intervalos vacíos, de más de 24 horas ni superpuestos (también domingo-lunes). Zona IANA o UTC, tomada de la presentación general solo para un horario nuevo. Hasta 32 excepciones DateOnly únicas (1900–9998) reemplazan el día y se validan contra los vecinos. No se infieren feriados.
- **FAQ:** hasta 20 entradas con UUID único, pregunta de 200 y respuesta opcional de 1000. Solo los pares completos cuentan como hechos; una pregunta sin respuesta no inventa una ni genera.

En un conflicto, cada grupo se combina entero; los errores usan `details.location`, `details.openingHours` y `details.faq`. La migración `20261003215957_AddGuidedAiBusinessInformation` solo agrega la columna, conserva datos y comprobantes y no interpreta el texto libre. `LocationMap` (Leaflet/OSM) ya no se usa en este formulario; sus teselas se configuran con `VITE_BUSINESS_MAP_TILE_URL`, plantilla pública sin claves ([verificación](../reviews/2026-10-03-informacion-del-negocio-guiada.md)).

### Búsqueda de lugares — 2026-10-04

POST `/api/ai/business-information/places/search` exige ambos permisos del negocio, 4 KiB de cuerpo, la política `ai-places`, no-store e inventario/OpenAPI. Recibe de 3 a 200 caracteres sin controles y la cultura del pedido; no guarda, no abre transacciones ni usa la conexión LLM. `GooglePlacesClient` usa Autocomplete (New) con origen fijo y field mask de placeId/text, sin Details ni caché, y devuelve hasta cinco sugerencias. Solo se persiste el PlaceId, sin migración (la huella agrega `place:v1` si hay ID). Límites, atribución y Términos/Privacidad pendientes: [configuración](../guides/configurar-google-places.md) y [verificación](../reviews/2026-10-04-google-places.md).

## Inicio autenticado con consultas — 2026-10-04

Inicio usa `@assistant-ui/react` con `ExternalStoreRuntime`, y `app` compone `AiAssistantPage`. El núcleo publica `installedFeatures` por `IClientCapabilitySource`, fuera del valor cacheado de ajustes: sin IA, Inicio muestra su contenido base y la portada pública no cambia. Falta sincronizar el tablero de estados con el Artifact externo. Solo consulta: acepta una pregunta explícita, muestra etapas confirmadas y publica una respuesta completa validada, sin texto progresivo, escritura de negocio ni MCP externo. Recargar o recuperar una respuesta nunca repite la generación ([ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md)).

### Contrato y privacidad

Todas las rutas exigen `ai.assistant.use`, sesión y cuenta activa; el servicio revalida dueño y permisos y un UUID ajeno da 404. La aceptación solo admite `requestId`, `conversationId`, `expectedRevision`, `expectedConnectionRevision` y `question`; cultura y zona las toma el servidor. Historial, respuestas y estados son `no-store`.

| Verbo/ruta bajo `/api/ai/assistant` | Resultado |
| --- | --- |
| GET `/bootstrap` | disponibilidad, revisión de conexión, sugerencias y límite público |
| GET `/conversations` | historial privado paginado, título y estado de acceso |
| GET `/conversations/{id}` | revisión, acceso y operación activa |
| GET `/conversations/{id}/turns` | turnos confirmados paginados |
| POST `/turns` | 202 con comprobante y revisiones; una UoW |
| GET `/turns/{requestId}` | estado o resultado propio; nunca genera |
| POST `/turns/{requestId}/cancel` | cancelación durable; una UoW |
| DELETE `/conversations/{id}` | 204, retira el acceso y cancela; una UoW |

Un reintento idéntico (huella `assistant-request-fingerprint:v1`) recupera la aceptación antes de revisar conexión o cuota; otro cuerpo con el mismo UUID da 409. Tras retirar una conversación, su POST da 404 sin crear otra operación, y el GET por `requestId` conserva la metadata terminal durante la retención. Un 404 de recuperación no demuestra rollback. Conversación y turno tienen revisiones distintas, y los índices impiden dos operaciones activas por actor o conversación. Pregunta, respuesta, evidencia y resultados se cifran (`assistant-content:v1`, con AAD de instalación, keyId, dueño, conversación, turno, propósito y revisión); sin la clave histórica la lectura falla y nada se regenera. No se registran textos, secretos, argumentos, continuación ni cabeceras del proveedor.

### Fuentes y autorización

Las consultas de Inicio se aportan mediante `IAssistantCapabilitySource` y se ejecutan por el único `IAssistantCapabilityExecutor`, con un scope nuevo por llamada. El canal Inicio conserva explícitamente las cinco herramientas de abajo y `assistant_answer@1`; instalar otra capacidad no la agrega a su catálogo, sus sugerencias ni su historial. Cada fuente revalida actor y permisos. En Inicio, el modelo no elige SQL, tablas, filtros ni URL.

| Herramienta | Permiso adicional | Hechos ofrecidos |
| --- | --- | --- |
| `users.summary` | `users.read` | conteos de cuentas activas e inactivas visibles, sin datos personales |
| `roles.list` | `roles.read` | nombres, descripciones y conteos; hasta 50 filas, sin fingir completitud |
| `roles.permissions` | `roles.read` | un rol por ID o nombre, con permisos del catálogo instalado |
| `business.information` | `ai.business.read` | hechos, FAQ y horario; hasta 50 filas, truncamiento explícito, sin deducir lo ausente |
| `system.help` | ninguno | ayuda curada, con enlaces solo a destinos autorizados |

La salida final es una única herramienta `assistant_answer` con kind, texto e IDs de evidencias observadas. Se rechazan argumentos extra, herramientas desconocidas, referencias inventadas, respuestas factuales sin fuente y texto libre del proveedor; esto valida estructura y procedencia, no la semántica. Cada resultado de herramienta queda registrado aunque no haya evidencia, y un CallId no se acepta dos veces.

Antes de llamar al proveedor, la conversación suma los permisos de todas las fuentes ofrecidas, para no exponer nada si el worker cae antes de guardar evidencia. Cada paso verifica permisos actuales: si falta uno, se ocultan pregunta, respuesta y fuentes, pero borrar la conversación propia sigue disponible. Una conexión cambiada o pausada invalida la continuación y la publicación.

### Motor único y propuestas por canal — Etapa 5

El [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md) generaliza ese motor, sin otro coordinador, cola LLM ni transporte por funcionalidad. Un contexto creado por servidor fija canal, Actor, Subject opcional, cultura, zona horaria, capacidades y terminales permitidas. No es una entrada HTTP ni un argumento del modelo. Cada identidad debe estar activa y conservar sus propios permisos al aceptar, ejecutar y publicar; no hereda los de la otra. Inicio conserva `ai.assistant.use`; ese permiso no se impone por analogía a otros canales.

Cada capacidad declara un Id y versión exactos, tipo Read o Proposal, requisitos separados para Actor/Subject, esquemas y DTO con codec cerrado y presupuesto de entrada, salida, ítems y tiempo. Las fuentes sólo leen o devuelven propuestas: no reciben UoW, escritores, transporte del modelo ni un ServiceProvider. El motor aplica los presupuestos antes y después del aportante; el ejecutor aísla su scope. Un turno ofrece una sola versión por Id; una versión ausente no se sustituye por la vigente.

El motor asigna el Id de cada propuesta y la salida terminal sólo selecciona propuestas observadas del mismo turno. Las lecturas que justifican una propuesta también deben ser de ese turno, de una capacidad/versión exacta y ya confirmadas y enviadas al proveedor en un paso anterior. Leer y proponer en el mismo lote no acredita que el modelo haya visto esos datos. Los resultados se protegen por propósito; la propuesta conserva referencias a las ejecuciones de lectura existentes, sin volver a copiar sus snapshots. La lectura interna las reconstruye por sus codecs exactos, sin repetir consultas para inventar evidencia histórica ni exponer metadata o continuación por el HTTP de Inicio.

La conversación conserva su canal y el turno fija perfil de modelo e instrucciones con Id/versión, contexto y catálogo aceptados. Cambiar la configuración no cambia esa captura: sólo se usa su versión exacta disponible o se falla antes del proveedor. La historia que no registró esos datos sigue sin atribución de perfil; no se completa con la configuración actual. El dueño de una propuesta revalida vigencia y permisos antes de ejecutarla con su propia UoW y cola si hay efectos externos, y guarda su resultado mínimo para no depender después de la retención de IA.

### Ejecución, fallas y mantenimiento

El coordinador abre scopes nuevos por etapa; no recibe UoW ni llama a los servicios de escritura. Orden de locks: conexión → presupuesto → actor → conversación, nunca durante el HTTP. Un claim durable fija intento, fence y deadline, y cada paso se reserva antes de enviarse. Los clientes usan origen fijo, herramientas tipadas y continuación privada, sin redirects, loggers ni reintentos facturables; los nombres con punto pasan a guion bajo sin cambiar los IDs del catálogo. El uso de tokens informado se guarda también al fallar; el ausente es desconocido, nunca cero. No hay precios.

Cancelar un pendiente termina enseguida; en ejecución corta el transporte, salvo una respuesta ya confirmada. Un Running vencido con paso reservado queda Uncertain y no se repite; sin paso externo, Expired. Si falla el commit de una respuesta obtenida, solo se reintenta su persistencia con el mismo fence, por un plazo acotado.

Cuotas diarias, capacidad y unicidad se coordinan en PostgreSQL entre réplicas. El mantenimiento reconcilia y purga en lotes; borrar no libera el UUID ni la cuota. Una conversación retirada se borra cuando vencieron todos sus turnos (hasta entonces su metadata permite recuperar el estado final), y las cuentas borradas retiran y cancelan las suyas. Cuotas, capacidad y retención siguen **propuestas para el piloto**, y no hay perfiles de modelo habilitados por defecto: listar o probar la conexión no acredita herramientas ni contexto ([habilitar el asistente](../guides/configurar-ia.md#habilitar-el-asistente)). Las métricas no usan IDs ni contenido como etiquetas ([revisión](../reviews/2026-10-04-inicio-asistente.md)).

## Panel interno e identificación del historial — 2026-10-05

`/` abre una conversación nueva y `/?conversacion=<id>` restaura una elegida; abrir o paginar no escribe ni genera, y la primera aceptación crea las filas. El panel lista 20 conversaciones por página, por actividad e Id. El título sale de la primera pregunta cifrada, con permisos actuales y contenido vigente (`ReadPolicy`, `ResponsePolicy`), y se corta a 80 grafemas (`ConversationTitlePolicy`); bloqueo, purga o vencimiento muestran un genérico localizado y la corrupción, ContentUnavailable. No hay columna de título ni llamada al LLM. `AiAssistantReader` resuelve cada página en dos consultas, sin N+1 ni leer respuestas o herramientas. La capacidad sigue en el [plan](../plans/2026-10-04-panel-conversaciones-inicio.md).
