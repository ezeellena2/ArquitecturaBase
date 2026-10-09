# Inicio con assistant-ui: plan de implementación entre proyectos

Fecha de revisión: 2026-10-04.
Estado: **implementación y verificación local completas en los dos repositorios; piloto pendiente**.
Este documento reemplaza las decisiones abiertas del estudio inicial y es el plan único de este desarrollo.

El usuario autorizó implementar el plan completo el 2026-10-04. La propuesta original se conserva
como procedencia, con sus valores de piloto aún propuestos. Las reglas implementadas se consolidan
en [Inteligencia artificial](../features/inteligencia-artificial.md#inicio-autenticado-con-consultas--2026-10-04),
[ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md) y la
[revisión de entrega](../reviews/2026-10-04-inicio-asistente.md). Las pruebas HTTP usan handlers falsos;
no acreditan acceso, gasto ni comportamiento de modelos reales de una cuenta.

## 1. Decisiones y alcance de la primera entrega

| Tema | Decisión de este plan | Estado |
| --- | --- | --- |
| Biblioteca | @assistant-ui/react, con ExternalStoreRuntime y estado externo confirmado por el servidor | Elegida por el usuario |
| Pantalla | Inicio autenticado en /; portada pública y caparazón existentes | Encaje con el pedido y el código |
| Primera capacidad | Consultar usuarios agregados, roles/permisos, información del negocio y ayuda curada | Alcance autorizado e implementado; ejemplos concretos abajo |
| Escrituras de negocio | Primera entrega de consultas; administrar desde las pantallas existentes | Fuera de esta entrega según el pedido de implementación |
| Historial | Privado por cuenta, persistido en PostgreSQL | Implementado y verificado localmente |
| Transporte inicial | MVC + JSON; aceptación 202 y consulta de estado cada 2 s mientras está activo | Diseño concreto para esta entrega |
| Presentación de respuesta | Progreso por etapas y respuesta completa validada con fuentes | Diseño concreto; no simular escritura de tokens |
| Texto progresivo del modelo | Otra etapa con contrato durable de eventos y revisión del transporte común | Fuera del alcance inicial; no prometerlo en esta entrega |
| Proveedores | OpenAI Responses y Anthropic Messages usando la conexión guardada de la instalación | Amplía el módulo existente |
| BBDD | Herramientas tipadas autorizadas detrás de lectores; EF sólo en Infrastructure | Regla obligatoria |
| MCP | No es una dependencia necesaria para las fuentes locales; adaptador posterior para una fuente externa concreta | Frontera prevista; no implementar un MCP genérico |
| Idiomas | es/en; español rioplatense; cultura efectiva del request capturada por turno | Contrato transversal |
| Reloj | TimeProvider, UTC persistido; presentación en zona del perfil | Contrato transversal |
| Retención | Contenido 30 días desde cada turno; eliminación manual inmediata de acceso; purga posterior | Valor inicial propuesto para el piloto |
| Límites | Tiempo, llamadas, bytes, concurrencia y cuotas de aceptación explícitos | Valores iniciales propuestos; sección 16 |

El laboratorio acreditó montaje y recorridos simulados. La implementación posterior agregó
durabilidad, autorizaciones, adaptadores de proveedores y publicación con las fases siguientes.
La evidencia local usa HTTP simulado; el piloto con un modelo exacto y credenciales del producto
sigue pendiente y se habilita sólo con un perfil calificado explícito.

No se abrirá una segunda arquitectura: permanecen MVC → interfaces de servicios → servicios
Application → puertos → Infrastructure, Result, validación previa y una UoW por método público que escribe.

## 2. Fuentes vigentes y estado existente comprobado

Leídas para esta revisión: AGENTS de ambos repositorios y docs; instrucciones del módulo IA
en las cuatro capas y de home/artificial-intelligence/app/shared del front; arquitectura canónica,
reglas de IA, mensajes, premisas visuales, migraciones y validación de un cambio.

Fuentes:
- [Backend](../architecture/backend.md).
- [Módulo IA](../features/inteligencia-artificial.md).
- [Validación de cambios](../guides/validar-un-cambio.md).
- [Front](../../../ArquitecturaBaseFront/docs/architecture.md).
- [Mensajes](../../../ArquitecturaBaseFront/docs/guides/mensajes-y-estados.md).
- [Diseño elegido y procedencia](../../../ArquitecturaBaseFront/docs/design/inicio-asistente-propuesta.md).
- [Premisas compartidas](../architecture/premisas-de-la-plantilla.md).

| Existente | Se reutiliza para | Límite real |
| --- | --- | --- |
| AiConnection + IAiConnectionReader | Proveedor/modelo/estado/revisión guardados | No descifra ni envía clave al navegador |
| AiConnectionCandidatePolicy.Reveal | Acceso a credencial del proveedor correcto | Generación sólo usa la guardada; no claves candidatas |
| ILlmClient + clientes actuales | Prueba y catálogo de modelos | No convertir esos métodos en generación |
| AiBusinessInformationReader | Hechos confirmados del negocio | No inventar ventas, stock ni clientes |
| IUserReader + IRoleReader + IPermissionService | Cuentas activas, roles y permisos efectivos | Proyecciones mínimas y autorización actual |
| IUnitOfWork + locks PostgreSQL | Aceptación, claim, progreso y cierre atómicos | Nunca sostener una transacción durante un HTTP al LLM |
| Sistema de colas durables existente | Referencia de commit antes de efectos externos | No usar QueuedDeliveries para conversaciones |
| SystemSettingsService.GetPresentationAsync | Componer presencia de capacidades instaladas | Componer fuera del valor de ajustes cacheado en Redis |
| HomeLayout + AppLayout + Page | Sesión, portada, menú, cabecera y márgenes | No remontar el caparazón al cambiar de conversación |
| shared/api/httpClient | OIDC, renovación única, Accept-Language, ProblemDetails | No fetch propio dentro de la feature |
| QueryClient + errorFeedback + notifications | Lecturas, clasificación, aviso único y recuperación | No segundo Toaster ni errores generales inline |
| shared/hooks/useCountdown | Esperas de 429 y reenvíos existentes | Hoy decrementa por tick; mejorar el hook común con regresiones |
| shared/lib/dateTime | Fecha/hora/día en zona del perfil | No formateadores por componente |
| useLanguagePreference + sincronización de perfil | Idioma de la interfaz | No selector de idioma propio del asistente |

El árbol está con cambios previos sin confirmar. El desarrollo debe revisar el diff por archivos,
conservar cambios ajenos, trabajar en main y no crear rama, commit ni push sin el pedido aplicable.

## 3. Arquitectura completa y dueño de cada responsabilidad

~~~mermaid
flowchart LR
  UI["Inicio · assistant-ui"] --> A["Adaptador de estado de la feature"]
  A --> H["httpClient + TanStack Query"]
  H --> C["Controllers MVC"]
  C --> S["Servicios Application"]
  S --> P["Lectores / repositorios"]
  P --> DB["PostgreSQL"]
  W["Worker Infrastructure · scopes nuevos"] --> S
  W --> E["Ejecución Application · sin guardado"]
  E --> L["Puerto de conversación LLM"]
  L --> O["Adaptadores OpenAI / Anthropic"]
  E --> Q["Fuentes de consulta del sistema"]
  Q --> P
  S --> AU["Permisos y cuenta actuales"]
~~~

1. **assistant-ui**: composición de hilo/compositor, foco, scroll y traducción de nuestro estado a su UI.
2. **Feature del front**: borrador activo, intento lógico, transporte existente y adaptación de respuestas.
3. **Api**: binding, permiso HTTP, cultura resuelta, contratos, rate limit y Result a HTTP.
4. **Application**: propiedad, validación, reglas, contexto, herramientas, evidencias y límites funcionales.
5. **Domain**: entidades, estados, invariantes y errores, sólo BCL.
6. **Infrastructure**: EF, cifrado, proveedores, scheduler y coordinación técnica entre scopes.
7. **PostgreSQL**: verdad durable de conversaciones, intentos, reservas y resultado.
8. **Redis**: mecanismos existentes de permisos/ajustes; no almacén alternativo del chat.

La clave y el protocolo del proveedor no cruzan el borde HTTP. El formato de assistant-ui
no es el esquema de las tablas ni el contrato de la API.

### Coordinación y transacciones: regla especialmente importante

Un servicio Application **no llama al método de escritura de otro servicio**.
Por eso el coordinador del worker pertenece a Infrastructure y organiza estas unidades:

- claim en un scope;
- preparación de contexto sin escrituras en otro scope;
- autorización/reserva de un paso externo en otro scope;
- llamada al proveedor y resolución de herramientas sin transacción;
- registro de progreso o cierre en otro scope.

Cada método de escritura del servicio de despacho tiene exactamente una UoW.
El coordinador técnico atrapa el fallo de la unidad completa y puede abrir un scope nuevo;
el servicio dueño de la UoW no envuelve su límite en try/catch.
Los helpers terminan en Policy/Guard/Verifier/Recorder, son internal sealed y no guardan.

| Punto de entrada | Métodos públicos previstos | Guardado |
| --- | --- | --- |
| IAiAssistantService | BootstrapAsync, ListConversationsAsync, GetConversationAsync, ListTurnsAsync, GetTurnAsync | Lecturas sin UoW |
| IAiAssistantService | SubmitAsync, CancelAsync, RemoveConversationAsync | Una UoW OnSuccess por método; validación previa |
| IAiAssistantDispatchService | ClaimAsync, BeginProviderStepAsync, RecordProgressAsync, CompleteAsync, FailAsync | Una UoW OnSuccess por método, estado/fence y locks |
| IAiAssistantExecutionService | PrepareAsync, CallProviderAsync, AdvanceAsync | Ninguna UoW; no llama escritores de otro servicio |
| IAiAssistantRetentionService | ReconcileExpiredBatchAsync, PurgeBatchAsync | Una UoW OnSuccess por lote acotado |

Claim sin candidato devuelve éxito con valor opcional vacío; no convierte el estado ocioso en error.
Complete/Fail reciben un resultado ya obtenido, nunca una función que llama al proveedor.
Si una unidad debe confirmar transición terminal y comunicar un impedimento, devuelve el estado
confirmado como éxito de esa escritura; no inventar OnAnyResult sin una necesidad documentada.

## 4. Encaje entre home, IA, sesión y configuración

Crear app/AuthenticatedHome.tsx como composición del Inicio privado:

1. Esperar recuperación de sesión y perfil mediante los mecanismos actuales.
2. Leer la proyección pública de presentación usando useSystemPresentation.
3. Si falla esa lectura: recuperación explícita; un 5xx no se interpreta como módulo ausente.
4. Si la capacidad assistant está instalada y la cuenta tiene ai.assistant.use: montar
   la página IA de forma diferida.
5. Sin módulo o sin permiso de uso: montar DashboardPage, conservando Inicio utilizable.
6. La ruta pública continúa resolviéndose desde HomeLayout; no consultar IA sin sesión.

El núcleo define IClientCapabilitySource, con un identificador genérico. IA aporta assistant
mediante AiAssistantClientCapabilitySource y TryAddEnumerable.
SystemSettingsService compone InstalledFeatures después de FindPresentationAsync,
con una copia del record; no modifica el objeto cacheado ni agrega IA a la consulta EF del núcleo.
Identificadores duplicados fallan al componer, independientemente del orden de DI.

SystemPresentationResponse incorpora una propiedad init no nullable con valor vacío inicial.
Eso conserva constructores/proyecciones existentes y lectura de entradas Redis anteriores.
La presencia instalada no contiene disponibilidad, modelo, credencial ni permisos de una cuenta.
La disponibilidad actual se consulta en el bootstrap autenticado.

Extraer la orientación de cuenta sin correo a auth/components/AccountRecoveryNotice.tsx.
DashboardPage y AiAssistantPage la consumen desde auth, sin importarse entre features.
Conservar /perfil, traducciones y condición vigente.

Quitar IA exige retirar su aportante/registro, la página y su import de composición del front,
sus pruebas/recursos y generar la migración de retirada. Home y núcleo siguen compilando.

## 5. Consultas iniciales: contratos cerrados y fuentes reales

| Pregunta de ejemplo | Tool interna | Permiso de fuente | Lectura / datos permitidos |
| --- | --- | --- | --- |
| ¿Cuántos usuarios activos e inactivos hay? | users.summary | users.read | Contadores completos, sin nombres/correos/teléfonos |
| ¿Qué roles hay? | roles.list | roles.read | ID, nombre, descripción y cantidad; catálogo acotado |
| ¿Qué permisos tiene Soporte? | roles.permissions | roles.read | Búsqueda por nombre exacto/ID, lista de permisos traducibles |
| ¿Qué horario o dirección tenemos cargados? | business.information | ai.business.read | Hechos confirmados con revisión; sin credenciales ni datos de clientes |
| ¿Cómo asigno permisos a un rol? | system.help | ai.assistant.use | Ayuda curada y enlaces condicionados por permisos actuales |

Alcance autorizado e implementado de primera versión: estas cinco preguntas y sus repreguntas.
Un rol ambiguo pide precisión. Una fuente ausente se informa como no conectada.
Cantidad cero, falta de datos, falta de acceso y fallo de infraestructura son estados distintos.

ISystemQuerySource es un puerto neutral en Application/Interfaces/Channels.
UsersSystemQuerySource, RolesSystemQuerySource y HelpSystemQuerySource viven en el núcleo;
AiBusinessSystemQuerySource vive dentro de IA. No hay referencias entre módulos.
Nadie registra una herramienta universal que acepte SQL, tabla, columna, URL, propietario o permisos.

### Qué contiene una definición y un resultado

- ID estable y versión; descripción y esquema de argumentos.
- Permisos requeridos; límites de filas/bytes y significado de un resultado vacío.
- Argumentos tipados: status de catálogo, ID/nombre de rol o tema de ayuda.
- Actor y alcance definidos por servidor, nunca por JSON del modelo.
- Resultado tipado, estado completo/truncado y explicación del alcance.
- Evidencia con ID generado por servidor, observedAtUtc, revisión cuando existe y destino lógico.
- Enlaces construidos por el front desde destinos admitidos; el modelo sólo cita IDs de evidencia.

users.summary agrega FindSummaryAsync a IUserReader y su implementación EF, con una
proyección nueva UserSummaryRow. No reutilizar CountByFilterOptionAsync como un total:
ese método responde otra pregunta y tiene semántica de filtros.
roles.list reutiliza IRoleReader con tope y orden; si el catálogo excede el tope, pide precisión.
roles.permissions usa una nueva FindByNameAsync normalizada/exacta en IRoleReader.
No toma una página visible como universo completo.

La ayuda es contenido de producto es/en, versionado y acotado; no leer AGENTS, código,
documentación de desarrollo o archivos del servidor como contexto runtime.
Sólo ofrecer enlaces cuya pantalla pueda abrir la cuenta.

## 6. Autorización, propiedad e historial sensible

Nuevo permiso: ArtificialIntelligencePermissions.AssistantUse = ai.assistant.use.
Se agrega a All y a los dos recursos del módulo. El seed Admin ya consume el catálogo
compuesto: comprobar entrega e invalidación, sin hardcodear IA en RoleSeeder.

- El actor HTTP sale de ICurrentUser. OwnerUserId no existe en el body.
- Submit, bootstrap, historial, estado, cancelación y eliminación exigen permiso de uso.
- Cada tool filtra su definición por permisos y autoriza otra vez al ejecutarse.
- Antes de cada paso externo y publicación se revalidan cuenta activa, permiso de uso,
  permisos de fuentes, conversación y conexión.
- El worker consulta IUserReader.FindByIdAsync + IPermissionService para el actor persistido;
  no construye un HttpContext ni acepta claims del modelo.
- Cuenta ajena, conversación retirada o ID inexistente devuelven 404 indistinguible.
- Sin sesión: 401. Sin permiso de uso: 403. Admin no recibe acceso automático al historial ajeno.
- Las consultas del núcleo conservan filtros globales de usuarios eliminados.

Guardar por conversación la unión de permisos/capacidades utilizados, incluso en turnos
fallidos si se llegó a consultar datos. Si la cuenta pierde alguno, el diseño conservador
**bloquea el contenido completo de esa conversación**: preguntas, respuestas y fuentes.
Evita mostrar datos repetidos por la persona en una repregunta.

El servidor permite recuperar metadata genérica para eliminarla o comenzar otra conversación,
pero no reenvía esa historia al proveedor. El front purga inmediatamente el contenido cacheado
al detectar pérdida de acceso. Una pestaña con datos viejos no es una autoridad de permisos.

Las comprobaciones usan el estado confirmado al momento de cada lectura; un cambio concurrente
posterior a una comprobación no permite prometer revocación retroactiva instantánea.
No se puede retirar contenido ya visto/copiado o ya enviado al proveedor.

## 7. Modelo durable y retención

Todas las entidades heredan Entity (Guid v7). Auditoría por interceptores; no asignar
las propiedades de fecha u operador de auditoría desde el caso de uso.
En un worker, ICurrentUser no tiene sesión HTTP: CreatedBy/ModifiedBy técnicos pueden ser null,
como permite el mecanismo actual. OwnerUserId y actor del intento son datos de negocio explícitos;
no falsear un HttpContext ni asignar auditoría manualmente para atribuirlos.

| Tabla / entidad nueva | Responsabilidad y principales campos |
| --- | --- |
| AiConversations / AiConversation | OwnerUserId, State Active/Removed, Revision, LastActivityAtUtc, RemovalRequestedAtUtc, RequiredPermissions, título genérico |
| AiAssistantTurns / AiAssistantTurn | ConversationId, RequestId, ordinal, estado/etapa/revisión, contenido protegido, cultura/zona capturadas, revisión/proveedor/modelo, attemptId, fence, deadline, cancelación, contadores/usage |
| AiAssistantToolExecutions / AiAssistantToolExecution | TurnId, ordinal, toolCallId, ID/versión, permisos, evidencia, observedAtUtc, resultado protegido, tamaño/duración/estado |
| AiAssistantRequestReceipts / AiAssistantRequestReceipt | OwnerUserId + RequestId, HMAC/keyId, aceptación UTC/día de cuota, conversationId/turnId, vencimiento y metadata mínima de recuperación |

Restricciones:
- Unique (OwnerUserId, RequestId) en recibos.
- Unique (ConversationId, ordinal) en turnos.
- Unique parcial de turno activo por conversación y otro por actor.
- Unique (TurnId, ordinal) para tools.
- FK de propiedad: el turno corresponde al propietario de la conversación; comprobar en servicio
  y usar una clave compuesta cuando corresponda para impedir asociaciones inválidas.
- Índices de Pending por creación/Id y de Running por deadline; historial por OwnerUserId,
  LastActivityAtUtc/Id; recibos por AcceptedDayUtc y propietario.
- Revision/fence participan del UPDATE condicionado de progreso/cierre.
- JSONB tipado/versionado sólo para colecciones propias; no esquema de una biblioteca frontend.

AiConversation usa State Removed como estado funcional de retirada, con filtro EF global;
no implementa ISoftDeletable porque el módulo necesita una purga física posterior.
Esto es una decisión explícita para estas entidades durables, no una excepción a interceptores.
La retirada cambia estado dentro de UoW y conserva auditoría.
Los repositorios de mantenimiento son los únicos que incluyen retiradas y usan Remove/RemoveRange
sobre entidades seguidas. No usar ExecuteDelete/ExecuteUpdate para eludir auditoría.
Los tests deben verificar filtros de conversaciones y turnos, no confiar sólo en el filtro del padre.

### Política propuesta

- Cada payload de turno/fuentes vence 30 días después de aceptación; una repregunta no extiende
  el vencimiento de textos anteriores.
- Si un turno necesario para el contexto expiró, se marca como retirado de la historia:
  no resumirlo automáticamente con otra llamada al modelo.
- Conversación vacía tras expiración queda con metadata genérica o se purga cuando corresponda.
- Borrar manualmente oculta todo al confirmar y pide cancelar turnos activos.
- Purga en lotes de 100 cada hora, también al arrancar; objetivo operativo de eliminación
  en vivo hasta una hora después del vencimiento/retirada, mientras el worker esté sano.
- El recibo mínimo conserva el UUID durante 30 días desde aceptación aunque se purgue el texto.
  Un intento ya retirado devuelve estado terminal, nunca vuelve a generar por reusar el UUID.
- Al vencer el recibo, el cliente de la app nunca reutiliza ese UUID. No prometer deduplicación eterna.
- La metadata que acredita cuota se conserva al menos hasta cerrar su día de aceptación.
- Borrado de cuenta cancela/retira su contenido; la lectura consulta el estado actual de la cuenta
  y la retención detecta propietarios borrados, sin tocar servicios del núcleo.
  Desactivación bloquea lecturas/ejecución, conservando la retención de 30 días; no purga sólo
  porque la cuenta esté temporalmente inactiva.
- Backups se rigen por su ventana operativa; purgar en vivo no reescribe backups.
- Purga y claim comparten locks/condiciones para impedir publicar contra contenido eliminado.

## 8. Cifrado, deduplicación y datos que salen del sistema

Reutilizar AiKeyRing externo con propósitos nuevos:
- assistant-content:v1 para preguntas, respuestas, evidencias y contexto persistido;
- assistant-request-fingerprint:v1 para HMAC de aceptación.

IAiAssistantContentProtector y IAiAssistantRequestFingerprint son puertos diferentes.
La AAD liga installationId, propósito, versión y entidad/turno; el envelope incluye keyId.
Usar el anillo existente no significa reutilizar una clave derivada o un propósito de credenciales.

Para comparar una repetición se usa el keyId del recibo existente, aunque haya rotado la clave
activa. La rotación conserva claves históricas hasta vencer contenido/recibos; faltar una clave
necesaria devuelve indisponibilidad, nunca autoriza regenerar para compensarlo.

La huella incluye actor + campos inmutables normalizados del body:
requestId, conversationId, expectedRevision, expectedConnectionRevision y pregunta.
Normalización estable documentada: trim externo y CRLF a LF; no reescribir contenido semántico.
Los valores de idioma/zona resueltos en la primera aceptación no se recalculan al deduplicar.
Repetir el body desde una interfaz que cambió de idioma recupera el intento original.

Las tools iniciales envían agregados, configuración del negocio y roles; no extraen
correos/teléfonos, códigos, credenciales ni mensajes de WhatsApp.
El texto libre del usuario puede contener información personal: no afirmar que una simple
regla de prompt garantiza detectarla. El piloto usa datos ficticios hasta revisar el alcance
de egress y condiciones de las cuentas proveedoras.

Logs/telemetría: IDs operativos, estado, códigos, duración, tamaños y usage conocido.
No prompts, preguntas, respuestas, resultados completos, encabezados ni argumentos MVC sensibles.
ToString redactado en contratos de entrada y tipos internos que llevan contenido/credenciales.
Cache-Control: no-store en las respuestas con contenido.

## 9. Recorrido de una pregunta y de una repregunta

1. Bootstrap autenticado obtiene disponibilidad, revisión actual, sugerencias permitidas y límites.
   No llama al LLM ni descifra una clave sólo para dibujar Inicio.
2. La persona escribe. El borrador pertenece al compositor activo, no a QueryCache/MutationCache.
3. Enviar crea un UUID una sola vez y congela body/revisiones mientras se conoce su aceptación.
4. SubmitAsync valida antes de UoW. Dentro toma locks, busca primero el recibo y deduplica.
5. Para una aceptación nueva: verifica dueño, revisiones, conexión, cuenta, permisos y cuotas;
   crea conversación si es la primera pregunta + turno Pending + recibo en la misma UoW.
6. Tras commit devuelve 202 con IDs, revisiones, serverNowUtc y pollingAfterSeconds.
7. Worker selecciona candidatos; ClaimAsync confirma Running con attemptId/fence/deadline.
8. Execution.PrepareAsync carga sólo historia legible y vigente y el catálogo de herramientas.
9. Dispatch.BeginProviderStepAsync registra que ese paso puede empezar, comprobando reserva,
   cancelación, deadline, cuenta, permisos y revisión de conexión. Confirma antes del HTTP.
10. Execution.CallProviderAsync hace una solicitud del proveedor fuera de UoW.
11. Execution.AdvanceAsync valida las llamadas de tools y consulta fuentes autorizadas.
    Devuelve resultado final candidato o contexto de siguiente paso + evidencias.
12. El coordinador llama RecordProgressAsync o CompleteAsync en un scope nuevo.
    Complete verifica referencias/estructura y acceso otra vez antes de confirmar.
13. GET de estado devuelve la respuesta confirmada; el front adapta los mensajes a assistant-ui.
14. Repreguntar agrega otro turno con nueva revisión/UUID. Una pregunta sobre el estado actual
    vuelve a consultar la fuente; la respuesta anterior no prueba cómo está la base hoy.

Los datos críticos (contadores, filas, permisos) son bloques derivados de evidencia tipada;
el modelo aporta explicación y referencias válidas. No reemplazar cantidades con cálculos
extraídos de un párrafo. Toda respuesta factual sobre el sistema requiere evidencia de ese turno.
Si falta evidencia válida, el verificador impide publicar una respuesta afirmativa.

### Contrato final del modelo

Además de las herramientas de datos, la ejecución agrega una herramienta terminal propia,
assistant_answer. Sus argumentos son kind (answer/clarification/unsupported), text y evidenceIds.
No contiene SQL, URL, datos de tablas, permisos ni instrucciones de sistema.
Su schema y versión pertenecen al módulo; argumentos adicionales/IDs ajenos se rechazan.
Los bloques de datos se arman desde las evidencias del servidor, no desde valores del modelo.

El adaptador normaliza esa llamada a una respuesta candidata tipada. La ejecución la verifica
y el coordinador la confirma sin una llamada extra al proveedor. Una salida libre que no cumple
el contrato no se publica ni dispara una reparación facturable automática.
Para answer factual se exige evidencia del turno; clarification/unsupported conservan estado
propio sin cifras afirmadas. El nombre técnico de la herramienta no se muestra en el producto.
El tope de seis tools cuenta consultas de datos; la llamada terminal no abre una consulta.

Eso no acredita que todo texto libre del LLM sea verdadero. Los ensayos de calidad y las fuentes
visibles son parte del criterio de cierre.

## 10. Máquinas de estado, carreras y fallos

~~~text
Pending ──claim──> Running ──resultado verificado──> Completed
   │                 ├──rechazo conocido─────────> Failed
   │                 ├──envío posible sin cierre─> Uncertain
   │                 ├──cancelación──────────────> Canceled
   │                 └──vence sin envío──────────> Expired
   ├──cancelación────────────────────────────────> Canceled
   └──vence antes de claim───────────────────────> Expired
~~~

Las etapas de Running son códigos: preparing, consulting, composing, verifying.
El UI las traduce; no contiene razonamiento interno ni payload crudo de tools.

| Situación | Comportamiento exacto |
| --- | --- |
| Mismo UUID/body | Recuperar recibo/turno aunque cambió disponibilidad o cuota; no cobrar una segunda aceptación |
| Mismo UUID/body distinto | 409 Ai.Assistant.RequestReused; conservar editor y consultar el intento |
| Dos pestañas envían | Lock + revisión + índices; una acepta y otra recibe conflicto/turno activo |
| Se pierde el 202 | Consultar GET por requestId; 404 no prueba rollback; repetir explícitamente body/UUID idénticos |
| Pending y reinicio | Se puede reservar en otro host: todavía no hubo generación |
| Running y caída | Tras deadline + gracia: Uncertain si algún paso pudo salir; no reejecutar generación |
| Proveedor responde, commit falla | Coordinador conserva resultado en memoria y reintenta sólo ese cierre en scope nuevo con igual fence |
| Worker tardío | UPDATE condicionado no publica; no puede iniciar otro paso sin reserva vigente |
| Conexión cambia antes de claim | Failed por revisión; nuevo intento exige releer y nueva aceptación |
| Conexión cambia entre pasos | No iniciar otro paso ni publicar con la anterior; conservar estado y consumo conocido |
| Se pausa IA | Frenar futuros pasos/publicación al comprobar la nueva revisión; no prometer abortar HTTP ya recibido por proveedor |
| Cuenta/permiso cambia | Bloquear siguiente paso/publicación y lectura de historia; limpiar contenido del front |
| Modelo devuelve límite/max_tokens | Failed con salida incompleta; no elevar tope ni continuar automáticamente |
| Error después de posible envío | Uncertain; uso desconocido no se informa como cero |
| Se cierra la pestaña | Worker sigue; reconstruir estado desde servidor al volver |
| Navegación cambia el hilo | Cancelar lecturas/ignorar respuesta tardía local; no cancelar por ello la operación durable |

### Cancelación

POST cancel es idempotente. Pending pasa a Canceled. Running recibe CancelRequestedAtUtc;
el worker lo observa antes de cada paso y la cancelación de HTTP es cooperativa.
Complete rechaza publicación si se confirmó la intención antes de su límite.
Si Completed ganó la carrera, cancel devuelve ese estado, sin anunciar una cancelación ficticia.
Cancelar no garantiza evitar consumo de un proveedor que ya recibió la solicitud.

Nueva conversación no cancela automáticamente: con una operación activa, ConfirmDialog ofrece
continuar en segundo plano o pedir cancelación. Un borrador se confirma antes de perderlo.
Repetir una generación Failed/Uncertain es una acción explícita con UUID nuevo; no habilitar
el callback genérico de regeneración de la biblioteca en esta entrega.

## 11. Locks, scopes y despliegue con varias réplicas

Claves nuevas en AiAdvisoryLockKeys y en AiTransactionContribution:
ai-assistant-budget:, ai-assistant-actor:, ai-assistant-conversation:.

Orden común de escritura:
1. ai-connection:installation, si la unidad necesita estabilidad de conexión;
2. ai-assistant-budget:installation;
3. ai-assistant-actor:<id>, ordenadas si hay más de una;
4. ai-assistant-conversation:<id>, ordenadas si hay más de una.

Purge no necesita lock de conexión; comienza por budget y conserva el orden restante.
No agregar locks del núcleo después de esta secuencia.
Validar la convivencia con escritores de conexión (que sólo toman el primero).

READ COMMITTED, sin retries automáticos de EF. Lecturas protegidas se hacen después del lock.
Claim confirma la reserva antes de generar; los límites de usuario/instalación cuentan en PostgreSQL.
Un semáforo local puede limitar recursos técnicos, pero no sustituye la reserva global.

Worker singleton sólo contiene scheduler/coordinador y opciones; servicios EF/Application scoped.
Crear/liberar un scope por unidad, no conservar DbContext por conversación ni compartirlo
entre herramientas. Tools secuenciales inicialmente.

Deadline total 90 s desde claim. Lease de mantenimiento vence 15 s después para reconciliar
workers muertos; no se renueva para extender el plazo del producto.
El coordinador limita HTTP/lecturas con el tiempo restante.
El cierre de resultado tiene hasta 5 s técnicos independientes del token de una lectura del navegador.
Si shutdown interrumpe, el estado durable conserva la posibilidad de resultado incierto.

## 12. Contratos HTTP concretos

Todas las rutas bajo /api/ai/assistant: MVC, HasPermission, inventario, OpenAPI,
rate limit y errores comunes. No agregar BackendPrefixes ni CORS.

| Verbo/ruta | Éxito | Servicio / semántica |
| --- | --- | --- |
| GET /bootstrap | 200 AiAssistantBootstrapResponse | Disponibilidad actual, revisión y sugerencias por permisos |
| GET /conversations | 200 PagedResult<AiConversationSummaryResponse> | Historial propio paginado, títulos genéricos |
| GET /conversations/{conversationId} | 200 AiConversationResponse | Metadata, permisos de lectura, revisión y turno activo |
| GET /conversations/{conversationId}/turns | 200 PagedResult<AiAssistantTurnResponse> | Historia propia legible, orden estable |
| POST /turns | 202 AiAssistantTurnAcceptedResponse | Aceptación atómica; ToAcceptedResult |
| GET /turns/{requestId} | 200 AiAssistantTurnResponse | Recuperación propia por UUID |
| POST /turns/{requestId}/cancel | 200 AiAssistantTurnResponse | Cancelación confirmada o estado terminal existente |
| DELETE /conversations/{conversationId} | 204 | Retirada y cancelación, Result sin valor |

~~~json
{
  "requestId": "uuid",
  "conversationId": null,
  "expectedRevision": null,
  "expectedConnectionRevision": 7,
  "question": "¿Cuántos usuarios activos hay?"
}
~~~

Primer turno: conversationId/expectedRevision nulos.
Repregunta: ambos obligatorios, con revisión confirmada.
Todos los demás campos se rechazan o no existen en el contrato: identidad, modelo, clave,
system prompt, mensajes previos, definiciones de tools, URLs o permisos.

Accepted contiene conversationId, requestId, conversationRevision, turnRevision, state,
stage, submittedAtUtc, serverNowUtc y pollingAfterSeconds.

TurnResponse terminal agrega answer y evidences, usage opcional y metadata de idioma/zona.
Failed/Uncertain se leen con HTTP 200: son estado de un recurso aceptado.
El campo failure contiene code estable, detail traducido y recuperación tipada, no Error/SDK serializado.
La causa operativa no se presenta como un mensaje generado por el asistente.

ConversationResponse y TurnResponse incluyen contentAccess (allowed/blocked/expired).
Si se revocó un permiso de fuente, se puede devolver metadata genérica propia con blocked,
pero question/answer/evidences se omiten; el lector no descifra esos payloads.
Cancel/delete propios siguen disponibles con permiso de uso. Un 403 por perder uso no se
transforma en ese 200: conserva el flujo de permisos compartido.

Bootstrap distingue available, paused, notConfigured, modelNotQualified y capacityLimited.
Configuración no es capacidad; un test OK no acredita tools/contexto/salida.
Incluir enlaces administrativos sólo si la cuenta puede abrirlos.

ProblemDetails de la petición:
400 campo/cuerpo inválido; 401 sesión; 403 uso; 404 propiedad/ausencia; 409 revisiones/deduplicación/
activo; 429 espera/cuota; 500 genérico con traceId.
Declarar errores Domain AiAssistantErrors y traducirlos en Errors.resx + Errors.en.resx.
429 conserva retryAfter común. No traducir del cuerpo crudo del proveedor.

Query de historial usa PagedRequest, validador común, SortableFields y desempate Id.
Página/tamaño/orden van por usePagination/useQueryUpdate cuando el historial está abierto.
No buscar preguntas o respuestas en URL.

Cancel toma el UUID de la ruta, sin cuerpo. Application recibe CancelAiAssistantTurnRequest
para validar el identificador y resolver actor/estado. No crear un HttpRequest vacío por simetría.

## 13. Idioma, zona horaria y timer transversal

### Idioma

- UI en locales/es/assistant.json y locales/en/assistant.json, i18next.
- Precargar assistant al resolver la hoja privada y namespace para evitar parpadeos.
- Todos los placeholders, labels, tooltips, estados y controles de assistant-ui pasan por t.
- Cambiar idioma usa useLanguagePreference del sistema; la página no escribe storage/perfil.
- httpClient envía Accept-Language. Api reutiliza LocalizationExtensions y captura la cultura
  ya resuelta en el modelo Application al mapear Submit; nunca lee un language del body.
- Guardar la cultura del turno. El worker configura explícitamente recursos/cultura dentro de
  un scope limitado y restaura la anterior: no confiar en la cultura inicial del BackgroundService.
- Prompt pide responder en es/en efectivo, con voseo en español. Etiquetas/control/resultados
  deterministas siempre salen de recursos, no de generación.
- Cambiar idioma no regenera ni retraduce respuestas históricas automáticamente. UI cambia;
  la próxima pregunta captura el nuevo idioma.
- Paridad de recursos/keys y tests de es/en, también cuando el header falta y aplica DefaultCulture.
- IDs, código, logs y excepciones en inglés.

### Fechas y zona

- Todos los instantes salen de TimeProvider.GetUtcNow().UtcDateTime.
- Props *Utc, JSON ISO 8601 con Z, UtcDateTimeConverter existente.
- Day de cuota es DateOnly derivado del instante UTC; no el día del navegador.
- TimeZoneId se toma del perfil/valor general permitido, no del body.
- El turno captura zona para interpretar períodos; UI muestra instantes en la zona actual del perfil.
- formatDateTimeInZone para fuentes/detalles, formatTimeInZone para mensajes y
  calendarDateInZone para separadores; no new Intl por componente.
- Períodos de consulta son [startUtc, endUtc), derivados desde fecha/zona local en servidor.
  Horarios/fechas especiales del negocio conservan su semántica local, no convertir DateOnly a UTC.
- Tests FakeTimeProvider y casos de fin de mes, cambio de zona, medianoche y DST donde aplique.

### Temporizadores

| Necesidad | Dueño | Regla |
| --- | --- | --- |
| Timeout de tool/paso/intento | Backend | Tiempo restante del deadline; TimeProvider inyectado |
| Scheduler/espera/reconciliación | Infrastructure | Task.Delay con TimeProvider y CancellationToken |
| Sondeo activo | TanStack Query | 2 s indicado por server, sin setInterval en componentes |
| Cooldown 429 | Hook común useCountdown | Duración de retryAfter; recomputar tras pestaña suspendida |
| Vencimiento/retención/cuota | PostgreSQL + TimeProvider | El reloj del navegador no autoriza |
| Duración de avisos | notifications.ts | Conservar 4/8/10 s y duración indefinida con acción necesaria |

Los tokens de timeout se crean con CancellationTokenSource(duración, timeProvider), ligados
al token de parada/cancelación de la unidad. Un CancelAfter con reloj global no sirve para
acreditar los tests con FakeTimeProvider. Para duración técnica, GetTimestamp/GetElapsedTime;
los instantes persistidos siguen siendo UTC. Un timer del transporte actual de prueba no se
reutiliza si conserva otro límite o un reloj no controlado.

Mejorar useCountdown **sin cambiar su API pública**: initialSeconds, seconds, isRunning, restart.
Calcular segundos restantes contra un deadline monotónico con performance.now; el timer
sólo despierta para recalcular, no cuenta ticks como tiempo transcurrido.
Limpiar al desmontar y recomputar al volver a una pestaña visible. Testear cero, restart,
suspensión, StrictMode y cleanup con reloj controlado.

Regresiones obligatorias de sus consumidores: LoginCodePage, LoginLinkPage, EmailCodeForm,
WhatsAppCodeForm y VerifyDestinationDialog (perfil/usuarios).
No cambiar las esperas del backend del ingreso ni migrar todas las features en este desarrollo.
Las cuentas regresivas locales actuales de configuración IA tienen un pendiente separado;
el asistente nuevo consume el hook común desde su primera versión.

## 14. assistant-ui y composición visual productiva

Dependencia productiva mínima: @assistant-ui/react, versión exacta revisada al promover
(la comparación validó 0.15.23). No sumar AI SDK, CopilotKit o ChatKit al producto.
El lockfile del laboratorio no se copia al raíz; instalar la dependencia elegida en package.json
y generar su package-lock con el gestor actual.

Runtime de la feature:
- ExternalStoreRuntime recibe proyección de turnos y isRunning del servidor.
- IDs estables: <turnId>:user y <turnId>:assistant; no aleatorios por render.
- onNew llama a la acción de aceptación propia.
- onCancel solicita cancelación durable; no sustituye el resultado por un mensaje local.
- No aportar onEdit/onReload/setMessages de branching/onAddToolResult ni cola de envíos.
- Montar un runtime por conversación; al cambiar, cancelar lecturas, limpiar el borrador por
  confirmación y reconstruir desde el servidor.
- La biblioteca no conserva una segunda lista de mensajes que compita con TanStack Query.

**Corrección visual vinculante tras el rechazo de la primera implementación:** la referencia son
las capturas originales `assistant-ui-empty/answer-*.png` en
`../ArquitecturaBaseFront/design-lab/history/home-assistant-comparison-2026-10-04/evidence`.
El tablero chosen-board recompuso el diseño elegido y no acredita una nueva aprobación.
Marco a toda el área disponible; columna de 704 px con 16 px de padding (672 px de contenido).
En vacío, bienvenida/editor centrados con sugerencias debajo. Con turnos, pregunta en burbuja
a la derecha, respuesta de lectura a la izquierda y compositor al pie. Editor con radio de 16 px
y envío por flecha, sin Surface/FormField/textarea de formulario dentro del chat ni panel punteado
de configuración por fuera. Disponibilidad compacta junto al editor; marco sin banda Conversación.
Primitivas Thread/Composer/Message, Button/IconButton y ActionTooltip compartidos; diálogos comunes.
ComposerPrimitive.Input tiene label accesible, ayuda de teclado oculta y error asociado. Los
tokens quedan en index.css y la representación en shared/ui/assistant. No ofrecer adjuntos ni
regeneración sin implementación. [Contrato visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md).

Los componentes de representación genérica quedan en shared/ui/assistant;
la feature aporta textos, fuentes, estados y acciones. Esa carpeta no importa features,
API, auth, provider ni Domain. El runtime/controlador específico se queda en la feature.
No copiar los 35 archivos vendor del comparador ni conservar sus exclusiones de lint en producto.
Los archivos propios pasan las reglas existentes; ampliar tests de arquitectura para permitir
la integración estrecha con los primitivos, sin permitir controles ad hoc.

Para esta entrega, cuerpo del modelo como texto y bloques tipados, sin HTML, ejecución de código,
imágenes remotas o enlaces arbitrarios. La dependencia de Markdown se añade sólo si se
incorpora un renderer acotado realmente requerido, con su política y pruebas.

### Estados y mensajes

| Estado/evento | Presentación | Dueño del aviso |
| --- | --- | --- |
| Vacío | Bienvenida + sugerencias autorizadas | Contenido |
| Carga inicial | Skeleton/Spinner comunes | QueryCache sólo si falla |
| Conexión pausada/no configurada | Condición durable y acción permitida | Contenido, sin toast por render |
| Pregunta inválida | Error asociado al compositor | Local; sin toast duplicado |
| Pending/Running | Etapa traducida + acción Cancelar + lector de pantalla | Estado de la operación |
| Completed | Respuesta y fuentes confirmadas; copiar | Contenido; no toast por cada polling |
| Fallo de GET con datos previos | Mantener hilo/borrador | QueryCache |
| Failed/Uncertain durable | Estado terminal del turno y recuperación; no falsa respuesta IA | Una notificación local por transición y UUID |
| 409 | Conservar body y recuperar/revisar conversación | Local |
| 429 | Cooldown y bloqueo visible | Local, un aviso común |
| 401 | Renovación común o login | Sesión |
| 403 | ForbiddenPage o bloqueo de contenido según recurso | Flujo compartido |
| Eliminar/cancelar/nueva con borrador | ConfirmDialog cuando hay consecuencia | Acción local |

Notificación estable por actor/requestId/transición; una relectura o StrictMode no vuelve a anunciar.
El estado terminal conserva una etiqueta funcional; la explicación operativa aparece en el aviso
con el canal común, sin repetir el mismo párrafo en una burbuja y un toast.

Enter agrega línea; Ctrl+Enter/Meta+Enter envían. Respetar composición IME.
Scroll anclado sólo si la persona estaba al final; no mover mientras revisa fuentes anteriores.
Historial en Dialog, foco al abrir/cerrar y etiquetas comunes.
1440/390/320 px, es/en, teclado móvil, contenido largo y reduced motion. Los diálogos mantienen
acciones touch de 44 px; la flecha del editor conserva los 30 px de la composición elegida.
Live region anuncia cambios de etapa/resultado sin releer toda la historia.

## 15. Transporte, caché y recuperación del SPA

Usar sólo shared/api/httpClient para todas las rutas.
Su renovación 401 puede repetir el POST con **body/UUID idénticos**; la deduplicación lo hace seguro.
La feature no repite un POST por red/5xx ni crea un UUID desde un callback de retry.
Acciones con preguntas como funciones del editor, sin variables sensibles en MutationCache.

Query keys: assistant + userId + conversationId/requestId + parámetros + idioma de metadata.
No preguntas/respuestas en keys. No persistir caché ni borradores en storage.
confirmed data en QueryCache de la sesión; gcTime acotado y cleanup al perder sesión/permisos.

GET activo:
- refetchInterval = pollingAfterSeconds * 1000; base 2 s.
- staleTime 0 para activo, false al llegar a terminal.
- refetchIntervalInBackground false; al volver a foco, relectura antes de otra acción.
- cancelación por signal de TanStack Query; ningún timer artesanal.
- Ante error operativo: parar el polling automático del turno hasta reintento explícito,
  manteniendo el dato anterior; no inundar el Toaster cada 2 s.
- Lecturas conservan los retries comunes de red; 429 respeta retryAfter y no insiste automáticamente.
- No repetir la generación cuando se agota un timeout local.

URL: /?conversacion=<uuid>. Sin conversaciones vacías persistidas antes de la primera pregunta.
Actualizar mediante useQueryUpdate; página/tamaño/orden del historial sólo mientras aplica.
Volver atrás restaura la conversación desde el backend.
No usar URL para pregunta, prompt, respuesta, fuente cruda, credencial o borrador.

## 16. Límites iniciales y configuración técnica

Valores para ensayar el piloto, pendientes de calibración con modelo/operación elegidos:

| Control | Propuesta de partida |
| --- | --- |
| Pregunta | 4000 caracteres, body 16 KiB; longitud documentada según validación .NET y prueba Unicode |
| Historia | Últimos 12 turnos autorizados completos; máximo 24.000 caracteres antes de sumar schemas |
| Contexto de proveedor | Tope serializado 128 KiB incluyendo schemas/historia/tools; límite de contexto según perfil de modelo validado |
| Pasos de proveedor | 4 máximo por intento; reserva y chequeo antes de cada HTTP |
| Tools | 6 máximo, secuenciales; 50 filas por resultado, 16 KiB por resultado y 64 KiB agregado |
| Salida | 1500 tokens por paso; incompleta falla; no incremento automático |
| HTTP del proveedor | 256 KiB de respuesta; continuación opaca hasta 128 KiB |
| Tool | 5 s, nunca más que el tiempo restante |
| Paso proveedor | 30 s, nunca más que el tiempo restante |
| Intento Running | 90 s desde claim |
| Pending | Hasta 120 s para claim; luego Expired sin generación |
| Lease/gracia reconciliación | Deadline + 15 s |
| Reintentar persistencia final | Hasta 5 s técnicos, resultado ya obtenido; ningún HTTP nuevo |
| Aceptaciones activas | 1 por actor y conversación; 50 Pending globales |
| Ejecuciones activas | 4 por instalación entre réplicas |
| Rate limit HTTP | Submit 6/min, lecturas 120/min, cancel/delete 20/min por operador/réplica |
| Cuota durable inicial | 10 intentos aceptados/día UTC por actor, 100 por instalación |
| Retención | Contenido y recibo 30 días; lotes 100, revisión cada hora |
| Workers | Sondeo 1 s, espera cancelable; no busy loop |
| UI | Estado cada 2 s, sin requests de generación por polling |

Cuotas se cuentan en recibos confirmados bajo lock y no se reinician borrando conversaciones.
Un reintento idéntico no consume cuota nueva. Un intento aceptado/cancelado sigue contando:
es una cuota de aceptación, **no una cuenta exacta de cobros**.
Máximo cuatro llamadas por intento; cambiar de día no cambia el límite de un intento activo.
Usage ausente = desconocido. No inventar precio ni anunciar garantía de gasto monetario.
Una reserva monetaria o token exacta necesitaría datos de precios/tokenización y otro contrato.

AiAssistantOptions valida límites técnicos al arrancar; AiAssistantLimits, modelo puro de
Application/Configuration, expone lo necesario a los casos de uso sin tipos de opciones Infrastructure.
No convertir esto en veinte ajustes del Inicio.

Modelo arbitrario de catálogo no acredita compatibilidad. Definir perfiles técnicos explícitos
(provider + model + contexto declarado + tools/salida comprobados) en configuración del host,
con pruebas representativas. Bootstrap marca modelNotQualified si falta perfil.
No crear un fallback silencioso ni un catálogo de capacidades adivinado a partir del nombre.
AiAssistantModelProfile pertenece a Configuration del módulo en Application; las opciones de
Infrastructure ligan/validan sus valores y entregan la proyección inmutable. Límites y perfiles
se registran con DI explícita, sin service locator en las reglas.

## 17. Adaptadores de proveedor y protocolos

Nuevo ILlmConversationClient separado de ILlmClient.
LlmConversationClient elige exactamente el proveedor guardado.
Clientes HTTP de generación propios reutilizan la política fija de transporte:
orígenes HTTPS fijos, sin redirects, loggers, retries, hedging ni claves en BaseAddress.
Compartir configuración técnica no altera timeout/tope/semáforo de prueba/catálogo.

OpenAiConversationClient:
- POST /v1/responses, Bearer por solicitud, modelo explícito y store:false.
- Tools function con strict:true y schemas completos compatibles.
- parallel_tool_calls:false; controlar IDs, argumentos y cantidad.
- Reenviar los output items necesarios del intento en orden, incluidos items de continuidad
  de razonamiento; no conservarlos como mensajes visibles del historial.
- No usar previous_response_id como fuente canónica ni depender del almacenamiento de proveedor.
- Distinguir respuesta completa, refusal, incompleta, tools y error; usage nullable.

AnthropicConversationClient:
- POST /v1/messages, x-api-key por solicitud y versión de API admitida.
- Separar system/messages; tools cliente definidas por servidor.
- tool_use y tool_result vinculados por ID/orden; resultado de tool como contenido, no instrucción.
- Respetar stop_reason: max_tokens no es éxito.
- No enviar thinking como respuesta ni registro visible.
- En caso de bloques de continuidad del protocolo, preservarlos sólo en memoria del intento
  y detrás del adaptador; no fabricar/desechar bloques necesarios.

El contrato Application expresa texto, solicitudes de tool, usage y estado, con un envelope
opaco acotado para continuidad técnica. No expone JsonDocument/SDK/tipos Infrastructure.
Ese envelope se usa dentro del intento; caída de Running no implica reconstrucción y reenvío.
Validar JSON, duplicados, respuesta grande, content types y motivos HTTP con handlers falsos.

store:false controla almacenamiento de Responses; no equivale por sí solo a ausencia
contractual de retención o ZDR. Antes de datos reales, revisar las condiciones de ambas cuentas.

## 18. MCP y protección frente a instrucciones en datos

Las instrucciones del usuario del producto, reglas del servidor y datos de fuentes se separan.
Un nombre de rol, una FAQ, una pregunta o un resultado de tool puede contener instrucciones
maliciosas: nunca cambia identidad, permisos, schemas, herramientas habilitadas o destino HTTP.

Prompt versionado del servidor:
- tarea/alcance;
- reglas de consulta actual;
- idioma;
- catálogo de herramientas autorizadas;
- resultados delimitados como datos;
- contrato final y referencias admitidas.

No basta un prompt contra inyección: la autorización y los límites se verifican fuera del LLM.
Probar resultados con “ignorá permisos”, tool inventada, SQL, URL externa y pedidos de secretos.

Si luego hay una fuente MCP necesaria: adaptador de Infrastructure, servidor/destino
concreto aprobado, autenticación propia, timeout/bytes, schema validado y mapping de permisos.
El modelo no elige una URL ni ejecuta cualquier tool anunciada por un servidor.
No instalar un MCP de acceso libre a PostgreSQL para resolver esta primera entrega.

## 19. Árbol de archivos: backend

Leyenda: **[N] nuevo**, **[M] modificar existente**, **[R] reutilizar sin cambio previsto**.
Raíz: C:/Users/ezequ/source/repos/ArquitecturaBase.
Los nombres nuevos son los previstos para la implementación; la revisión puede fusionar un
modelo/helper si no tiene responsabilidad propia, conservando los contratos de este plan.

~~~text
src/ArquitecturaBase.Domain/
├─ Modules/ArtificialIntelligence/
│  ├─ AiConversation.cs                                      [N]
│  ├─ AiConversationState.cs                                 [N]
│  ├─ AiAssistantTurn.cs                                     [N]
│  ├─ AiAssistantTurnState.cs                                [N]
│  ├─ AiAssistantToolExecution.cs                            [N]
│  ├─ AiAssistantRequestReceipt.cs                           [N]
│  ├─ AiAssistantErrors.cs                                   [N]
│  ├─ ArtificialIntelligencePermissions.cs                   [M]
│  └─ AGENTS.md / CLAUDE.md                                  [M AGENTS / R CLAUDE]
└─ Common/Entity.cs, IAuditable.cs                            [R]

src/ArquitecturaBase.Application/
├─ Interfaces/Channels/
│  ├─ IClientCapabilitySource.cs                             [N]
│  ├─ ISystemQuerySource.cs                                  [N]
│  └─ AGENTS.md                                             [M]
├─ Models/SystemQueries/
│  ├─ SystemQueryDefinition.cs                               [N]
│  ├─ SystemQueryContext.cs                                  [N]
│  ├─ SystemQueryRequest.cs                                  [N]
│  ├─ SystemQueryResponse.cs                                 [N]
│  ├─ SystemQueryEvidence.cs                                 [N]
│  └─ AGENTS.md / CLAUDE.md                                  [N]
├─ Channels/SystemQueries/
│  ├─ UsersSystemQuerySource.cs                              [N]
│  ├─ RolesSystemQuerySource.cs                              [N]
│  ├─ HelpSystemQuerySource.cs                               [N]
│  └─ AGENTS.md / CLAUDE.md                                  [N]
├─ Channels/AGENTS.md                                        [M]
├─ Models/Users/UserSummaryRow.cs                            [N]
├─ Models/Settings/SystemPresentationResponse.cs             [M]
├─ Interfaces/Persistence/IUserReader.cs                     [M FindSummaryAsync]
├─ Interfaces/Persistence/IRoleReader.cs                     [M FindByNameAsync]
├─ Services/Settings/SystemSettingsService.cs                [M composición capacidades]
├─ DependencyInjection.cs                                   [M fuentes neutrales]
├─ Modules/ArtificialIntelligence/
│  ├─ Interfaces/Services/
│  │  ├─ IAiAssistantService.cs                              [N API: bootstrap/read/submit/cancel/delete]
│  │  ├─ IAiAssistantDispatchService.cs                      [N worker: claim/step/progress/complete/fail]
│  │  ├─ IAiAssistantExecutionService.cs                     [N preparar/llamar/avanzar, sin guardado]
│  │  └─ IAiAssistantRetentionService.cs                     [N reconciliar y purgar]
│  ├─ Interfaces/Persistence/
│  │  ├─ IAiConversationReader.cs                            [N]
│  │  ├─ IAiConversationRepository.cs                        [N]
│  │  ├─ IAiAssistantTurnReader.cs                           [N]
│  │  ├─ IAiAssistantTurnRepository.cs                       [N]
│  │  └─ IAiAssistantRequestReceiptRepository.cs              [N]
│  ├─ Interfaces/Integrations/
│  │  ├─ ILlmConversationClient.cs                           [N]
│  │  ├─ IAiAssistantContentProtector.cs                      [N]
│  │  └─ IAiAssistantRequestFingerprint.cs                    [N]
│  ├─ Configuration/AiAssistantLimits.cs                     [N]
│  ├─ Configuration/AiAssistantModelProfile.cs               [N]
│  ├─ Models/Assistant/
│  │  ├─ SubmitAiAssistantTurnRequest.cs                     [N]
│  │  ├─ CancelAiAssistantTurnRequest.cs                     [N]
│  │  ├─ ListAiConversationsRequest.cs                       [N]
│  │  ├─ ListAiAssistantTurnsRequest.cs                      [N]
│  │  ├─ AiAssistantBootstrapResponse.cs                     [N]
│  │  ├─ AiConversationSummaryResponse.cs                    [N]
│  │  ├─ AiConversationResponse.cs                           [N]
│  │  ├─ AiAssistantTurnAcceptedResponse.cs                  [N]
│  │  ├─ AiAssistantTurnResponse.cs                          [N]
│  │  ├─ AiAssistantAnswerResponse.cs                        [N]
│  │  ├─ AiAssistantEvidenceResponse.cs                      [N]
│  │  ├─ AiAssistantClaim.cs                                 [N]
│  │  ├─ AiAssistantExecutionState.cs                        [N]
│  │  ├─ AiAssistantExecutionProgress.cs                     [N]
│  │  ├─ LlmConversationRequest.cs                           [N]
│  │  ├─ LlmConversationStepResponse.cs                       [N]
│  │  ├─ LlmToolCall.cs / LlmToolResult.cs                     [N]
│  │  └─ LlmContinuation.cs                                  [N opaco, acotado y redactado]
│  ├─ Validation/Assistant/
│  │  ├─ SubmitAiAssistantTurnRequestValidator.cs            [N]
│  │  ├─ CancelAiAssistantTurnRequestValidator.cs            [N]
│  │  ├─ ListAiConversationsRequestValidator.cs              [N]
│  │  └─ ListAiAssistantTurnsRequestValidator.cs             [N]
│  ├─ Services/
│  │  ├─ AiAssistantService.cs                               [N]
│  │  ├─ AiAssistantDispatchService.cs                       [N]
│  │  ├─ AiAssistantExecutionService.cs                      [N]
│  │  ├─ AiAssistantRetentionService.cs                      [N]
│  │  ├─ AiAssistantAccessGuard.cs                          [N]
│  │  ├─ AiAssistantContextPolicy.cs                        [N]
│  │  ├─ AiAssistantToolPolicy.cs                           [N]
│  │  └─ AiAssistantEvidenceVerifier.cs                     [N]
│  ├─ Channels/AiAssistantClientCapabilitySource.cs           [N]
│  ├─ Channels/AiBusinessSystemQuerySource.cs                 [N]
│  ├─ Resources/Assistant.resx / Assistant.en.resx            [N prompts/etapas]
│  ├─ Resources/AssistantTexts.cs                            [N acceso explícito por cultura]
│  ├─ Resources/Permissions.resx / Permissions.en.resx        [M]
│  ├─ ArtificialIntelligenceApplicationRegistration.cs       [M]
│  └─ AGENTS.md                                             [M]
├─ Resources/SystemHelp.resx / SystemHelp.en.resx             [N ayuda curada]
├─ Resources/Errors.resx / Errors.en.resx                     [M códigos]
├─ Resources/Validation.resx / Validation.en.resx             [M campos]
└─ Resources/ValidationMessages.cs                           [M acceso a claves]

src/ArquitecturaBase.Infrastructure/
├─ Persistence/Readers/UserReader.cs                         [M agregado]
├─ Persistence/Readers/RoleReader.cs                         [M rol exacto]
├─ Persistence/Readers/SystemSettingsReader.cs               [R cache sólo ajustes]
├─ Modules/ArtificialIntelligence/
│  ├─ AiAssistantOptions.cs                                  [N]
│  ├─ Providers/LlmConversationClient.cs                     [N]
│  ├─ Providers/LlmConversationHttpTransport.cs               [N topes/timeout/conversación]
│  ├─ Providers/OpenAI/OpenAiConversationClient.cs            [N]
│  ├─ Providers/Anthropic/AnthropicConversationClient.cs      [N]
│  ├─ Security/AesGcmAiAssistantContentProtector.cs           [N]
│  ├─ Security/HmacAiAssistantRequestFingerprint.cs           [N]
│  ├─ Persistence/AiAdvisoryLockKeys.cs                       [M]
│  ├─ Persistence/Configurations/
│  │  ├─ AiConversationConfiguration.cs                      [N]
│  │  ├─ AiAssistantTurnConfiguration.cs                     [N]
│  │  ├─ AiAssistantToolExecutionConfiguration.cs            [N]
│  │  └─ AiAssistantRequestReceiptConfiguration.cs           [N]
│  ├─ Persistence/Readers/
│  │  ├─ AiConversationReader.cs                            [N]
│  │  └─ AiAssistantTurnReader.cs                           [N]
│  ├─ Persistence/Repositories/
│  │  ├─ AiConversationRepository.cs                        [N]
│  │  ├─ AiAssistantTurnRepository.cs                       [N tools vía agregado Turn]
│  │  └─ AiAssistantRequestReceiptRepository.cs             [N dedup/cuota]
│  ├─ Workers/AiAssistantCoordinator.cs                      [N scopes/unidades]
│  ├─ Workers/AiAssistantBackgroundService.cs                [N scheduler]
│  ├─ Workers/AiAssistantRetentionBackgroundService.cs       [N scheduler mantenimiento]
│  ├─ ArtificialIntelligenceInfrastructureRegistration.cs   [M]
│  └─ AGENTS.md                                             [M]
├─ Persistence/Migrations/
│  ├─ <fecha>_AddAiAssistant.cs / .Designer.cs                 [N generados]
│  └─ ApplicationDbContextModelSnapshot.cs                    [M generado]
└─ Persistence/Seed/RoleSeeder.cs                            [R catálogo existente]

src/ArquitecturaBase.Api/
├─ Modules/ArtificialIntelligence/
│  ├─ Controllers/AiAssistantController.cs                   [N]
│  ├─ Contracts/Assistant/
│  │  ├─ SubmitAiAssistantTurnHttpRequest.cs                  [N ToString redactado]
│  │  ├─ ListAiConversationsQuery.cs                         [N]
│  │  └─ ListAiAssistantTurnsQuery.cs                        [N]
│  ├─ AiRateLimitOptions.cs                                  [M]
│  ├─ ArtificialIntelligenceApiRegistration.cs               [M políticas/inventario]
│  └─ AGENTS.md                                             [M]
├─ Program.cs                                               [R salvo composición necesaria]
├─ Localization/LocalizationExtensions.cs                    [R]
└─ Json/UtcDateTimeConverter.cs                              [R]
~~~

No DbSet de módulo en ApplicationDbContext. El módulo usa Set<T>; las configuraciones se
descubren como hoy. No tocar AppHost/ServiceDefaults/CORS/BackendPrefixes para este recorrido.
No nuevo paquete .NET por defecto; si se demuestra necesidad, va centralizado en
Directory.Packages.props y requiere revisar el contrato de ApplicationPackagesTests.

## 20. Árbol de archivos: front

Raíz: C:/Users/ezequ/source/repos/ArquitecturaBaseFront.

~~~text
package.json / package-lock.json                             [M sólo dependencia elegida]
src/
├─ app/
│  ├─ AuthenticatedHome.tsx                                  [N composición privada]
│  ├─ AuthenticatedHome.test.tsx                             [N]
│  ├─ routes.tsx                                            [M hoja /]
│  ├─ providers.tsx                                         [R Toaster/Query/tema existentes]
│  └─ AGENTS.md                                             [M]
├─ auth/components/
│  ├─ AccountRecoveryNotice.tsx                             [N extraído de Dashboard]
│  └─ AccountRecoveryNotice.test.tsx                        [N]
├─ auth/AGENTS.md                                            [M aviso reutilizable]
├─ features/home/
│  ├─ pages/DashboardPage.tsx                                [M reutilizar aviso]
│  └─ AGENTS.md                                             [M]
├─ features/artificial-intelligence/
│  ├─ pages/AiAssistantPage.tsx                              [N]
│  ├─ pages/AiAssistantPage.test.tsx                         [N recorridos]
│  ├─ components/assistant/
│  │  ├─ AssistantRuntimeProvider.tsx                       [N adaptador específico]
│  │  ├─ AssistantWelcome.tsx                               [N sugerencias autorizadas]
│  │  ├─ AssistantSources.tsx                               [N evidencia y destinos]
│  │  ├─ AssistantHistoryDialog.tsx                         [N historial propio]
│  │  └─ AssistantOperationState.tsx                        [N estados durables]
│  ├─ hooks/
│  │  ├─ useAssistantConversation.ts                       [N lecturas/timers Query]
│  │  ├─ useAssistantTurnActions.ts                        [N aceptación/cancel/delete]
│  │  └─ useAssistantTurnActions.test.tsx                   [N pérdida/conflictos]
│  ├─ api/assistant.ts                                      [N DTOs + httpClient]
│  ├─ assistant/model.ts                                    [N proyección UI y estado local]
│  ├─ assistant/model.test.ts                              [N]
│  ├─ assistant/errors.ts                                   [N códigos/recuperación]
│  └─ AGENTS.md                                             [M]
├─ shared/ui/assistant/
│  ├─ AssistantThread.tsx                                  [N primitivas y aspecto]
│  ├─ AssistantComposer.tsx                                [N Input nativo assistant-ui/acciones comunes]
│  ├─ AssistantMessage.tsx                                 [N texto/bloques tipados]
│  ├─ AssistantThread.test.tsx                             [N foco/scroll/IME]
│  └─ AGENTS.md / CLAUDE.md                                 [N]
├─ shared/api/presentation.ts                               [M installedFeatures]
├─ shared/api/httpClient.ts                                 [R transporte único]
├─ shared/api/queryClient.ts                                [R políticas comunes]
├─ shared/ui/notifications.ts                               [R duración/avisos]
├─ shared/hooks/useCountdown.ts                             [M deadline compatible]
├─ shared/hooks/useCountdown.test.tsx                       [N]
├─ shared/hooks/AGENTS.md                                    [M regla del timer]
├─ shared/lib/dateTime.ts                                   [R formateo único]
├─ shared/i18n/index.ts                                     [M sólo precarga si necesaria]
├─ locales/es/assistant.json                                [N]
├─ locales/en/assistant.json                                [N]
├─ test/architecture/featureDependencies.test.ts            [M integración sin imports cruzados]
├─ test/architecture/designArchitecture.test.ts            [M primitivas/composición estrecha]
├─ test/visual/assistant.spec.ts                            [N escenarios]
└─ test/visual/fixtures.ts / Catalog.tsx                     [M escenarios productivos simulados]
~~~

No copiar fake auth, fixtures, escena/lang/selector ni transportes del laboratorio al producto.
No alterar la paleta/global theme para adoptar los radios del demo. Una variante necesaria
se incorpora en shared/ui con revisión de sus consumidores y evidencia de ambos idiomas.

## 21. Inventario de tests backend

Archivos nuevos previstos bajo las carpetas correspondientes:

~~~text
tests/ArquitecturaBase.Domain.UnitTests/Modules/ArtificialIntelligence/
  AiConversationTests.cs
  AiAssistantTurnTests.cs
  AiAssistantRequestReceiptTests.cs

tests/ArquitecturaBase.Application.UnitTests/
  Channels/SystemQueries/SystemQuerySourcesTests.cs
  Services/Settings/SystemSettingsServiceTests.cs             [M capacidades]
  Channels/ClientCapabilityTests.cs                           [N]
  Modules/ArtificialIntelligence/
    AiAssistantServiceTests.cs
    AiAssistantDispatchServiceTests.cs
    AiAssistantExecutionServiceTests.cs
    AiAssistantRetentionServiceTests.cs
    AiAssistantEvidenceVerifierTests.cs
    ResourceParityTests.ArtificialIntelligence.cs             [M]
    AGENTS.md                                                 [M]

tests/ArquitecturaBase.Api.IntegrationTests/Modules/ArtificialIntelligence/
  AiAssistantEndpointsTests.cs
  AiAssistantPersistenceTests.cs
  AiAssistantConcurrencyTests.cs
  AiAssistantProviderContractTests.cs
  AiAssistantRecoveryTests.cs
  AiAssistantRetentionTests.cs
  AiAssistantMigrationTests.cs
  AiAssistantPrivacyTests.cs
  AiTestModule.cs                                              [M doble proveedor/inventario]
  AGENTS.md                                                   [M]

tests/ArquitecturaBase.ArchitectureTests/Modules/ArtificialIntelligence/
  AiTransactionContribution.cs                                [M locks]
  AiAssistantBoundaryTests.cs                                  [N reglas específicas]
  AGENTS.md                                                   [M]
~~~

El inventario explícito se amplía mediante AiTestModule/IApiTestModule y las contribuciones
existentes; no agregar referencias IA al núcleo del arnés.
Helpers exclusivamente de prueba en TestFeatures/Modules según su ámbito.
Reutilizar FakeUnitOfWork/FakeTimeProvider, handlers HTTP falsos y Docker real para PostgreSQL/Redis.

Casos mínimos, con nombres ingleses como frases:

| Garantía | Prueba representativa |
| --- | --- |
| Validación antes de guardar | Invalid_question_does_not_open_a_transaction |
| Aceptación atómica | Failed_acceptance_rolls_back_conversation_turn_and_receipt |
| Deduplicación | Repeated_request_returns_the_same_turn_without_another_provider_call |
| UUID distinto cuerpo | Reused_request_id_with_different_question_returns_conflict |
| Recuperación | Lost_acceptance_response_is_recovered_by_request_id |
| Propiedad | Another_users_conversation_is_indistinguishable_from_missing |
| Permisos | Revoked_source_permission_blocks_history_and_publication |
| Herramientas | Unknown_tool_and_extra_arguments_do_not_query_the_database |
| Cantidades | User_summary_counts_the_full_filtered_dataset |
| Evidencias | Foreign_or_unknown_evidence_id_prevents_completion |
| Carreras | Two_hosts_claim_a_turn_once_and_share_capacity |
| Cancelación | Confirmed_cancel_request_prevents_late_completion |
| Efectos externos | Provider_request_runs_after_claim_commit_and_outside_transactions |
| Caída | Expired_running_lease_is_uncertain_and_is_not_regenerated |
| Commit final | Retrying_result_persistence_does_not_repeat_provider_http |
| Límites | Deadline_caps_every_step_and_never_extends_on_retry |
| Cambio de conexión | Changed_connection_revision_prevents_the_next_provider_step |
| Protocolo | Required_provider_continuation_items_keep_their_order_and_ids |
| Privacidad | Logs_and_http_contracts_do_not_expose_content_or_credentials |
| Retención | Purged_content_keeps_the_receipt_until_its_window_expires |
| Opcionalidad | Removing_ai_leaves_core_queries_and_home_composition_buildable |
| Idioma | Header_and_default_culture_apply_to_background_turns |
| UTC | All_assistant_instants_serialize_with_z_and_use_fake_time |

Además: 401/403/404/409/429, OpenAPI con respuestas/ProblemDetails, body ilegible, Unicode,
índices/cascadas, migración de base anterior, datos de conexión/negocio intactos,
quota dos hosts, filtrado de Removed, reanudación de lecturas y purga contra worker tardío.

## 22. Plan por fases con entrada, archivos, salida y prueba

TDD donde hay lógica: rojo que observa comportamiento, implementación mínima, refactor
y pruebas de integración. No marcar una fase cerrada sólo porque existen archivos.

Estado registrado tras ejecutar el desarrollo:

| Fases | Resultado local |
| --- | --- |
| 0 | Contrato/ADR y tablero local completo antes de programar; sincronización al Artifact externo pendiente |
| 1–2 | Capacidades, cinco fuentes, aceptación/historial/cifrado y migración implementados, con unitarios y Docker |
| 3–4 | Ambos protocolos con handlers falsos, worker, cancelación, fencing, recuperación y retención comprobados |
| 5 | assistant-ui, timer/consumidores, es/en y privacidad del SPA comprobados |
| 6 — local | Build backend sin advertencias, 2334 pruebas; build/lint front, 1003 unitarios y 185 visuales; smoke Aspire final real y servidores propios cerrados |
| 6 — piloto | Modelos/cuentas reales, calibración de propuestas y condiciones operativas siguen pendientes en la sección 25 |

### Fase 0 — contrato y tablero completo elegido

Archivos: este plan; propuesta visual y baseline; ADR de assistant-ui y ciclo durable,
con número siguiente real según docs/decisions/README.md.
No cambiar MVC/UoW; el ADR justifica dependencia, transporte, protocolo externo y ciclo de operación.

Entregables: cinco consultas/estados del tablero, alcance de lectura, mensajes/canales,
fuentes, historial, pérdida de permisos, cancelación, 429 y recuperación.
Conservar la selección explícita assistant-ui. Dibujar estados restantes con piezas reales antes de
programar sus pantallas; la elección de biblioteca no acredita todos los diálogos.
No afirmar sincronización del Artifact privado si no se pudo acceder.

Cierre: revisar contratos/decisiones con esta matriz; identificar modelos/perfiles piloto y
retención antes de habilitar datos reales. Ninguna llamada productiva en esta fase.

### Fase 1 — capacidades y consultas sin generación

Archivos: IClientCapabilitySource, ISystemQuerySource, modelos/Channels neutrales,
IUserReader/UserReader, IRoleReader/RoleReader, fuentes IA y Settings.
Pruebas: exactitud de agregados, filtrado, definición por permiso, ausencia/duplicación
de aportantes, núcleo sin módulo y proyección pública compatible.

Cierre: consultas devuelven proyecciones y evidencia reales bajo actor explícito.
No instalar biblioteca para resolver SQL ni abrir un endpoint universal de herramientas.

### Fase 2 — Domain, persistencia, aceptación e historial

Archivos: cuatro entidades/configuraciones/puertos/implementaciones, cifrado/huella,
AiAssistantService, validadores, permiso/recursos/controller y migración única.
Pruebas: rollback, revisión/dedup, propiedad, cuotas PostgreSQL, API/OpenAPI, UTC/recursos,
historial, cancel/delete y pérdidas de respuesta con proveedor falso.

Cierre: el front puede aceptar/recuperar una operación durable; dos hosts no duplican intentos.
Generation sigue con doble de prueba; no falso “Completed” productivo.

### Fase 3 — proveedores, herramientas y ciclo de worker

Archivos: clientes de conversación, transport/options, Execution/Dispatch/helpers y coordinador.
Pruebas: ambos protocolos completos, loop acotado, continuidad, cancelación, permisos,
ausencia de transacción durante HTTP, response too large y usage incierto.

Cierre: preguntas elegidas funcionan con handlers falsos usando datos reales de integración.
Ningún retry externo, ningún SDK en Application ni dependencia de Node para backend.

### Fase 4 — mantenimiento y resistencia

Archivos: RetentionService/worker, índices/queries de lease/recibos, tests de recovery/retención.
Pruebas: reinicio Pending/Running, caída entre HTTP/commit, reintento de sólo persistencia,
dos hosts, cambios de modelo/permisos, purga y fencing.

Cierre: cada fallo tiene estado recuperable y ningún GET dispara generación.
Conservar el recibo pese a borrado; cuotas no se evaden borrando conversaciones.

### Fase 5 — integración de assistant-ui y timer común

Archivos: dependencia mínima, app/AuthenticatedHome, feature IA, shared/ui/assistant,
AccountRecoveryNotice, locales, hook común y fixtures/arquitectura.
Pruebas: envío duplicado, aceptación incierta, 401 con body idéntico, cambio de hilo/idioma,
permisos, estado terminal, avisos únicos, cleanup y todas las regresiones de useCountdown.

Cierre: construir UI con API real/dobles de contrato, sin copiar bootstrap/estados falsos del lab.
Escritorio/móvil y ambos idiomas cubiertos.

### Fase 6 — verificación conjunta y piloto

Ejecutar checks de ambos repositorios y un recorrido real con Aspire.
Prueba proveedor real acotada sólo en ambiente piloto con credencial del producto configurada
y condiciones/límites aprobados; tests automatizados siguen sin consumo facturable.
Concluir compatibilidad sólo para la combinación proveedor/modelo realmente ensayada.

Cierre: fuentes correctas, historial/reload, repreguntas actuales, permisos revocados,
cancelación, retención, cotas y retirada del módulo comprobados.
Cerrar Aspire/servidores de pruebas al terminar; conservar sólo la vista solicitada para revisión.
Retirar el selector y bibliotecas descartadas del laboratorio cuando se promueva la pantalla,
rescatando evidencia/procedencia que corresponda.

## 23. Documentación y mapa a actualizar al implementar

### Operación y observabilidad a entregar con el código

- Métricas de Pending/Running por instalación, edad del pendiente más viejo, Accepted/Completed/
  Failed/Uncertain/Canceled/Expired, pasos externos, tool latency y latencia total.
- Uso de tokens sólo cuando fue informado; métrica separada de uso desconocido, sin estimarlo en cero.
- Alerta de Running vencido, purga atrasada y fallos de claim/cierre; logs con códigos e IDs,
  sin cuerpos. Evitar alta cardinalidad: no etiquetas de pregunta, usuario o conversación en métricas.
- Readiness comprueba servicios locales necesarios, sin llamar al LLM. La salud remota de un
  proveedor es una condición operativa; no derriba ingreso/perfil por una llamada facturable.
- Worker se detiene cooperativamente, deja estados durables y no vuelve a enviar al arrancar
  un Running que pudo haber salido. Prueba de shutdown/reinicio antes de producción.
- Runbook para revisar un intento Uncertain, rotación/restore del anillo, perfiles incompatibles,
  cuota agotada y retención atrasada. Ninguna reparación operativa repite generación automáticamente.
- Comparar alcance con el plan de sugerencias IA de WhatsApp: reutilizar el adaptador de proveedor
  si aparece ese consumidor; cualquier puerto hacia otro módulo sigue la frontera neutral.
  Esta entrega no incorpora referencias cruzadas ni resuelve ese plan por anticipación.

| Archivo | Qué consolidar entonces |
| --- | --- |
| docs/features/inteligencia-artificial.md | Generación/historial implementados, permisos, límites, rutas y retención |
| docs/guides/configurar-ia.md | Perfiles modelo, límites, workers, protección/restore y operación |
| docs/guides/quitar-ia.md | Cuatro tablas nuevas, composición Inicio y dependencias del front |
| docs/architecture/mapa-de-instrucciones.md | Fuentes/servicios/adaptadores nuevos y pares locales |
| docs/architecture/premisas-de-la-plantilla.md | Sólo decisiones verificadas, no propuestas |
| docs/decisions/README.md + nuevo ADR | Elección e integración técnica |
| ../ArquitecturaBaseFront/docs/architecture.md | Composición de Inicio y contratos de integración |
| ../ArquitecturaBaseFront/docs/mapa-de-instrucciones.md | Nueva UI compartida/feature |
| ../ArquitecturaBaseFront/docs/design/visual-baseline.md | Elección y evidencia productiva cuando exista |
| ../ArquitecturaBaseFront/docs/guides/estandar-visual.md | Integración compartida de primitivos verificada |
| ../ArquitecturaBaseFront/docs/guides/mensajes-y-estados.md | No cambiar su política; registrar adopción de este consumidor |
| AGENTS de cada área afectada | Responsabilidad/peligros; CLAUDE importa @AGENTS sin duplicarlas |

Agregar AGENTS/CLAUDE en scopes nuevos listados, leer padres antes de tocar código y actualizar
mapa en el mismo desarrollo. No crear una guía por cada DTO.
Después del cierre, consolidar y retirar este plan siguiendo auditar-documentacion; Git conserva
el historial. Mientras siga propuesto, no presentarlo como comportamiento del producto.

## 24. Verificación exigida y criterio de terminado

Backend:
~~~powershell
dotnet build ArquitecturaBase.slnx
dotnet test
~~~

Front, desde su raíz:
~~~powershell
npm run build
npm run lint
npm run test
npm run test:visual
~~~

Migración según la receta canónica: generar/revisar Up/Down/snapshot y ejecutar
has-pending-model-changes. No copiar el timestamp de un ejemplo.
Integración requiere Docker; sin Docker se puede avanzar con unitarios/arquitectura,
pero el desarrollo conserva integración pendiente y no se declara cerrado.
Si se levantó Aspire: aspire stop al finalizar. No parar procesos ajenos de Visual Studio.

Aceptación funcional:
- Las cinco consultas elegidas devuelven información real permitida y fuentes verificables.
- Pregunta inicial y repregunta funcionan con ambos proveedores/modelos del piloto.
- Reload/corte de red recupera el mismo intento y no repite HTTP facturable.
- No se accede a historial ajeno ni a contenido que ya requiere un permiso revocado.
- Dos hosts cumplen cuota/capacidad y sólo un claim; worker tardío no publica.
- Cancelar, revisar incertidumbre y nueva conversación conservan estado/borrador según contrato.
- Retención elimina payload/fuentes y respeta recibos; copia sin IA compila y funciona.
- es/en, cultura general, zona del perfil, FakeTimeProvider, cooldown y timer común comprobados.
- Avisos comunes no se duplican; teclado/móvil y capturas revisadas.
- Builds sin advertencias, suites requeridas verdes, diff revisado y documentación consolidada.
- Ensayo real y límites de modelo explicitados; una maqueta o una suite verde no prueba calidad del LLM.

## 25. Evidencia de esta revisión y pendientes honestos

La revisión inicial preparó este plan sin implementar. El pedido posterior del usuario autorizó
su desarrollo completo y produjo código, migración y pruebas locales. La evidencia vigente se
registra en [la revisión de implementación](../reviews/2026-10-04-inicio-asistente.md); no se
confunden pruebas de contratos con handlers falsos con compatibilidad de un modelo real.

Antecedente visual: el comparador pasó 48 combinaciones y recorridos locales; producto pasó
sus verificaciones registradas en la propuesta visual. Ese resultado no se atribuye al código
productivo de este plan; las capturas del producto tienen su registro independiente.

Pendientes para habilitar producción:
1. Confirmar/calibrar cuotas y retención propuestas; el alcance de consultas ya fue autorizado.
2. Sincronizar con el Artifact externo el tablero elegido y la evidencia productiva ya generada.
3. Conservar la evidencia local registrada en la revisión de implementación al ensayar el piloto.
4. Ensayar proveedores/modelos concretos con cuentas del producto.
5. Verificar condiciones de egress/retención de proveedores, recuperación de anillo/backup
   y operación de dos réplicas. No usar una sesión de Codex como credencial.
6. Texto progresivo/MCP externo/acciones de escritura sólo se incorporan con alcance y
   contrato propios; no agregar puertos sin consumidor en esta entrega.

## 26. Fuentes oficiales consultadas el 2026-10-04

- [assistant-ui ExternalStoreRuntime](https://www.assistant-ui.com/docs/runtimes/custom/external-store):
  estado externo y capacidades habilitadas por callbacks. El encaje con .NET/Query es una decisión
  de integración del proyecto, no una arquitectura prescrita por la biblioteca.
- [OpenAI Function calling](https://developers.openai.com/api/docs/guides/function-calling):
  ciclo de herramientas y schemas estrictos. La autorización de datos pertenece a nuestro servidor.
- [OpenAI Responses](https://developers.openai.com/api/docs/guides/migrate-to-responses):
  historia/continuaciones y control store; conservar los items necesarios del intento.
- [Claude Define tools](https://platform.claude.com/docs/en/agents-and-tools/tool-use/define-tools):
  definición del catálogo. El adaptador debe comprobar también la guía de ejecución/continuación
  al implementar los handlers de contrato.
