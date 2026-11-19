# Motores configurables: diseño de referencia

Borrador del 2026-11-19 para el [ADR 0033](../decisions/0033-motores-configurables-para-procesos.md), que sigue en estado
Propuesta. **No hay nada programado.** Baja el ADR a archivos, clases, tablas, tecnología y controles, para que se pueda revisar
antes de escribir código. Los procesos que lo piden están en [procesos del campo](../plans/2026-11-19-procesos-del-campo.md). La
investigación con fuentes está al final.

## 1. El problema

Cualquier cliente tiene que poder armar sus procesos sin que programemos nada para él, a mano o con la IA. Hay tres riesgos:

1. **Código por todos lados.** Si cada proceso es una pantalla, el código crece con cada cliente y nadie lo puede mantener.
2. **Una IA cara o impredecible.** Si el modelo interpreta o calcula, cada operación gasta tokens y el resultado cambia de una
   vez a otra.
3. **Una plataforma dentro de la plataforma.** Si la configuración admite fórmulas, scripts o condiciones libres, termina siendo
   un lenguaje de programación peor que C#, imposible de validar y de explicar.

Las plataformas configurables que funcionan lo resuelven igual. Frappe/ERPNext tiene DocType, Workflow, Notification, Assignment
Rule y Scheduler, y para el stock usa Stock Ledger Entry y Bin. Odoo usa `stock.move` y `stock.quant`, más sus automatizaciones.
Salesforce tiene metadatos con un orden de ejecución fijo y límites por transacción. En las tres:

- los conceptos son código fijo;
- la configuración sólo declara cómo se combinan;
- hay límites explícitos;
- **el stock nunca es configurable en su mecánica**: el cliente dice qué suma y qué resta, y el motor hace el resto.

Este diseño sigue esa receta.

## 2. Cómo crece sin casuística

El código tiene una cantidad chica y cerrada de conceptos: los cinco de hoy y los cinco nuevos. Cada proceso de un cliente es una
combinación de definiciones.

| Concepto (motor) | Estado | Qué declara un cliente |
| --- | --- | --- |
| Formularios | existe | Preguntas, bloques, cálculos, condiciones |
| Catálogos | existe | Listas con campos, filtros por persona |
| Reglas | existe | Al recibir, aprobar o rechazar: avisar, pedir aprobación, armar el PDF, emitir |
| Tablero | existe | Cuántas, totales y repartos de un formulario |
| Biblioteca | existe | Estándares listos para agregar |
| **Saldos** | nuevo | Qué formularios suman o restan, en qué saldo, según qué datos |
| **Flujos** | nuevo | Estados, quién los cambia, qué se completa en cada uno, vencimientos |
| **Agenda** | nuevo | Qué pasa a una hora, antes de una fecha o si algo falta |
| **Relaciones** | nuevo | Una pregunta que elige otra respuesta; un catálogo que sale de un formulario |
| **Consultas** | nuevo | Una pregunta tipada sobre las respuestas o los saldos |

Procesos de [procesos del campo](../plans/2026-11-19-procesos-del-campo.md) armados sólo con definiciones, sin código nuevo:

| Proceso | Definiciones que lo arman |
| --- | --- |
| Salida de galpón que descuenta stock | Formulario «Salida» · catálogos Depósitos y Productos · saldo «Stock» (resta) · regla «bajo el mínimo, avisar» |
| Orden de trabajo al contratista | Formulario «Orden» · flujo pendiente → asignada → ejecutada → confirmada · agenda «vence en 3 días» |
| Salida repartida entre órdenes | Formulario «Salida» con un bloque cuya pregunta elige una «Orden» (relación) · saldo «Stock» |
| Lluvia con resumen semanal | Formulario «Lluvia» · consulta «milímetros por campo y mes» · agenda «los lunes, mandar el resumen» |
| Movimiento de hacienda | Formulario «Movimiento» · saldo «Hacienda» por potrero y categoría (suma en destino, resta en origen) |
| Silobolsa armada y extraída | Formularios «Armado» y «Extracción» · saldo «Grano» por bolsa · catálogo «Bolsas activas» que sale del armado |
| Vencimiento de un contrato | Catálogo «Contratos» con su fecha · agenda «30 días antes, avisar» |
| «¿Cuánto glifosato queda?» por WhatsApp | La IA usa la capacidad de leer un saldo; no hay definición nueva |

