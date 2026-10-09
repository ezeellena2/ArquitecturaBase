# Inteligencia artificial: configuración y consultas de la instalación

Implementado el 2026-10-02. Una instalación independiente por negocio y una conexión
compartida, en Administración → Configuración → Inteligencia artificial (`/configuracion/ia`).
La configuración no activa sugerencias, bot, contactos ni plantillas. El 2026-10-03 se agregó
Información del negocio como editor independiente. El 2026-10-04 se agregó Inicio autenticado
con consultas explícitas y privadas; guardar configuración o hechos no dispara generación.
OpenAI y Anthropic/Claude se usan por sus API; una sesión de Codex no es una credencial del producto.

## Inicio autenticado con consultas — 2026-10-04

Inicio autenticado en `/` usa `@assistant-ui/react` con `ExternalStoreRuntime`. `app` compone
la sesión, la presentación general y `AiAssistantPage` de `artificial-intelligence`; el núcleo publica `installedFeatures`
mediante `IClientCapabilitySource`, fuera del valor cacheado de ajustes. Si se retira IA, Inicio
conserva el contenido base sin intentar sus rutas. La portada pública conserva su recorrido.
Campo, Public Sans y UI común se mantienen. El tablero de estados es/en y tres tamaños se
completó antes del producto; la sincronización al Artifact externo sigue pendiente.

La primera versión sólo consulta: acepta una pregunta explícita, muestra etapas confirmadas
y publica una respuesta completa validada. No hay texto progresivo, escritura de negocio ni
MCP externo. No se repite generación al recargar, leer, volver a una conversación o recuperar
una respuesta HTTP perdida. [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md).

### Contrato y privacidad

Todas las rutas requieren `ai.assistant.use`, sesión y cuenta activa. El servicio vuelve a
comprobar propietario y permisos actuales; un UUID ajeno devuelve 404. El cuerpo de aceptación
contiene sólo `requestId`, `conversationId`, `expectedRevision`, `expectedConnectionRevision`
y `question`; propiedades adicionales se rechazan. Cultura efectiva de Accept-Language y
zona del perfil se capturan desde el servidor. Fechas UTC con Z; el front usa formateadores
comunes del perfil. Historial, respuestas y lecturas de estado son `no-store`.

| Verbo/ruta bajo `/api/ai/assistant` | Resultado |
| --- | --- |
| GET `/bootstrap` | disponibilidad, revisión de conexión, sugerencias autorizadas y límite público |
| GET `/conversations` | historial privado paginado, título autorizado derivado y estado de acceso |
| GET `/conversations/{id}` | revisión, acceso y operación activa |
| GET `/conversations/{id}/turns` | turnos paginados confirmados |
| POST `/turns` | 202, comprobante de aceptación y revisiones; una sola UoW |
| GET `/turns/{requestId}` | estado/resultado propio; nunca ejecuta generación |
| POST `/turns/{requestId}/cancel` | pedido durable de cancelación; una sola UoW |
| DELETE `/conversations/{id}` | 204, retirada inmediata de acceso y cancelación; una sola UoW |

El pedido lógico usa un UUID y una huella HMAC independiente del contenido cifrado. Un reintento
idéntico recupera la misma aceptación antes de revisar conexión/cuota; otro cuerpo con ese UUID
devuelve 409. Tras retirar una conversación, repetir su POST idéntico devuelve 404 sin crear
otra operación; GET por requestId conserva metadata terminal propia durante la ventana de retención.
Un 404 de recuperación no demuestra rollback: se conserva el cuerpo/UUID congelado
para consultar o reintentar explícitamente. Conversación y turno tienen revisiones distintas.
PostgreSQL conserva `AiConversations`, `AiAssistantTurns`, `AiAssistantToolExecutions` y
`AiAssistantRequestReceipts`, agregadas por `20261004164813_AddAiAssistant`. Sus índices impiden
dos operaciones activas por actor o conversación; FK compuesta protege pertenencia.

