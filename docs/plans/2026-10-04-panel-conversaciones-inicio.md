# Asistente de Inicio: capacidad, ciclo de vida y calibración

Estado (2026-11-18): los siete pendientes de abajo siguen **sin empezar**, salvo lo que cubrieron las
tareas 12, 18 y 28 de los [arreglos de la revisión](#arreglos-de-la-revisión-de-arquitectura), que están programados y
cerraron en verde el 2026-11-17; lo que dejaron para después está en
[lo que quedó de los arreglos](#lo-que-quedó-de-los-arreglos). Inicio, historial privado, panel interno y título derivado ya
están implementados: reglas en [inteligencia artificial](../features/inteligencia-artificial.md#panel-interno-e-identificación-del-historial--2026-10-05),
configuración y runbook en [configurar IA](../guides/configurar-ia.md#habilitar-el-asistente) y
decisión en [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md). Este plan
conserva sólo lo abierto: cuánto se guarda, cuánto aguanta una instalación y con qué valores se
habilita. El piloto con modelos y cuentas reales está en [verificación y producción](verificacion-y-produccion.md).

## Alcance

- Una instalación independiente por empresa, con base, anillo e `InstallationId` propios. La
  capacidad se mide y dimensiona **por instalación**; compartir un servidor PostgreSQL comparte
  CPU, IO y conexiones y se dimensiona como operación, no como multi-tenancy en estas tablas.
- Entra: ventanas de retención, purga física de metadata, purga acotada de WhatsApp, ensayo de
  carga y calibración de cuotas/capacidad/retención.
- No entra: título editable o conservado aparte, mensajes normalizados por separado, particionado
  sin medición ni archivado periódico.

## Punto de partida comprobado en código

- El contenido de cada turno vence a los 30 días. `AiAssistantRetentionService.PurgeBatchAsync`
  purga cada hora un lote de hasta 100 elementos repartido entre contenido, conversaciones de
  cuentas borradas que retira, conversaciones que borra y comprobantes vencidos.
- **Mientras una conversación tenga algo vigente, sus turnos sin contenido conservan metadata.** Una
  conversación, de Inicio o de un canal, se borra físicamente cuando no queda un turno activo, venció
  todo su contenido y ningún recibo sigue vigente, aunque nadie la haya retirado; una de Inicio
  retirada no espera los recibos. Una cuenta borrada retira y cancela lo suyo aunque ya no tenga
  contenido; una desactivada queda bloqueada sin purga anticipada.
- WhatsApp vacía `Body` y `ProtectedFlowResponse` a los 90 días con un `UPDATE` sin lote y sin
  índice parcial por fecha con contenido presente (`WhatsAppMessageRetentionRepository`); las
  filas se conservan ([WhatsApp](../features/whatsapp.md#reglas)).
- `AiAssistantOptions.IsValid` pone techo de código: 30 días, 4 Running, 50 Pending. Las cuotas
  diarias no tienen techo. Subir un techo es un cambio de código con pruebas, no sólo configuración.
- Los locks de presupuesto, actor y conversación serializan escrituras breves: más réplicas no
  garantizan más throughput.
- El listado pagina con offset y `TotalCount`, 20 filas por defecto y dos consultas por página
  (`AssistantReadCommandRecorder`). Es una regresión, no un benchmark.
- No hay archivado, tablas mensuales, particiones ni traslado a otra base.

## Decisiones tomadas

- Los valores 10 por actor y 100 por instalación por día UTC, 4 Running, 4 Pending (bajó de 50 con
  la tarea 12; el código admite hasta 50) y 30 días son propuestas del piloto, no premisas.
- Una operación activa o incierta no se borra para «limpiar».
- Como referencia aritmética, 10 personas × 5 consultas × 365 días dan 18.250 turnos por año.
  No fija una cuota ni acredita una prueba de carga.
- Separar mensajes en otra tabla sólo se justifica por una capacidad concreta (adjuntos, edición,
  branching), no por la cantidad de filas.
- Pasar a cursor (`LastActivityAtUtc` + `Id`) sólo si la medición muestra costo en páginas
  profundas, y con un contrato nuevo.
- Particionar por fecha sólo si las mediciones lo justifican; nunca «tabla por mes» en controllers
  ni una base por usuario.
- Con el [ADR 0020](../decisions/0020-motor-unico-de-ia-con-capacidades.md), los canales nuevos
  comparten cuotas y operación durable: la calibración cuenta todos los canales de la instalación.

## Pendientes y criterio de cierre

1. **Ventanas por empresa**: contenido, metadata de conversaciones y turnos, comprobantes y
   deduplicación, backups. No asumas 12 meses. Se cierra con valores aprobados por el usuario,
   reflejados en la feature, en configurar IA y en la política pública.
2. **Purga física de terminales y de conversaciones sin contenido**, con el registro mínimo que
   impida repetir un UUID, eludir la cuota o reprocesar webhooks y envíos. En WhatsApp, conservá
   referencias, read-state y contactos, y validá el replay antes de borrar IDs de Meta. Se cierra
   con TDD, integración con Docker y migración según la [guía](../guides/migracion.md); si cambia
   una regla de producto, con ADR.
3. **Purga de IA sobre muchas filas ya vaciadas**: evaluá un índice parcial de contenido presente.
   Se cierra con los planes SQL de la consulta de purga sobre el volumen del ensayo; no des por
   bueno el índice actual de `ContentExpiresAtUtc` sin medirlo.
4. **Purga de WhatsApp acotada** por lote, con índice parcial fecha/Id y `Body` presente, y la salida
   durable sin Pending/Sending. Se cierra con TDD, integración y migración propia.
5. **Operación de la base**: autovacuum, estadísticas, tamaño de tablas, índices y WAL, backups y
   restore, atraso de purga. Borrar contenido deja espacio reutilizable; no achica los archivos.
6. **Ensayo de carga reproducible**, fuera de CI y de la base real:
   - base desechable con 10.000, 100.000 y 1.000.000 de turnos sintéticos de una instalación,
     con contenido vigente, vencido y purgado, y herramientas;
   - 10, 50 y 100 sesiones contra dos hosts con PostgreSQL y Redis reales, con aceptaciones
     repetidas, cancelaciones y borrados concurrentes;
   - proveedor HTTP controlado, sin cobro; el ensayo real chico con modelos va aparte y sin reintentos;
   - medí p50/p95/p99, conexiones, CPU/IO, locks, SQL por request, memoria, payloads, pendientes,
     tokens informados y atraso de purga, con `EXPLAIN (ANALYZE, BUFFERS)` de listado, apertura,
     últimas páginas, cuotas/claims y purga, sin contenido en las etiquetas.

   Objetivo a aprobar contra el hardware: p95 ≤ 300 ms en listado y lectura de página y ≤ 500 ms
   en aceptación, sin contar el proveedor; cero generación por GET, cero activos duplicados y
   backlog acotado. Se cierra con un informe de recursos, distribución, carga y límites. Si un
   objetivo falla, ubicá SQL, locks o capacidad antes de cambiar contratos o índices.
7. **Calibración**: con el piloto real y el ensayo, aprobá cuotas, capacidad y retención. Se cierra
   cuando la feature y configurar IA dejan de marcarlas como propuestas. Si superan un techo de
   código, el cambio lleva pruebas.

Referencias: PostgreSQL, [particionado](https://www.postgresql.org/docs/current/ddl-partitioning.html)
y [vacuum](https://www.postgresql.org/docs/current/routine-vacuuming.html). El plan original
(panel y capacidad) se recupera desde Git, commit `ceaed208`.

## Arreglos de la revisión de arquitectura

Decididos el 2026-11-14; se programan desde el 2026-11-15, etapa por etapa (cada una anota abajo sus commits). Salen de la [revisión de arquitectura de IA](../reviews/2026-11-14-arquitectura-ia.md),
que tiene el escenario, la evidencia (archivo y línea en `a9f57202`) y el esfuerzo de cada hallazgo, y de las
[decisiones del usuario](../reviews/2026-11-14-arquitectura-ia.md#decisiones-del-2026-11-14), que no se vuelven a preguntar.

- **Alcance:** los hallazgos de «Hoy» (B1 a B5 e I1 a I16), I17, y de gran escala sólo el conteo de pendientes del bootstrap,
  los índices parciales de I20 y el conteo del claim de I19.
- **Fuera:** I18, antes de pasar de una réplica; el resto de I19 e I20, cuando haya volumen real que medir; y las
  observaciones menores, que quedan anotadas en la revisión.
- **Cruce con este plan:** la tarea 18 resuelve, para Inicio, la metadata sin plazo del punto de partida y parte de los
  pendientes 1 y 2 (la metadata de una conversación de Inicio vive lo que su contenido y sus recibos); la tarea 28 es el
  índice parcial que pide el pendiente 3; y la tarea 12 baja el tope de pendientes, que el pendiente 7 vuelve a calibrar con
  el piloto. El punto de partida dice que una cuenta borrada retira lo suyo: sólo pasa si todavía tiene contenido (I3), y lo
  arregla la tarea 18.
- **Cuándo:** en orden y sin fecha; ninguna traba la prueba integral de la tanda.
- **Al día:** el 2026-11-14 el único commit que tocó el código de IA desde `a9f57202` fue `42d8f47f` (el motivo de la
  lectura guardada de un documento). Cada tarea vuelve a leer sus archivos antes de empezar.
- **Sin revisor del plan:** es documentación, y la revisión adversarial va según el riesgo; la de cada tarea va abajo.

**Cómo se trabaja.** Cada tarea sigue [validar un cambio](../guides/validar-un-cambio.md): el test en rojo primero y las
suites rápidas del área. La revisión adversarial va completa en lo que toca datos personales, permisos, el cupo o una llamada
al proveedor, y no en documentación, textos ni arreglos de una línea. Cada etapa cierra con el build sin advertencias, los
unitarios y `ArchitectureTests` enteros y la integración de lo que cambió; la corrida completa va una sola vez, al cerrar el
desarrollo. Además:

- una regla escrita que cambia se corrige con su arreglo, no antes, y cada tarea nombra las suyas:
  [inteligencia artificial](../features/inteligencia-artificial.md), [configurar IA](../guides/configurar-ia.md),
  [quitar IA](../guides/quitar-ia.md) y los `AGENTS.md` del módulo, que repiten algunas;
- ninguna suite llama a una API que cobra: los proveedores van con sus dobles, que aprenden cada caso nuevo antes del
  arreglo (un 429 de cupo, un 404 de modelo, una conexión rechazada), como pide el
  [ADR 0018](../decisions/0018-robustez-proporcional-al-efecto.md);
- un error nuevo se declara en Domain, con su código estable y sus textos en `Errors.resx` y `.en.resx`;
- un cambio de un contrato publicado es aditivo y se revisa con el front
  ([ADR 0005](../decisions/0005-sin-versionado-de-api-por-ahora.md)); una columna nueva es opcional, y un valor nuevo del
  sello o del proveedor se escribe un despliegue después del que ya lo lee ([despliegue](../guides/despliegue.md));
- los logs van con `[LoggerMessage]`, sin preguntas, respuestas, claves ni datos de la persona;
- ninguna tarea cambia las instrucciones del modelo, que pediría una versión nueva y una calificación con el motor real
  ([configurar IA](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles)): la tarea 6 suma la hora al mensaje, no a
  las instrucciones, y el formato de fechas y números espera a la próxima versión que haya que calificar (decisión 7).

Las etapas van en orden, y dentro de cada etapa las tareas también, salvo en las etapas 1 y 2, donde son independientes.

### Etapa 1: lo trivial y la documentación

1. **B3. Una falla pasajera de la base no cancela la consulta.** El vigilante de cancelación registra la excepción y sigue
   con la lectura siguiente; la confirmación final ya verifica fence, estado y cancelación (`AiAssistantCoordinator`). Test:
   con una lectura del vigilante que falla, el turno termina con la respuesta del proveedor. Una línea.
2. **B4. Una instrucción que falta, lanza.** `AssistantTexts.Prompt(key)` lanza si falta la clave y `Get` conserva su
   respaldo para los códigos de falla; un test recorre las claves de instrucciones que usa el código. Trivial.
3. **B5. Inicio y los canales arman los hechos del negocio con la misma regla.** `business.information` de Inicio usa
   `AiBusinessKnowledgePolicy`, sin los días sin definir ni la zona cuando ningún día está confirmado; un test compara los
   dos con los mismos datos. Trivial.
4. **I10, el log. El sondeo vacío no llena el log.** El claim y la reconciliación sin trabajo no pasan por `OperationLog`, y
   `Microsoft.EntityFrameworkCore.Database.Command` va en Warning en `appsettings.json`. Test: un claim sin trabajo no
   escribe en Information. Trivial.
5. **I16. La guía para quitar IA, al día.** Sin número fijo de claves (remite a `ErrorCodeTests`), con el prefijo
   `ai/assistant/documents/` y un paso previo: pasar a Manual los chats en modo Asistente y cerrar las extracciones
   pendientes de Formularios, con lo que defina su revisión. Si la tarea 26 agrega una tabla, la guía la nombra. Chico.
6. **I15, el instante. En Inicio el modelo sabe qué día es** (decisión 7). Al armar el contexto de Inicio, la hora del pedido
   y la zona del perfil van al final del mensaje actual, como en los canales, sin tocar el perfil sellado ni las
   instrucciones, así no cambia la huella de los reintentos. Test del contexto de Inicio con la línea de la hora. La regla de
   la hora (`inteligencia-artificial.md:147`) pasa a valer también para Inicio. Trivial.
7. **Los dos costos, escritos** (decisión 8). La regla de IA dice que la cuota corta en el día UTC, e Inicio muestra, con el
   error de cupo, la hora local en que se renueva, a partir de `retryAfter`; [configurar IA](../guides/configurar-ia.md)
   escribe el máximo diario en tokens que resulta de las cuotas y de los techos de cada turno, y sugiere una alerta en la
   cuenta del proveedor. Chico.

**Hecha el 2026-11-15:** B3 en 07b57864, B4 en a0eeb4d3, B5 en 38648b9f, I10 (el log) en 7d97d88d, I16 en e1425a6e, I15
en ed519cc7 y los dos costos en 02a474bc (front edd82618). También B1, que el alcance incluía sin número de tarea, en
de3ccfb4: la última propuesta se reserva primero, el resto entra del más nuevo al más viejo sin huecos y, si la propuesta sola
no entra, el turno falla con `Ai.Assistant.ProposalTooLarge`. En Inicio la hora usa `Prompt.SourceTime`, que dice «zona del
negocio» aunque sea la del perfil: corregirlo es un cambio de instrucciones y va con la próxima calificación (decisión 7).
Build sin advertencias, Domain 1168, Application 6080 y Architecture 229 enteros, e integración de IA (148) y de WhatsApp que
consume el motor (41) en verde; en el front, tsc, lint y vitest de la feature (dos cortes por carga en `AiAssistantPage`,
que solo da 86/86).

### Etapa 2: las fallas se entienden

8. **I5. Una capacidad que falla no se culpa al proveedor.** En `ScopedAssistantCapabilityExecutor`, una excepción de la base
   no cancelada se vuelve un resultado fallido, con un `[LoggerMessage]` que nombra la capacidad, la versión y el turno: una
   lectura queda «no disponible» y el turno sigue; una propuesta corta con un código propio. El coordinador usa
   `ProviderUnavailable` sólo para lo que salió de la llamada al proveedor. Tests: una fuente que lanza `DbException` en una
   lectura, en una precarga y en una propuesta. Chico.
9. **I6. El turno distingue las fallas del proveedor** (decisión 5). Por estado, sin leer el cuerpo: 401 y 403, clave
   rechazada; 402 y 429, cupo o límite del proveedor; 400 y 404, modelo o pedido rechazado, cada uno con su código y su texto
   para quien administra. La resolución de nombres, la conexión rechazada y el TLS son «no disponible» y no inciertos, con un
   plazo de conexión en el handler. El estado HTTP va a un `[LoggerMessage]` con el turno y el proveedor. Vale para texto,
   audio y documentos. Cambia el test que fija todo 4xx como rechazo (`AiAssistantProviderContractTests`), el doble del
   proveedor aprende los casos y un test comprueba que WhatsApp deriva igual con los códigos nuevos (H5). El corte que falla
   rápido queda para más adelante. Chico.
10. **I7. Una clave ilegible se entiende y no quema cupo.** `Ai.Connection.CredentialUnavailable` deja de ser una falla
    interna y el front lo traduce con «No pudimos leer la clave guardada: reemplazala», resaltando «Reemplazar clave»; la
    disponibilidad del asistente prueba el descifrado, local y sin proveedor, y responde no disponible, así no acepta turnos
    que no pueden andar; y el buscador de lugares sin clave de Google es una capacidad apagada, no un 500 por búsqueda.
    Tests: la pantalla con ese código, el bootstrap con una clave ilegible y el buscador sin clave. Chico.
11. **I8. Una fila ilegible no tira la página.** El listado, el detalle y la página de turnos marcan esa fila como contenido
    no disponible, como lo bloqueado, con un log del id; el contexto del worker saltea el turno ilegible como si hubiera
    vencido; y se puede borrar desde el panel. El test que hoy exige que falle la página pasa a exigir ver las demás. Chico.

**Hecha el 2026-11-15:** I5 en 69ec0c45, I6 en cd4b21db, I7 en a55bb418 y 80cd3f17 (front 6b8183c5) e I8 en a57d86f7 y
e7d90a6e (front b6b90091 y 912d5182); el segundo commit de I7 y de I8 corrige lo que encontró la revisión adversarial. Quedó
para después que Configuración › IA avise de una clave ilegible a quien sólo tiene `ai.read`: quien administra ya lo ve al
cargar los modelos. I5 e I6: build sin advertencias, Domain 1169, Application 6142 y Architecture 229, e integración de 20
clases (288) en verde. I7 e I8: Domain 1181, Application 6180 y Architecture 228 de 229 (el rojo es
`Forms.Rule.RecordsReadRequired` sin texto, de a765080f de Formularios), e integración de IA, WhatsApp y Formularios (224, 67
y 51) en verde; en el front, tsc, lint y vitest de la feature (252).

### Etapa 3: el cupo y las subidas

12. **I1. Lo que vence sin intentarse devuelve el cupo** (decisión 2). Al vencer un pendiente que no reservó ningún paso, su
    recibo se marca como devuelto: sigue deduplicando el UUID y deja de contar en las cuotas del día. El tope de pendientes
    por defecto baja a lo que cuatro carriles alcanzan a empezar en 120 segundos con turnos largos, del orden de 8 a 12, como
    dato del piloto. Tests con `FakeTimeProvider`: una tanda de turnos de duración fija devuelve el cupo de los que vencieron
    sin intento, y uno que reservó un paso no lo devuelve. Cambian «borrar no libera la cuota»
    (`inteligencia-artificial.md:217`, `configurar-ia.md:213`), sólo para lo que nunca se intentó, y el valor propuesto en
    `configurar-ia.md`. Chico.
13. **I14. Con el cupo agotado no se lee ni se guarda el archivo.** Antes de leer y guardar, una revisión barata y sin
    candados del cupo del actor y de la instalación, de la capacidad y, en el audio, del perfil de transcripción, con el
    mismo error y su `retryAfter`; la aceptación sigue mandando. En `file-turns`, audio y documentos. Test: con el cupo
    agotado, ni el lector ni el almacenamiento reciben nada. Chico.
14. **I13. El mismo audio dos veces termina en una transcripción.** Como en los documentos: ante `RequestReused` de la
    aceptación se vuelve a consultar y se devuelve esa transcripción si el archivo coincide, y la consulta inicial compara el
    archivo con el descriptor y da 409 si es otro. Con los dos tests espejo de los documentos. La regla del audio en Inicio
    (`inteligencia-artificial.md:183`) suma lo que ya promete para planillas y documentos. Chico.
15. **B2. Cambiar el idioma con una lectura en curso no la pierde.** Para `home-audio` y `home-document`, la consulta y la
    recuperación van por persona e identificador, sin exigir el mismo idioma ni la misma zona; y el front descarta el pedido
    del audio ante un 409, como el del documento. Test: la consulta con otro idioma encuentra la operación. Chico.
16. **I9. Las subidas grandes no voltean una copia.** Un limitador de concurrencia por copia para las tres rutas que reciben
    archivos (`file-turns`, `audio` y `documents`), con una cola corta y 429 con espera, que el front ya entiende; el archivo
    se lee una sola vez a un arreglo de su largo; y la relectura de la copia se compara con un hash incremental, sin traerla
    entera. Test: con el límite tomado, la subida siguiente recibe 429. El costo del lector de Formularios con fotos grandes
    lo mira su revisión. Chico a mediano.

**Hecha el 2026-11-15:** I1 en 734eac9a y f096d255, I14 en 3993227a y 1881ab06, I13 en 45ef6b5e, 6bcac225 y cf1f056b,
B2 en 1db62122, 2f75ee9f y cd3ef78f (front bd8ded44 y 2b26f070, y la guía en 6c954948), e I9 en 2c9303c4 y f2c58ed5; el
segundo commit de cada uno corrige lo que encontró su revisión adversarial. El tope de pendientes quedó en 4 y no en 8: con
turnos de 60 a 80 segundos es lo que cuatro carriles empiezan antes de que venza. I9 toma el lugar después del permiso, uno
por persona y con un plazo para el cuerpo. Quedó para después: que la prueba de la tanda falle con un solo carril (pide
rehacer el proveedor ficticio de integración), la clave de la copia guardada que no depende de la persona y la copia
huérfana de dos subidas del mismo pedido con distinto tipo, las dos anteriores y sin camino desde la app. Un apagado prolijo
cierra como fallido un turno tomado sin paso reservado y ese cupo no se devuelve: lo resuelve la tarea 24. Build sin
advertencias, Domain 1187, Application 6226 y Architecture 229 enteros en una copia de HEAD; integración de IA (485), de
WhatsApp (155) y de Formularios (61) con I1 e I14, y de las rutas que cambiaron con I13, B2 e I9, en verde; en el front,
tsc, lint y vitest de la feature (253).

### Etapa 4: lo que se borra

17. **I2. Borrar una conversación borra sus lecturas y transcripciones** (decisión 3). Al borrar una conversación de Inicio,
    en la misma unidad, se purga el contenido de las lecturas y normalizaciones que nombraron sus turnos (`AudioRequestId` y
    el documento sellado en el perfil), con el lock de su conversación en el orden de la casa; las otras conversaciones que
    las usaron conservan su copia sellada. Una lectura o una grabación que no llegó a ningún turno vence a las 24 horas. El
    aviso de borrar nombra los archivos y audios (`front:src/locales/{es,en}/assistant.json`). Tests de integración: borrar
    una conversación con foto y audio deja sin contenido su lectura y su transcripción, la consulta ya no las devuelve y la
    retención suelta las copias; una lectura sin turno vence a las 24 horas. Cambian las reglas de borrar
    (`inteligencia-artificial.md:97`) y de retención del audio y de los documentos (`:185`, `:203`). Chico a mediano.
18. **I3. La conversación de Inicio se borra cuando venció todo** (decisión 3). La purga trata a Inicio como a los canales: se
    borra cuando no tiene turnos activos, venció todo su contenido y ningún recibo sigue vigente, aunque nadie la haya
    retirado; y una cuenta borrada retira las suyas aunque ya no tengan contenido. El panel deja de listar esas
    conversaciones, que sólo mostraban un título genérico. Tests: una de Inicio con todo vencido se borra, y una cuenta
    borrada tres meses después de su último uso queda sin conversaciones; cambia el caso `home` de
    `AiAssistantRetentionTests`. Cambian `inteligencia-artificial.md:217` y `:221` y el punto de partida de este plan. Chico.

**Hecha el 2026-11-15:** I2 en 9f428c5c, 85c81ed0, 0632052f, 6be1683c, d5002755 y 80e36028 (front 6385f9fa y a4903420), e
I3 en 1fc155d9, 55588405, b0b675a8, bf29a75c y 1e1c8ed1 (front e4278753); los commits después del primero de cada una
corrigen lo que encontró su revisión adversarial: lo que sólo nombró una conversación retirada también vence a las 24 horas,
la lectura se encuentra por la clave de su copia, la grabación se suelta con su normalización, e Inicio tiene salida cuando la
purga ya borró la conversación. Va a la tarea 28 el costo de la purga bajo el lock global (los recibos sin índice por
conversación). Build sin advertencias, Domain 1184, Application 6216 y Architecture 229 enteros en una copia de HEAD;
integración de borrado, retención, historial, endpoints e Inicio con audio, documento y planilla (51 y 43) en verde; en el
front, tsc, lint y vitest de la feature (259).

### Etapa 5: versiones y apagado

19. **I4, la regla. Un valor nuevo del motor, un despliegue después** (decisión 4). [Inteligencia artificial](../features/inteligencia-artificial.md)
    anota la regla de `despliegue.md` para el sello del turno (propósito, archivo, modo, instrucciones, capacidades y
    terminales) y el proveedor, y los `AGENTS.md` del motor la repiten. Docs.
20. **I4, el motor. Lo que no entiende no se falla ni se niega.** La lectura del sello distingue «un valor que no conozco»
    de «dañado». Con lo primero, la consulta de una operación, la búsqueda de una propuesta y la cancelación devuelven una
    falla reintentable propia en vez de «no existe», y el claim no toma lo que no entiende: lo deja pendiente para la versión
    que lo conoce, y si nadie lo toma vence sin gastar cupo (tarea 12). El texto de `ContentBlocked` deja de invitar a borrar
    una conversación sana cuando el sello no se entiende. Tests: un turno con un valor futuro sigue pendiente después del
    claim y su consulta no da «no existe»; Formularios y WhatsApp tratan esa falla como transitoria. Lo que hace Formularios
    con un «no existe» es de su revisión. Chico a mediano.
21. **I4, los módulos. La versión sellada de una capacidad sigue valiendo.** La comparación del alcance y la huella aceptan,
    para la capacidad de un módulo, la versión sellada mientras el catálogo la conozca, en vez de exigir la que arma hoy el
    código: así un despliegue que sube una versión no hace fallar lo que quedó en vuelo (`FormReceivedDocumentExtractionPolicy`,
    `WhatsAppAssistantAccessPolicy`). Tests: una extracción y un turno de WhatsApp sellados con la versión anterior se
    recuperan después de subirla. Mediano.
22. **I4, el proveedor. Un proveedor desconocido no tira la IA.** El proveedor guardado se lee tolerante: un nombre que la
    versión no conoce es «no soportado», con la conexión pausada y el asistente no disponible, en vez de un 500 en cada
    lectura. Test: una conexión guardada con un proveedor futuro. Chico.
23. **I11. Una pestaña de Inicio abierta tolera la versión nueva** (decisión 4). `api/assistant.ts` lee las respuestas con
    esquemas que descartan lo desconocido, y lo estricto queda en los tests de contrato, que leen los ejemplos exportados;
    cambia la regla de zod estricto del `AGENTS.md` de la feature para las respuestas. Extenderlo a todo el front se decide
    con esa regla del front. Test: un cuerpo exportado con un campo de más se sigue leyendo. Chico.
24. **I12. El apagado deja terminar el paso en curso** (decisión 6). Al apagarse, la copia deja de tomar turnos pero no corta
    el que está en curso: le da un plazo de vaciado y recién ahí corta; lo que no reservó un paso en este intento vuelve a
    pendiente en vez de cerrarse como cancelado; y un corte por apagado lleva su código, sin decir que el proveedor pudo
    consumir cuando no hubo paso. Va con el plazo de apagado de N17, que se fija en el núcleo. Tests: con el host
    deteniéndose durante un paso, el paso termina; sin paso, el turno vuelve a pendiente. Chico.

**Hecha el 2026-11-16:** la 19 en 446350e1; la 20 en 5e730587, 8a0faa3f, 8b85cac4, 04365ea6 y 4fb2eb12
(front 033d8d28); la 21 en ef2d920a, 03a375f4, 80636878, 930b8779, 4d37d8f2 y c8ae6370; la 22 en 2a3bbda2, 612ab9b1 y
668f6684 (front 35f3ba7c y 15e8c94e); y la 23 en el front, en bcea09bb. Los commits después del primero de cada una corrigen
lo que encontró su revisión adversarial: sólo una versión mayor que la más alta conocida espera a otra copia, y lo retirado o
dañado cierra como antes; un turno terminado responde su estado sin leer el sello y la cancelación se registra igual; el
claim trae los pendientes en una sola consulta; las terminales y lo que el sello no admitió también aceptan la versión
sellada; y guardar sin tocar la clave de un proveedor desconocido la descarta. Un pendiente que esta copia no entiende ocupa
su lugar del tope hasta vencer. Quedan para las revisiones de Formularios y WhatsApp: Formularios convierte una falla de la
búsqueda de una propuesta en 404 o en 500, aplica el tope de la extracción sólo a un «no existe» y relee los 20 más viejos;
los botones de WhatsApp contestan «no se pudo» ante cualquier falla de la consulta; y quienes leen el resultado (`Handles`,
`FormDocumentAuthoringPolicy`, `WhatsAppAssistantReplyPolicy`) comparan la versión exacta, que la regla pide cambiar con la
primera suba. Build sin advertencias, Domain 1184, Application 6282 y Architecture 229 enteros en una copia de HEAD;
integración de IA (49) y de WhatsApp y Formularios (100) en verde; en el front, tsc, lint y vitest de la feature (264).

La 24 se hizo después, en 680e88b5, cuando N17 fijó el apagado del host en 25 s. Las correcciones de su revisión
adversarial son 8c2661ad, c3863530, 08733beb y 0aa4fc03:
- la devolución se cuenta sólo si el turno volvió a pendiente;
- la parada no deja un Warning;
- el claim no lo corta la parada;
- después del vaciado toda falla lleva `Interrupted`, salvo el turno que canceló su persona.

El vaciado es el apagado del host menos el margen para cerrar el turno (hoy 18 s). Desde N8 (e7ce1bbc) el host frena todos
los servicios a la vez, así que no demora a los demás workers. Build sin advertencias, Domain 1191, Application 6352 y
Architecture 239 enteros; integración del apagado, la traza, la recuperación, la ejecución y los coordinadores (50) y de
WhatsApp (116 con la primera versión) en verde.

### Etapa 6: la traza y el anillo

25. **I10, la traza. Una consulta se sigue a través del motor.** Como H19: una fuente de trazas del asistente registrada con
    `AddSource`; el contexto de quien llama, guardado en el turno al aceptar, en una columna opcional (por WhatsApp hereda
    el de su vuelta y en Inicio el del pedido); una actividad por turno en el coordinador que lo continúa, con el proveedor y
    las capacidades adentro; y el turno en un scope de los logs del worker. Migración aditiva. Tests: el turno guarda el
    contexto y el coordinador abre su actividad con él. Chico a mediano.
26. **I17. Una copia con otro anillo no queda lista** (decisión 1). Un valor de control por clave del anillo, guardado en la
    base: la primera copia lo escribe y cada copia que arranca lo recalcula; si no coincide, un chequeo de salud de IA falla
    readiness con un log claro, sin descifrar datos ni llamar al proveedor. Las tres huellas tratan una clave ausente como
    «no se puede verificar» y no como una excepción, que hoy da 500. Migración aditiva: una tabla chica del módulo, que
    [quitar IA](../guides/quitar-ia.md) nombra. Test de integración: una copia con otro `InstallationId` sobre una base con
    el control escrito no queda lista. Chico a mediano.

**Hecha el 2026-11-16:** la 25 en 4a98eab1, y la 26 en 321eeb8e. Los commits que siguen corrigen lo que encontró la revisión
adversarial de cada una: los de la 25 son 58bbcf9a, aa7ba2e2, ae1744f3 y 20ffb4b7; los de la 26, 3f70b320, 9f9d6cc6,
005f10ee, 53164b17 y 14030ad4 (a0d451f3 sacó de 9f9d6cc6 un párrafo ajeno). La traza marca como error todo turno que
termina fallido, también los que cierra el propio motor, y el sondeo de cancelación corre fuera de ella. Con controles
guardados, una copia queda lista sólo si su clave activa ya tiene control y coincide, y coinciden todas las que comparte con
la base. El veredicto vive en la copia y lo consultan la readiness, el claim, la disponibilidad y la aceptación: una copia
desalineada tampoco toma turnos ni acepta por WhatsApp o Formularios. El bootstrap de Inicio y el chequeo previo a subir un
documento no lo consultan, pero la aceptación igual rechaza. Build sin advertencias, Domain 1190, Application 6342 y
Architecture 229 enteros; integración de la traza (37), del anillo, del coordinador, de la salud y del motor que usan
WhatsApp en verde.

### Etapa 7: lo trivial de gran escala

27. **I20, el bootstrap. Abrir Inicio no mide toda la tabla.** El bootstrap pide sólo la cantidad de pendientes, que resuelve
    el índice por estado, en vez de `FindWorkloadAsync`, que queda para el worker. Test: el bootstrap no llama a la
    medición. Trivial.
28. **I20, los índices. La purga y la medición usan índice.** Índices parciales sobre los turnos que todavía tienen contenido
    (`ContentExpiresAtUtc`, `Id`) y sobre las conversaciones retiradas, con la selección de la purga y la medición partida en
    consultas que los usen: vencidos, retiradas y dueños borrados. Migración según la [guía](../guides/migracion.md). Test de
    PostgreSQL: con miles de turnos purgados, el plan de la purga no recorre la tabla. Chico.
29. **I19, el claim. El claim cuenta sólo lo que está en curso.** El claim deja de contar los recibos del día y cuenta sólo
    los turnos en curso, que es lo único que usa. Test: con la capacidad llena, el claim no cuenta recibos. Trivial.

**Hecha el 2026-11-16:** la 27 en 9e1e2cbc, la 28 en 9bbbf02c y la 29 en faf6381f. La revisión adversarial de la 28, que
cambia qué elige la purga, comparó la consulta vieja con la nueva sobre 45.000 turnos con topes de 1 a 5000: eligen los
mismos turnos en el mismo orden. Sus correcciones son 888f4113 (un test que fija el orden entre motivos con un lote chico),
6f937b59 (el `EXPLAIN` corre con `enable_seqscan` apagado para no depender de las estadísticas de la base compartida) y
e4fc3671 y 83bc2748 (texto). Partir la purga en una consulta por motivo no acorta el tiempo bajo el candado: lo que saca la
historia es el índice parcial, y la partición evita recorrer todas las conversaciones. La parte activa de las conversaciones a
borrar todavía revisa cada conversación activa por índice; evitarlo pide cambiar el esquema y queda con el resto de gran
escala. Build sin advertencias, Domain 1190, Application 6342 y Architecture 229 enteros; integración del bootstrap (23), de
la retención y la purga (28, y 24 cinco veces seguidas después de las correcciones) y del claim (13) en verde.

**Cierre, el 2026-11-17:** la corrida completa quedó en verde. El backend, sobre `c82ec50d`, dio 11.641 de 11.641 con la
exportación conjunta de las 28 cohortes HTTP (front `9aa157ae`). El front dio tipos, lint, arquitectura (56), los 6.002 tests
y el build. Hicieron falta tres exportaciones, y lo que destaparon se arregló así:
- 911a89c5 y c82ec50d: dos siembras de prueba que se apartaban del editor y quedaban en la base compartida;
- 5adc6e96: la migración de la lectura de documentos;
- 9cbf0ad0: la ubicación de una planilla de WhatsApp;
- 0e3bd943: Redis, del núcleo;
- en el front, ecc0f561 y los plazos de 571503cf, b618d58b, 925ab958, 1e877620 y f027d83f.

Queda la prueba manual, que va en la prueba integral de la tanda.

### Lo que quedó de los arreglos

Juntado el 2026-11-18 de las etapas de arriba y de la [revisión](../reviews/2026-11-14-arquitectura-ia.md), y comprobado en
el código ese día. Ninguno traba la prueba integral.

- **Antes de pasar de una réplica:** I18, que el barrido de archivos privados borre sólo bajo los prefijos que alguien
  declaró (núcleo, chico).
- **Con volumen real que medir** (van con los pendientes 3, 5 y 6): el resto de I19 (autorizar, descifrar y armar el
  catálogo antes de los candados, la conexión compartida, los pasos sin el candado de presupuesto y contadores por día) y de
  I20 (herramientas y dueños del lote en una consulta, la purga sin el candado de presupuesto, un índice para la retención
  de documentos y la parte activa de las conversaciones a borrar).
- **Con la próxima versión de instrucciones que haya que calificar** (decisión 7): fechas y números sólo con números, la
  frase del permiso y `Prompt.SourceTime`, que en Inicio dice «zona del negocio» aunque sea la del perfil.
- **Sin fecha:** el corte que falla rápido después de varios rechazos (I6); el aviso de una clave ilegible en
  Configuración › IA para quien sólo tiene `ai.read` (I7); que la prueba de la tanda falle con un solo carril, que pide
  rehacer el proveedor ficticio de integración (I1); la clave de la copia guardada que no dependa de la persona y la copia
  huérfana de dos subidas del mismo pedido con distinto tipo, sin camino desde la app (I13); y el veredicto del anillo en el
  bootstrap y en el chequeo previo a subir un documento (I17), que hoy rechaza la aceptación.
- **Observaciones menores:** siguen abiertas O1 (el registro de los proveedores corre dos veces), O2 y O13 (a confirmar con
  una cuenta real), O3, O4 (la prueba de conexión llama incierto a un corte antes de enviar), O6, O8 (el nombre del archivo
  conserva los caracteres bidireccionales), O9, O10, O11, O12 y O14 (`KnownInstructions` acepta «la 1 o la actual»). De O5
  queda sólo lo opcional (la huella con una clave ausente ya da 409, I17), y O7 lo acota I9: la ruta limita el cuerpo.