**Qué sí requiere código:** un concepto nuevo que ninguno de estos expresa, como los mapas o la carga sin conexión. Se decide como
producto, se diseña una vez y queda para todos. Un pedido que no entra en un concepto existente se contesta «todavía no» y queda
anotado como faltante (ADR 0033, punto 5).

## 3. Anatomía de un motor

Todos los motores tienen las mismas siete piezas, en las carpetas de siempre del módulo
([backend](../architecture/backend.md#módulos-opcionales)):

| Pieza | Qué es | Dónde |
| --- | --- | --- |
| Definición | Records C# que son el esquema; se guardan como JSON en una revisión inmutable | `Application/Modules/BusinessForms/Models/<Concepto>/` y una entidad de revisión en `Domain` |
| Validador | Reglas de negocio de la definición, el mismo para la pantalla, la IA y la biblioteca | `Application/.../Validation/<Concepto>/` |
| Motor | Servicio que interpreta la definición: posteo, transición, disparo o consulta, con su política pura | `Application/.../Services/<Concepto>/` |
| Persistencia | Repositorio, lector, configuración EF y, si hace falta, SQL propio | `Infrastructure/Modules/BusinessForms/Persistence/` |
| Trabajo de fondo | Sólo si corre solo: un worker con lease y fence, como `FormActionBackgroundService` | `Infrastructure/Modules/BusinessForms/Workers/` |
| Borde | Controller, contratos HTTP y permiso | `Api/Modules/BusinessForms/` y `Domain/.../BusinessFormsPermissions.cs` |
| Capacidades y acciones de IA | Leer un resumen, proponer una definición y, si aplica, leer un dato, por `IAssistantCapabilitySource`; las cargas y lo que su pantalla crea, edita o elimina, como acciones declaradas ([ADR 0034](../decisions/0034-acciones-del-asistente.md)) | `Application/.../Channels/` |

Más el editor y la vista en el front (`ArquitecturaBaseFront/src/features/business-forms/<concepto>/`) y los tests de cada capa.

### El ejemplo completo: saldos

```text
src/ArquitecturaBase.Domain/Modules/BusinessForms/Ledgers/
  BusinessFormLedger.cs              el saldo declarado: nombre, revisión activa, estado
  BusinessFormLedgerRevision.cs      revisión inmutable con DefinitionJson (como BusinessFormRuleSet)
  BusinessFormLedgerEntry.cs         un movimiento, sólo de alta
  BusinessFormLedgerBalance.cs       el saldo materializado de una clave
  FormLedgerErrors.cs                errores con código estable (Forms.Ledger.*)

src/ArquitecturaBase.Application/Modules/BusinessForms/
  Models/Ledgers/LedgerDefinition.cs          el esquema: records con [Description]
  Validation/Ledgers/LedgerDefinitionValidator.cs
  Interfaces/Services/IFormLedgerService.cs
  Interfaces/Persistence/IFormLedgerRepository.cs, IFormLedgerReader.cs, IFormLedgerPostingStore.cs
  Services/Ledgers/
    FormLedgerService.cs             guardar una definición: revisión esperada, 409, una UoW
    FormLedgerPostingPolicy.cs       de una respuesta a movimientos; puro y sin base
    FormLedgerPostingRecorder.cs     postea dentro de la UoW de la captura, la corrección o la anulación
    FormLedgerRebuildService.cs      reconstruye una clave desde el libro y compara
  Channels/FormLedgerAssistantCapabilitySource.cs

src/ArquitecturaBase.Infrastructure/Modules/BusinessForms/Persistence/
  Configurations/BusinessFormLedger*Configuration.cs
  Repositories/FormLedgerRepository.cs
  Readers/FormLedgerReader.cs        saldos y movimientos con paginado por keyset
  FormLedgerPostingStore.cs          el único SQL propio: insertar movimientos y sumar al saldo

src/ArquitecturaBase.Api/Modules/BusinessForms/
  Controllers/FormLedgersController.cs
  Contracts/Ledgers/SaveFormLedgerHttpRequest.cs, FormLedgerBalancesQuery.cs

ArquitecturaBaseFront/src/features/business-forms/ledgers/     editor del saldo y vista con sus movimientos
tests/ArquitecturaBase.Application.UnitTests/.../Ledgers/      la política de posteo, sin base
tests/ArquitecturaBase.IntegrationTests/BusinessForms/Ledgers/ concurrencia, idempotencia y reconstrucción
harnesses/assistant-qualification/                             casos de ejemplo de sus capacidades
```

**La definición es el esquema** (ilustrativo):

```csharp
public sealed record LedgerDefinition(
    [property: Description("Balance name in the original culture, like «Stock de insumos».")] string Name,
    [property: Description("One or two catalog-backed dimensions, like warehouse and product.")]
        IReadOnlyList<LedgerDimension> Dimensions,
    [property: Description("Published forms that add to or subtract from this balance.")]
        IReadOnlyList<LedgerSource> Sources,
    [property: Description("Optional minimum per key that raises a notice.")] LedgerMinimum? Minimum);

public sealed record LedgerDimension(string Key, Guid CatalogId);

public sealed record LedgerSource(
    Guid DefinitionId, LedgerSign Sign, Guid QuantityFieldId,
    IReadOnlyList<LedgerBinding> Bindings);            // qué pregunta llena cada dimensión

public enum LedgerSign { Add, Subtract }
```

De estos records sale todo lo demás:

- el JSON que se guarda en la revisión;
- el esquema que ve el modelo, generado con `JsonSchemaExporter` y adaptado al modo estricto (sección 5);
- el contrato HTTP del editor.

No hay un segundo esquema escrito a mano. El validador agrega lo que un esquema no expresa:

- que el formulario esté publicado;
- que la pregunta de cantidad sea un número;
- que cada dimensión tenga su pregunta;
- un tope de fuentes y de dos dimensiones.

**El posteo** corre en la misma transacción que guarda la respuesta. No hay un trabajo aparte ni una consistencia eventual: el saldo
cambia o no cambia junto con la respuesta.

```csharp
// FormLedgerPostingRecorder, dentro de la UoW de la captura (ADR 0001)
var entries = FormLedgerPostingPolicy.Entries(record, activeLedgers);        // puro: qué movimientos genera
var ordered = entries.OrderBy(e => e.Key, LedgerKey.Comparer);               // orden fijo: sin deadlocks
var balances = await postingStore.PostAsync(ordered, ct);                    // idempotente por (saldo, respuesta, ítem)
foreach (var crossed in FormLedgerPostingPolicy.BelowMinimum(balances, activeLedgers))
    notices.Add(crossed);                    // como una acción de regla: se guarda con la respuesta y la entrega su worker
```

```sql
-- FormLedgerPostingStore: el movimiento, una sola vez por origen
INSERT INTO "BusinessFormLedgerEntries" ("Id","LedgerId","KeyHash","Quantity","SourceRecordId","SourceRow",...)
VALUES (...)
ON CONFLICT ("LedgerId","SourceRecordId","SourceRow","Kind") DO NOTHING
RETURNING "Id";

-- y, sólo si el movimiento se insertó, el saldo con un incremento atómico (sin leer y volver a escribir)
INSERT INTO "BusinessFormLedgerBalances" ("LedgerId","KeyHash","Dim1","Dim2","Quantity","Revision")
VALUES (@ledger, @key, @d1, @d2, @delta, 1)
ON CONFLICT ("LedgerId","KeyHash")
DO UPDATE SET "Quantity" = "BusinessFormLedgerBalances"."Quantity" + EXCLUDED."Quantity",
              "Revision" = "BusinessFormLedgerBalances"."Revision" + 1
RETURNING "Quantity";
```

**Una corrección** no edita movimientos: escribe la reversa de los de la respuesta reemplazada y los nuevos. **Una anulación**
escribe sólo la reversa. **Un conteo** es un movimiento de ajuste por la diferencia y avisa si el saldo se movió después de contar.
La reconstrucción agrupa el libro por clave y lo compara con el saldo materializado. Corre a pedido y en un control periódico,
porque ERPNext documenta saldos desfasados y conviene detectarlos desde el primer día.

### Los otros motores, en corto

**Flujos.** La definición declara:

- los estados;
- las transiciones, con quién las puede hacer: un rol o la persona asignada;
- qué preguntas se completan en cada estado;
- el vencimiento de cada estado.

La instancia vive en la respuesta: estado, persona asignada, revisión del flujo y vence. Su historial es de sólo alta.

- **Una transición** es un caso de uso con revisión esperada y 409. Cambia el estado, deja el historial y encola los avisos en la
  misma transacción.
- **El vencimiento** es una fila de la agenda, creada y cancelada en esa misma transacción.
- **La aprobación de hoy** es un caso particular de un flujo. Se puede expresar como uno sin tener que migrarla de entrada.

```text
Domain/.../Flows/        BusinessFormFlowRevision.cs, BusinessFormRecordFlowState.cs, BusinessFormFlowTransition.cs, FormFlowErrors.cs
Application/.../Flows/   FlowDefinition.cs, FlowDefinitionValidator.cs, FormFlowTransitionService.cs, FormFlowPolicy.cs (puro)
```

**Agenda.** Hay una tabla de próximos disparos y la reserva y la confirmación son las del patrón de siempre. La política y el
cálculo del siguiente disparo son puros:

- cada fila guarda la definición, `NextFireAtUtc`, la zona IANA, la política de disparos perdidos y una clave única
  «disparo + hora»;
- un cron se calcula con Cronos;
- «X días antes de una fecha» se recalcula al guardar la respuesta, en la misma transacción;
- «si a las 18 no se cargó» se evalúa al disparar, con el estado de ese momento.

```text
Domain/.../Schedules/          BusinessFormSchedule.cs, BusinessFormScheduledTrigger.cs, FormScheduleErrors.cs
Application/.../Schedules/     ScheduleDefinition.cs, ScheduleDefinitionValidator.cs, FormScheduleService.cs, FormScheduleFirePolicy.cs (puro)
Infrastructure/.../Workers/    FormScheduleBackgroundService.cs
```

**Relaciones.** Suma dos cosas:

- un tipo de pregunta, «Respuesta de otro formulario», con filtros por estado y por persona asignada. Guarda la referencia, como
  hoy una opción de catálogo, y sella los datos que trae;
- un tipo de catálogo `FromForm`, que es una proyección de las respuestas de un formulario actualizada al capturar, como la copia
  local de un `External`.

```text
Application/.../Relations/     RecordReferenceSource.cs (la pregunta), FormRecordReferenceReader.cs, FormCatalogFromFormProjector.cs
```

**Consultas.** Es un DTO tipado que reusa la gramática de filtros de la planilla (`FormRecordFilterPolicy`) y la suma en SQL del
tablero (`FormDashboardReader`):

- se elige un formulario o un saldo;
- se filtra como en la planilla;
- se agrupa por hasta dos datos;
- se calcula una métrica (cuántas, suma, promedio, mínimo o máximo) en un período.

Lo usan los tableros que cruzan formularios y la capacidad «consultar» de la IA, siempre con los permisos de quien pregunta y con
tope de filas. La IA nunca escribe SQL (ADR 0019).

```text
Application/.../Queries/       RecordQuery.cs, RecordQueryValidator.cs, FormRecordQueryService.cs
Infrastructure/.../Readers/    FormRecordQueryReader.cs (compila a SQL con parámetros, como FormResponseQuery)
```

## 4. Cómo se ejecuta

```text
 Navegador / WhatsApp
        │  pedidos HTTP con sesión, requestId y revisión esperada
        ▼
 Api (N copias sin estado)  ── controller → servicio → UoW ──►  PostgreSQL
        │                                                        ▲  definiciones, respuestas, movimientos, saldos,
        │ propuestas (DTO)                                       │  estados, disparos, colas transaccionales
        ▼                                                        │
 Motor de IA ── capacidades por módulo ──► proveedor del modelo  │
                                                                 │
 Workers (en cada copia): acciones, publicación, agenda ─────────┘  reservan con lease y fence, confirman con el fence vigente
 Redis: opcional (ADR 0032)        Azure Blob: archivos privados
```

- **La Api no guarda estado.** Todo vive en PostgreSQL. Con más copias se suman réplicas detrás del balanceador. Los workers
  pueden quedarse dentro de la Api, como hoy, o pasar a un proceso aparte del AppHost si la carga lo pide, con un host nuevo y
  sin cambiar su lógica: el patrón de reserva ya soporta varias copias.
- **Un solo patrón de trabajo de fondo,** el de hoy: un candidato se reserva con un lease y un fence; el efecto corre fuera de la
  transacción; se confirma sólo si el fence sigue siendo el suyo; lo incierto se consulta antes de repetir
  ([robustez proporcional](../architecture/backend.md#robustez-proporcional-al-efecto)). La agenda lo reusa, sin un segundo
  sistema de colas.
- **Los avisos salen como hoy las acciones de las reglas:** se guardan en la misma transacción que el hecho, como un run, y un
  worker los entrega por el canal. Es un outbox, y el canal usa sus colas transaccionales
  ([ADR 0015](../decisions/0015-colas-transaccionales-cifradas.md)).

### Tecnología elegida y descartada

| Necesidad | Elegido | Descartado y por qué |
| --- | --- | --- |
| Flujos | Máquina de estados propia, declarada en datos. Stateless, si acaso, sólo para validar el grafo en tests | **Elsa 3:** trae su runtime, su diseñador y su esquema, depende de MassTransit 8 sin camino a la v9 y duplica los workers. **Temporal:** otro clúster y flujos escritos en código. **MassTransit:** la v9 es comercial y sus máquinas son código. **Camunda 8:** licencia Enterprise para producción. **Workflow Core:** poca actividad |
| Agenda | Tabla de próximos disparos con el worker de siempre, más **Cronos** (MIT) para el cron con zonas horarias y cambios de horario | **Quartz.NET:** muy bueno, pero sería un segundo sistema de reservas con su propio esquema; es la opción si algún día se delega la agenda. **Hangfire:** su almacenamiento en PostgreSQL es comunitario y lo pago es propietario. **TickerQ:** cambió a una licencia con código visible pero no libre. **Coravel:** en memoria, pierde disparos |
| Saldos | Libro de sólo alta más saldo materializado, con incremento atómico en Read Committed | **Marten:** un segundo modelo de persistencia fuera de EF para un problema de saldos. **Leer, sumar y escribir** el saldo, como el Bin de ERPNext: pierde actualizaciones con concurrencia |
| Esquemas | Records C# → `JsonSchemaExporter` (.NET 9 o posterior) → variante estricta para el modelo; validación con JsonSchema.Net (2020-12) y el validador de negocio | Esquemas escritos a mano como texto, como hoy en los codecs: se desalinean del DTO y del validador |
| Cliente del modelo | El motor propio. Microsoft.Extensions.AI opcional detrás, como cliente intercambiable con telemetría, sin que ejecute herramientas solo | **Semantic Kernel** (en mantenimiento) y **Agent Framework:** pensados para agentes autónomos; acá el modelo sólo propone |

### Patrones, con su lugar

| Patrón | Dónde |
| --- | --- |
| Intérprete de definiciones versionadas | Cada motor interpreta su revisión. Lo cargado guarda la revisión con que llegó |
| Especificación compilada a SQL | La gramática de filtros: planilla, reglas, consultas |
| Estrategia por concepto | Una fuente de capacidades y un servicio por motor; el motor de IA no los conoce |
| Máquina de estados | Flujos |
| Libro de sólo alta con vista materializada | Saldos |
| Outbox | Avisos y efectos externos, por las colas transaccionales |
| Receptor idempotente | `requestId` en cada escritura y claves únicas de origen en movimientos y disparos |
| Lease con fence token | Todos los workers |
| Concurrencia optimista | Revisión esperada y 409 en definiciones y transiciones |
| Lock por clave | Saldos, sólo para validaciones que bloquean |
| Puertos y adaptadores | Entre módulos, como hoy (`ModuleBoundaryTests`) |

## 5. Cómo se adapta la IA cuando el sistema crece

1. **Las capacidades se descubren solas.** El motor de IA recibe todas las fuentes registradas (`IEnumerable<IAssistantCapabilitySource>`)
   y en cada turno ofrece sólo las del canal y las que permiten los permisos de la persona (`AssistantChannelContext`). Un motor
   nuevo registra su fuente y el modelo la ve en el turno siguiente, sin tocar el motor de IA ni unas instrucciones globales.
   Las acciones de cada pantalla se registran igual, en el registro del núcleo del
   [ADR 0034](../decisions/0034-acciones-del-asistente.md).
2. **Cada capacidad lleva su propio contexto:** un id con versión, una descripción, el esquema de entrada y de salida, el permiso
   y el presupuesto (`AssistantCapabilityDefinition`). Para armar algo que se apoya en lo que existe, el modelo primero lee
   resúmenes acotados (los formularios, los catálogos, los saldos) y propone con ids reales.
3. **Pocas herramientas por turno.** OpenAI sugiere menos de 20 y Anthropic ve caer la precisión pasadas 30 a 50. Las cargas y
   lo que una pantalla crea, edita o elimina no suman herramientas: son acciones que el asistente propone con tres piezas
   comunes (ADR 0034), y la elección pasa a ser la de una acción en «lo que puede hacer», filtrado por permisos y módulo y con
   un tope. Las capacidades de leer y de armar definiciones se ofrecen por canal, por permiso y por modo (armar o consultar),
   con prefijos por concepto (`forms.ledgers.*`, `forms.flows.*`). Si ese catálogo crece más, se suma una búsqueda de
   herramientas o una elección en dos pasos (primero el concepto, después la herramienta). El conjunto de herramientas de cada
   perfil se mantiene estable, para no romper la caché.
4. **Armar un proceso entero** es una capacidad compuesta: un formulario, sus catálogos, sus saldos, su flujo y su agenda. Cada
   parte pasa por el validador de su motor, la persona ve todo junto y «Crear» deja borradores e inactivos en una sola
   transacción. Si el esquema compuesto pasa los límites del modo estricto, se arma en pasos.
5. **Esquemas en modo estricto.** El esquema generado se adapta: todos los campos en `required`, los opcionales como `null` y
   `additionalProperties: false`, sin `allOf`, `not` ni `if/then/else`. Un test de arquitectura falla si una capacidad pasa los
   límites de OpenAI: 10 niveles, 5.000 propiedades y 1.000 valores de enumeración.
6. **Calidad medida.** Cada capacidad tiene su conjunto de casos (ADR 0020, punto 6): pedidos correctos, ambiguos, ataques
   escondidos en documentos y pedidos que no tienen que activarla. Se corre en `harnesses/assistant-qualification` antes de
   cambiar de modelo, de instrucciones o de descripción, y se mide el acierto por herramienta y por acción.

### Costo

- **Sólo en los bordes.** Interpretar un mensaje es una llamada, más la transcripción si es un audio. Guardar, postear,
  transicionar, disparar y consultar no llaman al modelo.
- **Con tope.** Cada capacidad declara bytes de entrada y de salida, ítems y tiempo. Hay cupos por persona y por día, y en el
  multitenant, también por organización.
- **La caché del proveedor.** Lo estable va primero: instrucciones, herramientas y referencias. Lo de la persona va al final. En
  OpenAI el prefijo cacheable empieza en 1.024 tokens y se cobra a una fracción del precio de entrada. `prompt_cache_key` va por
  organización.
- **Medido por operación:** tokens de entrada, de salida y cacheados, por capacidad y versión, con el uso que ya registra el
  [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md).

### Seguridad (OWASP Top 10 para LLM, 2025)

- **Inyección de instrucciones (LLM01).** Un documento, un audio o un texto son datos, nunca instrucciones. Como ninguna capacidad
  tiene efectos, lo peor que logra una inyección es una propuesta equivocada que ve una persona, con el origen de cada dato.
- **Salida insegura (LLM05).** Toda salida pasa por el esquema y el validador. Nunca se arma SQL, una ruta o una plantilla con
  texto del modelo.
- **Agencia excesiva (LLM06).** Las capacidades y las acciones sólo leen y proponen. Al aplicar, el servidor revalida los permisos de quien
  aprueba.
- **Fuga de datos (LLM02).** Para armar, el modelo no recibe respuestas ni datos de personas (ADR 0024). Para consultar, recibe
  como mucho un resumen con los permisos de quien pregunta.
- **Consumo sin límite (LLM10).** Presupuestos, cupos y un tope de vueltas por turno.

## 6. Concurrencia, pestañas y carga: los 30 criterios

Contra la [guía de revisión](../guides/revisar-arquitectura.md), en los tres horizontes. Lo que no se nombra se resuelve como en
el resto de Formularios.

| Criterio | Cómo lo cubre el diseño |
| --- | --- |
| 1. Estado por pedido | Servicios scoped. Ningún motor guarda estado en memoria; las definiciones vigentes se leen por revisión |
| 2. Ediciones simultáneas | Dos pestañas que guardan la misma definición o mueven el mismo estado: la segunda recibe 409 con lo vigente y decide |
| 3. Repeticiones | `requestId` en cada escritura. Únicos de origen en movimientos (saldo, respuesta, ítem y tipo) y en disparos (disparo y hora) |
| 4. Orden | Las claves de saldo se toman en un orden fijo. La transición pide la revisión esperada del registro |
| 5. Todo o nada | La respuesta, sus movimientos, su estado, su historial y sus avisos van en una sola transacción, sin HTTP adentro |
| 6. Cada uno lo suyo | Un permiso por motor. El dueño del registro se controla en el servicio. En el multitenant, organización en cada tabla |
| 7 y 8. Varias copias | Sin colas en memoria. La agenda y los avisos se reservan con lease y fence, y cada disparo corre una vez |
| 9 y 10. Arranque y despliegue | Opciones validadas al arrancar. Una definición lleva su versión de esquema y codec. Las migraciones agregan: nada se renombra ni se borra en el mismo paso |
| 11 a 13. Consultas y volumen | Índices para cada lectura: clave y fecha para los movimientos (keyset) y un parcial sobre `NextFireAtUtc` para la agenda. Topes en consultas y exportaciones |
| 15. Trabajo pesado | La IA, el PDF y el Excel siguen fuera del pedido, como operaciones durables |
| 16. Filas únicas | El único punto de serialización es un saldo muy usado. Se mide con una prueba de carga; si hace falta, el saldo se reparte en varias filas que se suman al leer |
| 17 a 20. Terceros y costo | El modelo tiene plazos, cupos y presupuesto. Si la IA no está, todo se sigue armando a mano: los dos editores son la degradación |
| 21 a 23. Seguridad | La sección 5. Logs con `[LoggerMessage]`, sin valores de respuestas |
| 24. Integridad | Claves foráneas, únicos y checks: un saldo por clave, un movimiento por origen, un estado por registro |
| 25. Privacidad | Los movimientos son historia: entran en la retención de Formularios, que se decide antes de producción |
| 27. Fechas | La agenda calcula en la zona IANA del negocio. Están definidos los cambios de horario (la hora que no existe y la que se repite) y los disparos perdidos al reiniciar |
| 29. Observabilidad | Cada motor tiene sus actividades en el `ActivitySource` de Formularios y su historial auditable |
| 30. Separabilidad | Todo vive en el módulo de Formularios. Lo que otro módulo necesita va por un puerto |

## 7. Cómo se prueba y cómo se sabe que funciona

- **Las políticas, sin base:** posteo, reversas, transiciones permitidas y próximos disparos con `FakeTimeProvider`, incluidos el
  cambio de horario y el disparo perdido.
- **Con PostgreSQL** (Testcontainers):
  - cien posteos en paralelo sobre la misma clave dan el saldo exacto;
  - un `requestId` repetido produce un solo efecto;
  - dos pestañas que mueven el mismo estado dan un 409;
  - la reconstrucción coincide con el saldo;
  - dos copias del worker no disparan dos veces.
- **Arquitectura:**
  - las capacidades y las fuentes de acciones no escriben;
  - ningún SQL propio fuera de los lectores y del store de posteo;
  - los motores no nombran otros módulos;
  - los esquemas respetan los límites del modo estricto.
- **La IA:** el conjunto de casos de cada capacidad, calificado contra el modelo real.
- **Antes de elegir un horizonte, dos pruebas acotadas:**
  - carga sobre la captura con posteo en un saldo muy usado (por ejemplo con k6 o NBomber), midiendo p95 y esperas de locks;
  - la precisión al elegir: una acción en «lo que puede hacer», con su tope, y una capacidad con 20 a 30 ofrecidas (las de leer
    y armar definiciones).

## 8. Orden de construcción

Para cada motor:

1. el esquema y su validador;
2. el motor y su persistencia;
3. el editor a mano;
4. las capacidades de IA (leer y armar su definición), las acciones de sus cargas y su calificación;
5. la documentación en `features`.

Antes que todo:

- las dos pruebas acotadas de la sección 7;
- generar los esquemas desde los records;
- los tableros de cada pantalla, que se dibujan antes de programarse.

**Lo que no se hace:**

- código por cliente o por proceso;
- fórmulas o scripts en una definición;
- SQL o código escrito por el modelo;
- JSON sin esquema;
- sumar saldos recorriendo respuestas en memoria;
- programar la agenda en memoria;
- un segundo sistema de colas.

## Fuentes

- Plataformas:
  - Frappe y ERPNext: https://docs.frappe.io/framework/user/en/basics/doctypes, https://docs.frappe.io/erpnext/stock-ledger y
    https://github.com/frappe/erpnext/blob/develop/erpnext/stock/stock_ledger.py
  - Odoo: https://github.com/odoo/odoo/blob/master/addons/stock/models/stock_quant.py y
    https://www.odoo.com/documentation/18.0/applications/studio/automated_actions.html
  - Salesforce: https://architect.salesforce.com/fundamentals/platform-multitenant-architecture
- PostgreSQL: https://www.postgresql.org/docs/current/transaction-iso.html,
  https://www.postgresql.org/docs/current/explicit-locking.html y https://www.postgresql.org/docs/current/ddl-partitioning.html
- Flujos y agenda:
  - Elsa: https://github.com/elsa-workflows/elsa-core/discussions/6583
  - Temporal: https://github.com/temporalio/sdk-dotnet
  - MassTransit: https://masstransit.io/introduction/v9-announcement
  - Camunda: https://camunda.com/blog/2024/04/licensing-update-camunda-8-self-managed/
  - Quartz.NET: https://github.com/quartznet/quartznet/releases
  - Hangfire: https://www.hangfire.io/licenses.html
  - TickerQ: https://github.com/Arcenox-co/TickerQ
  - Cronos: https://github.com/HangfireIO/Cronos
- IA:
  - OpenAI: https://developers.openai.com/api/docs/guides/function-calling,
    https://developers.openai.com/api/docs/guides/structured-outputs y https://developers.openai.com/api/docs/guides/prompt-caching
  - Anthropic: https://platform.claude.com/docs/en/agents-and-tools/tool-use/tool-search-tool
  - .NET: https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/extract-schema,
    https://docs.json-everything.net/schema/basics/ y https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai
  - OWASP: https://genai.owasp.org/llm-top-10/