Pregunta, respuesta, evidencia y resultados de herramienta usan AES-GCM con propósito derivado
`assistant-content:v1`; AAD incluye instalación, keyId, propietario, conversación, turno,
propósito y revisión de payload. HMAC usa `assistant-request-fingerprint:v1`. No se registran
textos, secretos, argumentos, continuación ni cabeceras del proveedor. Sin clave histórica
correcta falla la lectura: no se regenera el contenido. El navegador guarda sólo su estado en
memoria; preguntas y operaciones no entran en MutationCache, storage ni URL.

### Fuentes y autorización

El catálogo neutral `ISystemQuerySource` aporta definiciones versionadas y proyecciones mínimas.
`ISystemQueryExecutor` crea un scope nuevo por llamada; cada fuente vuelve a comprobar actor y
permisos. Los lectores EF quedan en Infrastructure. No hay SQL, nombres de tablas, filtros
arbitrarios ni URL elegida por el modelo.

| Herramienta | Permiso adicional | Hechos ofrecidos |
| --- | --- | --- |
| `users.summary` | `users.read` | conteos completos de cuentas activas/inactivas visibles; sin datos personales |
| `roles.list` | `roles.read` | nombres, descripciones y conteos; máximo 50 filas, sin fingir completitud |
| `roles.permissions` | `roles.read` | un rol exacto por ID o nombre; permisos del catálogo instalado |
| `business.information` | `ai.business.read` | hechos confirmados, FAQ y horario local; máximo 50 filas, truncamiento explícito y sin deducir datos ausentes |
| `system.help` | sesión y `ai.assistant.use` al ingresar | ayuda curada; enlaces sólo hacia destinos autorizados |

La salida final es una única herramienta `assistant_answer` con kind, texto e IDs de evidencias
observadas. Se rechazan argumentos extra/duplicados, herramientas desconocidas, referencias
inventadas, respuestas factuales sin fuente y respuestas libres del proveedor. La UI presenta
texto y filas como texto seguro, y deriva enlaces de un catálogo local de destinos. La validación
estructural y de procedencia no constituye una prueba semántica de cada frase generada.

Cada resultado válido de herramienta queda registrado con CallId, estado, versión, instante y
resultado cifrado, incluso sin evidencia (por ejemplo, fuente no conectada). No se inventa un ID
de evidencia y el mismo CallId no se acepta otra vez en el intento.

Antes de enviar al proveedor, la conversación incorpora conservadoramente los permisos de todas
las fuentes ofrecidas. Esto puede bloquear más historial que registrar sólo fuentes usadas,
pero impide exponer una pregunta o contexto si el worker cae antes de guardar su evidencia.
Relecturas, cada herramienta, cada paso y publicación verifican permisos actuales. Si se pierde
uno requerido, se muestra metadata genérica bloqueada y se ocultan pregunta, respuesta y fuentes.
Quitar la conversación propia permanece disponible. Una conexión cambiada o pausada invalida
la continuación y la publicación; nunca se mezcla con otra credencial/revisión.

### Ejecución, fallas y mantenimiento

El coordinador técnico de Infrastructure abre scopes nuevos para claim, reserva de paso,
preparación, HTTP, herramientas y cierre. Servicios de escritura tienen una sola UoW; ejecución
no recibe UoW ni llama a esos servicios. Orden de locks: conexión → presupuesto de instalación
→ actor → conversación; no se mantiene ninguna transacción durante HTTP al proveedor.

Un claim durable fija intento, fence y deadline. Se reserva cada paso antes de enviarlo.
OpenAI Responses y Anthropic Messages usan origen fijo, credencial guardada, herramientas tipadas,
continuación privada y HTTP sin redirects, loggers ni reintentos facturables. Los nombres con punto
se adaptan a nombres de protocolo con guion bajo, sin cambiar IDs del catálogo. La respuesta final
se valida y se confirma antes de publicarse. Uso de tokens conocido se conserva también al fallar;
uso ausente queda desconocido, nunca cero. No hay precio ni presupuesto monetario calculado.

Cancelación pendiente termina inmediatamente; durante ejecución se observa desde scopes nuevos
y cancela el transporte. Una respuesta confirmada antes de cancelar conserva Completed. Una
operación Running vencida con paso reservado queda Uncertain y no vuelve a ejecutarse. Sin paso
externo, vence como Expired. Si se obtuvo respuesta y falla el commit, sólo se reintenta su
persistencia con el mismo fence, durante un plazo acotado; otra réplica puede reconciliar vencidas.

Cuotas de aceptación por día UTC, capacidad Pending/Running y unicidad activa se coordinan en
PostgreSQL entre réplicas. El mantenimiento reconcilia vencidas y purga contenido en lotes,
incluyendo conversaciones retiradas; la eliminación no habilita repetir UUID ni elude la cuota
del día. Las conversaciones retiradas se borran físicamente con sus turnos/herramientas cuando
todos sus turnos vencieron y no queda uno activo; hasta entonces su metadata permite recuperación
terminal sin contenido. Los comprobantes independientes vencen por su propia ventana. El lote
comparte un máximo total entre contenido, conversaciones y comprobantes. Las cuentas borradas
también retiran sus conversaciones y solicitan cancelación. Los valores iniciales 10/actor,
100/instalación, 4 Running y 30 días siguen **propuestos
para el piloto**, con configuración técnica validada. No hay perfiles de modelo habilitados por
defecto: listar o probar conexión no acredita herramientas/salida terminal/contexto suficiente.
[Configuración y runbook](../guides/configurar-ia.md#habilitar-el-asistente).

Métricas OpenTelemetry de aceptados del día confirmados, claims, pasos, resultados, duración
total/por herramienta, tokens informados, consumo desconocido, fallas de
persistencia/mantenimiento, Pending/Running, edad pendiente y atraso de purga no contienen
IDs ni contenido como etiquetas. La evidencia local y los pendientes de modelos/cuentas reales
se registran en la [revisión](../reviews/2026-10-04-inicio-asistente.md).

## Módulo y responsabilidades

`ArtificialIntelligence` vive en `Modules/ArtificialIntelligence` de Domain, Application,
Infrastructure y Api. No referencia WhatsApp ni recibe referencias del núcleo. Program compone
un registro por capa. Los permisos se suman con `IPermissionCatalogSource`; seed entrega los permisos
presentes a Admin y los cambios de rol conservan revisión/invalidation existentes. Las ocho tablas y sus
migraciones pertenecen a la cadena única. [Retirada](../guides/quitar-ia.md).

Domain define la conexión única, su revisión, el comprobante y errores. Application coordina
lectura, cambio y prueba, con `AiConnectionCandidatePolicy` y `AiConnectionChangeRecorder`.
Infrastructure encapsula EF, AES-GCM/HMAC y HTTP. `ILlmClient` consulta modelos y prueba una conexión; sus
dos adaptadores implementan `ILlmProviderClient`. `ILlmConversationClient` tiene el consumidor
concreto del asistente y adaptadores independientes para conversaciones con herramientas.
El front conserva features independientes; `app` compone Inicio, sin imports entre features.

## Contrato HTTP y permisos

La conexión usa ai.read/ai.manage. Información del negocio usa ai.business.read/ai.business.manage;
ninguno implica acceso al otro formulario ni uso de sugerencias en WhatsApp.

| Verbo/ruta | Permisos | Resultado |
| --- | --- | --- |
| GET `/api/ai/connection` | `ai.read` | proveedor, modelo, `hasCredential`, Disponible/Pausada, revisión y proveedores soportados |
| PUT `/api/ai/connection` | `ai.read` + `ai.manage` | comprobante con `requestId` y metadatos confirmados |
| GET `/api/ai/connection/changes/{requestId}` | ambos | comprobante propio; 404 si no está disponible |
| POST `/api/ai/connection/test` | ambos | revisión de partida y tokens de entrada/salida opcionales |
| POST `/api/ai/connection/models` | ambos | proveedor, revisión de partida e identificador/nombre de los modelos visibles para la clave |

Lectura sin fila devuelve revisión 0, OpenAI, modelo vacío y conexión pausada, sin escribir.
Los cuerpos son contratos HTTP mapeados a mano; `ToString` oculta claves. No hay endpoints
alternativos ni versiones. 401 sin sesión, 403 sin permiso, ProblemDetails localizado con
`code` y `traceId`; validaciones por campo en camelCase. La prueba no devuelve texto generado,
errores crudos, request IDs del proveedor, cabeceras ni secretos. Los logs de operación no
incluyen argumentos y los loggers de los dos clientes HTTP están desactivados.

Cambiar proveedor con `Keep` y una clave existente falla: nunca se envía una clave guardada
a otro proveedor. Modelo: identificador explícito de hasta 150 caracteres, sin selección
automática. El catálogo remoto permite elegirlo sin escribirlo. Clave: hasta 4096 caracteres ASCII imprimibles, sin espacios
ni saltos de línea. `Replace` exige clave; `Keep`/`Remove` exigen clave nula. `Remove` pausa.
Disponible exige modelo y credencial; la prueba es manual y puede ejecutarse en Pausada.

### Catálogo de modelos — 2026-10-04

La consulta admite proveedor, `Keep`/`Replace`, clave candidata opcional y revisión esperada;
no exige un modelo, no cifra la candidata, no guarda ni abre una transacción. Usa la misma
política de credenciales y revisión que la prueba: una clave guardada nunca se envía a otro
proveedor. El POST local mantiene la clave fuera de URL; el proveedor recibe un GET autenticado
a `/v1/models`, en los mismos orígenes fijos y clientes sin redirects, loggers ni reintentos.

OpenAI devuelve `data`; Anthropic agrega `display_name` y paginación `has_more`/`last_id`.
Se recorren páginas de 100 con `after_id`, hasta 20 páginas y 2000 modelos. Identificadores
compatibles con el contrato del modelo (150 caracteres), nombres de hasta 200, sin duplicados
y ordenados por ID. JSON inválido, cursor repetido o lista truncada fallan; no se anuncia una
lista parcial como completa. Se conserva el máximo de bytes por respuesta y el plazo total
de 15 segundos, compartiendo los dos slots con las pruebas. Rate limit local de escritura
(20/min por operador). Timeout/red de catálogo devuelve indisponibilidad, sin incertidumbre
facturable: listar no solicita generación. No se consulta desde readiness ni al guardar.

El editor carga automáticamente con la clave guardada y ofrece Cargar/Actualizar modelos
para la candidata o una nueva consulta. La lista queda sólo en el editor activo, sin claves
en QueryCache/MutationCache, storage ni URL. Cambiar proveedor, clave o revisión la invalida
y cancela/ignora respuestas tardías. Modelo/estado editados conservan la lista. Un 429 impide
repetir durante el cooldown; no se reintenta solo. Los errores siguen los canales comunes y
conservan clave y borrador. Sin ai.manage no consulta ni ofrece acciones de catálogo.

El selector no elige automáticamente ni reemplaza un modelo guardado fuera de la lista;
permite entrada manual para alias, claves sin permiso de listado o indisponibilidad del catálogo.
La disponibilidad del catálogo no acredita compatibilidad con Responses/Messages, cuota ni
generación de texto: Probar conexión sigue verificando el modelo elegido con el pedido acotado.
Fuentes: [OpenAI Models](https://developers.openai.com/api/reference/resources/models/methods/list)
y [Anthropic Models](https://platform.claude.com/docs/en/api/models/list). Pruebas con catálogos
y claves ficticias; acceso real de las cuentas continúa pendiente.

## Guardado, concurrencia y recuperación

PUT usa `ExpectedRevision` y un UUID `RequestId` por intento lógico. Valida antes del único
`IUnitOfWork.ExecuteInTransactionAsync(..., OnSuccess)`. Dentro toma el advisory lock
`ai-connection:installation`, consulta comprobante propio, comprueba revisión, cambia la
conexión y agrega comprobante. El índice único de Scope=1 evita dos conexiones. Ambos datos
se confirman o deshacen juntos; el interceptor completa auditoría. No hay HTTP dentro del lock.

Un reintento idéntico devuelve el mismo comprobante, incluso si hubo cambios posteriores;
un payload distinto con el mismo identificador devuelve 409. La igualdad usa HMAC-SHA256
con clave derivada, incluyendo proveedor, modelo normalizado, operación de clave, clave
candidata, estado, revisión y UUID. El comprobante guarda digest/keyId, operador, metadatos
históricos y fecha UTC; nunca clave ni ciphertext. Su ventana de recuperación es de **30 días**.
Lecturas omiten vencidos y el siguiente guardado elimina vencidos, dentro del mismo límite.
No hay worker ni caché de credenciales. El UUID no se reutiliza para otro cambio.

Ante pérdida de respuesta, red o 5xx, el editor congela el pedido y consulta su UUID. Un 404
**no demuestra rollback**: permite consultar de nuevo o reintentar el mismo cuerpo/UUID.
Un conflicto obliga a leer/revisar; el siguiente cambio usa otro UUID. La revisión evita
reaplicar un pedido vencido contra una configuración posterior. Confirmado el comprobante,
se limpia la clave y se lee la configuración actual; si otro operador cambió después, se
muestra su revisión con un aviso explícito. No se compara una clave oculta.

## Cifrado y operación

La información del negocio no contiene credenciales. Sus textos son datos de negocio en PostgreSQL;
no se cargan claves, códigos ni información personal de clientes en ese formulario. Los contratos
HTTP, modelos y entidades redactan su ToString, y los logs no incluyen textos.

AES-256-GCM con nonce aleatorio de 96 bits y tag de 128 bits. Envelope versionado con keyId;
AAD fija versión, instalación, keyId, propósito y proveedor: cambiar la metadata del proveedor
no permite descifrar la misma clave para otro destino. HMAC usa HKDF-SHA256 y otro propósito.
La base contiene ciphertext; el anillo y `InstallationId` se configuran **fuera de PostgreSQL**,
iguales en las réplicas y diferentes en una instalación independiente. Startup valida el anillo
aunque aún no exista clave del proveedor. No se reutiliza Data Protection almacenado en la base.
Perder una clave histórica vuelve ilegibles sus credenciales y comprobantes: no hay fallback
ni regeneración automática. [ADR 0014](../decisions/0014-credenciales-de-ia-fuera-de-la-base.md)
y [configuración, rotación/restauración](../guides/configurar-ia.md).

## Prueba acotada

Prompt fijo `Reply with the single word OK.`, sin conversaciones ni datos del negocio.
OpenAI usa POST `https://api.openai.com/v1/responses`, Bearer por pedido, `store:false`,
sin streaming/herramientas. Anthropic usa POST `https://api.anthropic.com/v1/messages`,
`x-api-key` y `anthropic-version:2023-06-01`, mensaje user. No hay URL arbitraria ni redirects.
Salida máxima: 128 tokens por defecto; timeout total 15 s; respuesta máxima 64 KiB; dos
pruebas simultáneas por proceso, sin cola. Todo es configuración técnica validada.
Los handlers de resiliencia globales se retiran: un POST facturable se intenta **una vez**.
Timeout/red posterior al envío produce `TestUncertain`; el proveedor podría haber cobrado.

401/403 se normalizan a clave rechazada; 404/modelo incompatible, cuota, 429 y disponibilidad
tienen códigos propios. JSON inválido, incompleto o salida vacía nunca acreditan la prueba.
Uso ausente/inválido se omite. Un modelo de razonamiento puede consumir su tope sin texto;
eso devuelve prueba incompleta, no éxito ni un aumento automático del costo.

Rate limit HTTP por operador: lectura 120/min, escritura 20/min, prueba 4/min, sin cola.
`UseRateLimiter` corre después de autenticación; el acceso anónimo conserva políticas por IP.
Estos límites y el semáforo son **por réplica**, no un presupuesto global ni una cuota del negocio.
Para varias réplicas el límite agregado crece; el presupuesto se configura en el proveedor.
No se llama al proveedor desde readiness ni al leer/guardar.

## Información del negocio — 2026-10-03

Página productiva independiente en Administración → Configuración → Información del negocio,
`/configuracion/negocio`. El usuario eligió promover la maqueta de siete campos; el bootstrap,
datos y selector de escenarios siguen fuera del producto. Guardar registra hechos confirmados
sin llamar al proveedor. Desde el 2026-10-04, la fuente tipada `business.information` los usa en Inicio
sólo ante una consulta autorizada; no activa sugerencias ni atención automática en WhatsApp.

| Campo | Máximo de caracteres |
| --- | --- |
| Nombre | 120 |
| Descripción | 1000 |
| Horario | 500 |
| Ubicación/zona | 500 |
| Productos/servicios | 2000 |
| Políticas/condiciones | 2000 |
| Preguntas/respuestas de texto libre | 4000 |

Nombre y al menos un hecho confirmado (texto anterior o detalle estructurado) son obligatorios. Se recortan espacios externos,
sin inventar hechos, datos iniciales, stock ni precios. No hay fuentes/documentos/RAG, publicación
editorial ni historial de textos. La información permanece hasta ser modificada, retirada o
restaurada desde un backup; los comprobantes no conservan copias de los hechos.

| Verbo/ruta | Permisos | Resultado |
| --- | --- | --- |
| GET `/api/ai/business-information` | `ai.business.read` | siete campos, `details` opcional y revisión; vacíos/revisión 0 sin fila, sin escribir |
| PUT `/api/ai/business-information` | lectura + `ai.business.manage` | `requestId` y revisión confirmada, sin devolver texto histórico |
| GET `/api/ai/business-information/changes/{requestId}` | ambos | comprobante propio; 404 si ausente/vencido |

No dependen de ai.read/ai.manage ni de WhatsApp. Seed Admin incorpora los permisos del catálogo
instalado al arrancar e invalida los roles actualizados. El front filtra menú/ruta/acciones y el
servidor autoriza cada petición. Lectura no ofrece Guardar/Descartar; sin lectura no revela hechos.
MVC, ProblemDetails es/en, rate limit existente de lectura/escritura y cuerpo limitado a 256 KiB.
Los contratos/modelos/entidades redactan ToString; logs sin hechos ni secretos.

`AiBusinessInformation` es auditable y tiene Scope=1 único y revisión de concurrencia.
`AiBusinessInformationService.UpdateAsync` valida antes del único límite OnSuccess; dentro
toma sólo `ai-business-information:installation`, deduplica operador/UUID, compara revisión,
actualiza datos y crea `AiBusinessInformationChange`. Commit/rollback incluyen ambas filas.
No invoca LLM, no toca AiConnection, no usa caché ni HTTP dentro de la transacción.

El comprobante guarda sólo operador, UUID, revisión, HMAC/keyId y fecha UTC. La huella incluye
los siete campos normalizados, revisión esperada y UUID; cuando hay `details`, agrega el marcador
`details:v1` y sus valores normalizados. Sin `details` conserva exactamente la huella anterior,
para recuperar pedidos históricos. HKDF tiene propósito propio
`ai-business-information-fingerprint:v1`, separado de los comprobantes de conexión. Usa el
mismo anillo externo de la instalación; rotar conserva claves históricas. Ventana de 30 días,
lecturas omiten vencidos y un guardado exitoso los elimina. Una copia del backup sin ese anillo
permite leer hechos, pero no verificar reintentos históricos; no se inventa un resultado.

Mismo pedido devuelve el comprobante original aun tras cambios posteriores. Otro cuerpo con
igual UUID da 409. Revisión vieja da 409 y requiere revisión explícita. El editor compara valores,
incorpora hechos ajenos no editados y conserva ediciones propias sólo por elección, sin guardar
automáticamente. Un 5xx/red bloquea edición/Guardar/Descartar y conserva cuerpo/UUID en el editor
activo. Un 404 no demuestra rollback: consultar otra vez o repetir explícitamente el pedido
idéntico. Confirmado el comprobante, se lee la revisión actual; si cambió después, se avisa.
Salir/recargar pierde borrador e identificador local; ConfirmDialog advierte al navegar.
No hay retries automáticos, almacenamiento local ni drafts en QueryCache/MutationCache.

Los hechos confirmados sí pueden estar en QueryCache de la sesión; el transporte y su limpieza
son compartidos. Page, cinco Surface y campos reales mantienen Campo/Public Sans en es/en.
[Evidencia, checks y límites](../reviews/2026-10-03-informacion-del-negocio.md).

### Editor guiado y evolución del contrato

El usuario aprobó el editor guiado el 2026-10-03. `details` contiene `location`, `openingHours`
y `faq`, con contratos HTTP propios y mapeo manual. Se guarda en una columna JSONB nullable
de la misma entidad, dentro del límite transaccional existente; no necesita nuevas tablas,
permisos ni servicios. Omitir/null en un PUT conserva los detalles actuales. Para vaciarlos se
envían los grupos completos en estado sin definir; siguen aplicando nombre y hecho mínimo.
Los clientes anteriores conservan además la validación de sus campos de texto.

- Ubicación: `unknown/local/area/both`, dirección/referencia/zona (500 caracteres), PlaceId
  opcional (hasta 1024 ASCII imprimibles sin espacios) o coordenadas históricas completas,
  finitas/en rango a seis decimales; nunca ambos. Maps URL se deriva en destino fijo, sin aceptar
  enlaces arbitrarios. Editar dirección quita la selección. Desde el 2026-10-04 el selector
  consulta Places por nombre/ciudad y abre la ficha completa; elegir prepara el borrador y
  Guardar confirma. No importa direcciones de Google ni solicita geolocalización del dispositivo.
- Horario: siete días, lunes=0 a domingo=6; `unknown/open/closed/allDay`. Hasta cuatro franjas
  por día con HH:mm cada 15 minutos y `nextDay` explícito para cruzar medianoche. Sin intervalos
  vacíos, mayores a 24 horas ni superpuestos, incluido domingo/lunes. Zona IANA válida o UTC,
  inicializada desde la presentación general sólo para un horario nuevo. Se guarda de forma
  independiente. Hasta 32 excepciones DateOnly únicas (1900–9998), que reemplazan el día semanal;
  también se valida su convivencia con cierres del día anterior/siguiente. No inferir feriados.
- FAQ: hasta 20 entradas con UUID único, pregunta obligatoria de 200 caracteres y respuesta
  opcional de 1000. Catálogo inicial editable y preguntas propias; ninguna respuesta precargada.
  Sólo pares completos cuentan como hechos confirmados. Guardar una pregunta incompleta no
  inventa una respuesta ni activa generación.

Los conflictos combinan ubicación, horario y FAQ como grupos independientes y atómicos;
dirección/punto/enlace nunca se mezclan por separado. Las listas se copian y normalizan antes
de congelar el pedido. Errores localizados usan `details.location`, `details.openingHours` y
`details.faq`, con foco en el grupo y sin duplicar el aviso operativo.

`20261003215957_AddGuidedAiBusinessInformation` sólo agrega la columna nullable. Conserva
IDs, textos, revisiones, auditoría y comprobantes anteriores; no interpreta texto libre como
horarios, puntos ni FAQ. El editor muestra los textos anteriores no vacíos para revisión manual.
En Production, ejecutar el bundle antes de activar la nueva imagen/front; Development migra
al iniciar. No se aplicó esta migración a una base del usuario durante las pruebas.

El selector Leaflet/OSM anterior se retiró de este formulario al integrar Places (2026-10-04).
LocationMap permanece como pieza compartida, sin sugerencias de Google. La revisión guiada
registra la etapa anterior.

Históricamente, las teselas se solicitaban desde el navegador sólo al abrir el diálogo. Atribución OSM visible,
caché normal del navegador y referrer por imagen limitado al origen; no cambia la política
global de la API. Sin precarga/offline/proxy de teselas. `VITE_BUSINESS_MAP_TILE_URL` es una
plantilla pública de build opcional (nunca una clave secreta); el valor predeterminado es
`https://tile.openstreetmap.org/{z}/{x}/{y}.png`. OSM no garantiza disponibilidad: ante falla
hay un único aviso y siguen disponibles las coordenadas manuales. Para otro proveedor se deben
adaptar atribución/licencia/topes y validar su acceso; no basta sustituir una URL con clave.
Fuentes revisadas: [Leaflet 1.9.4](https://leafletjs.com/reference.html),
[política de teselas OSM](https://operations.osmfoundation.org/policies/tiles/) y
[Maps URLs](https://developers.google.com/maps/documentation/urls/get-started).
[Verificación guiada](../reviews/2026-10-03-informacion-del-negocio-guiada.md).

### Búsqueda de lugares — 2026-10-04

POST `/api/ai/business-information/places/search` requiere ambos permisos del negocio,
4 KiB de cuerpo, política ai-places (60/min por operador), no-store e inventario/OpenAPI.
Input 3–200 caracteres sin controles y cultura de la petición; no guarda, no abre transacciones
ni usa la conexión LLM. Servicio → puerto → GooglePlacesClient dentro del módulo independiente.

Autocomplete (New), origen fijo, clave de servidor externa y field mask sólo placeId/text.
Sin redirects/loggers/retries/Details/caché, cuatro slots sin cola, cinco segundos, 32 KiB,
máximo cinco sugerencias. Etiquetas sólo en memoria con atribución Google Maps; persistir
únicamente el ID permitido y hechos manuales. Front cancela/ignora búsquedas superadas,
conserva cooldown y no repite al vencer. El enlace abre la ficha completa con query_place_id.

PlaceId es opcional en JSONB, sin migración. La huella conserva details:v1 desplegado y agrega
place:v1 sólo si hay ID; conserva comprobantes históricos. Cambiar ID participa del grupo
atómico de ubicación y deduplicación existente.

Clave real, cuotas/alertas y Términos/Privacidad públicos siguen pendientes.
[Configuración](../guides/configurar-google-places.md) y [verificación](../reviews/2026-10-04-google-places.md).

## Verificación de conexión

Tres `Surface`, Page, FormField, NativeSelect, Input password, radios, ConfirmDialog y
notificaciones compartidas. Ambos idiomas. Sólo metadatos entran en QueryCache; operaciones
con claves usan funciones asíncronas del editor, sin MutationCache, persistencia local ni URL.
La clave se limpia al confirmar, descartar, cambiar proveedor, perder permisos o desmontar.
La navegación advierte sobre pérdida del borrador y un guardado todavía incierto.
La prueba pertenece al borrador/revisión y se invalida al cambiarlo, incluso por refetch;
no se guarda como badge permanente. Un 429 conserva cooldown visible y no repite solo.

Unitarios protegen reglas y coordinación. Integración usa PostgreSQL/Redis reales para
transacciones, dos hosts, permisos y comprobantes; cifrado real con claves inventadas.
HTTP de proveedores, OIDC/correos del navegador y fixtures responsive son simulados;
ninguna suite llama a API facturable. [Evidencia y pendientes](../reviews/2026-10-02-configuracion-llm.md).

## Panel interno e identificación del historial — 2026-10-05

Inicio `/` abre una conversación nueva; `/?conversacion=<id>` restaura sólo al elegir una guardada.
Abrir, plegar o paginar no escribe ni genera. La primera aceptación crea las filas con el límite transaccional existente.
El panel usa assistant-ui y muestra 20 conversaciones por página por defecto, ordenadas por actividad e Id.
No consulta respuestas ni herramientas para identificar cada fila. La UI conserva los guards de borrador,
operación activa e incertidumbre, QueryCache privado por cuenta/idioma/permisos y cooldown compartido.

List/detail obtienen el título de la primera pregunta cifrada, sólo con los permisos actuales y contenido vigente.
ReadPolicy evalúa una instantánea actual de permisos por request; ResponsePolicy comprueba vencimiento con TimeProvider
antes de descifrar con dueño/conversación/turno/revisión originales. ConversationTitlePolicy normaliza espacios y
limita a 80 grafemas Unicode. Bloqueo, purga o expiración devuelven el genérico localizado; corrupción devuelve
ContentUnavailable. Los modelos redactan ToString. No hay columna de título, otra llamada al LLM ni migración.

AiAssistantReader usa un LEFT JOIN del turno Ordinal=1 con pertenencia compuesta; el índice único evita duplicación.
La prueba Docker instrumenta dos consultas por página (conteo y filas), sin N+1, respuestas ni herramientas.
Esto verifica el recorrido del panel; capacidad y eliminación de metadata de conversaciones conservadas siguen
en la etapa independiente del [plan](../plans/2026-10-04-panel-conversaciones-inicio.md#7-capacidad-por-instalación-etapa-independiente-del-panel).
