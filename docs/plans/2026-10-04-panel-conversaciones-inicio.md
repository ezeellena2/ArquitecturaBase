# Inicio: panel interno de conversaciones y validación de capacidad

Fecha: 2026-10-04. Actualización: 2026-10-05. Estado: **panel elegido explícitamente, implementado y validado**. Capacidad y retención de metadatos siguen pendientes como etapa independiente.

El usuario pidió volver al panel interno plegable, abrir Inicio con una conversación nueva y
conservar conversaciones anteriores para elegirlas. Confirmó **una instalación independiente
por empresa**. Esta propuesta complementa Inicio implementado y ADR 0017; no sustituye su
ejecución durable ni aprueba límites operativos por inferencia.

## 1. Decisión de alcance

Se mantiene `@assistant-ui/react` y su `ExternalStoreRuntime`, PostgreSQL como autoridad,
MVC → servicios Application → puertos → Infrastructure, el módulo opcional IA y la cadena
única de migraciones. No se introduce un segundo store de conversaciones en la biblioteca,
un controller alternativo, repositorio genérico, proveedor HTTP en el navegador ni caché local
compartido. La primera fase sigue siendo lectura de negocio con una respuesta terminal.

La fase del panel **no necesita tablas nuevas ni migración**. Las cuatro existentes ya separan
conversación, turno, herramientas y comprobante. Pregunta y respuesta siguen en el turno.
Son `AiConversations`, `AiAssistantTurns`, `AiAssistantToolExecutions` y `AiAssistantRequestReceipts`,
según sus configuraciones EF actuales; se conservan sus mapeos.
Una normalización futura a mensajes separados se evalúa por capacidades concretas —adjuntos,
edición o branching—; el número de filas por sí solo no la justifica.

Se implementa un título corto derivado de la primera pregunta vigente: amplía la proyección de
lectura dentro del módulo sin duplicar texto ni conservarlo más que el contenido original.
No hay llamada de título al LLM, columna de texto plano ni título derivado por el navegador
descargando todo el historial. Ante pérdida de acceso o vencimiento se usa el título genérico localizado.

## 2. Contrato visual elegido e implementado

Elección explícita del usuario del 2026-10-05: «dejalo claro en el plan e implementalo en el codigo».
Se promueve el panel refinado con primitivas nativas de assistant-ui; no el primer panel con fechas
y papelera por fila. La pantalla productiva usa AppLayout/Page, AssistantThread/Composer/Message/Suggestions
y UI compartida. La procedencia única del tablero se conserva inactiva en
`../ArquitecturaBaseFront/design-lab/history/home-assistant-conversations-2026-10-05`.

- Campo y Public Sans; menú general de 220 px conservado. Panel interno claro de 256 px,
  dentro del mismo marco del chat, sin competir con el menú general verde.
- Nueva conversación arriba del panel como acción compacta; agrupación Hoy/Ayer/Anteriores y
  filas de una línea con título y selección activa. Fechas representadas por los grupos, sin
  repetición por fila. Menú «…» visible en hover/foco/selección; siempre en táctil, con blanco de
  44 px. Eliminar dentro del menú pide ConfirmDialog. Sin búsqueda o renombrado sin contrato.
- El panel se pliega con su control interno. Se abre desde el borde superior izquierdo del
  chat. Historial/Nueva conversación dejan de estar en las acciones globales de Page.
- Desktop abierto inicialmente desde 1024 px; debajo, panel superpuesto cerrado inicialmente
  con Dialog compartido, foco contenido, Escape/cierre y devolución del foco al disparador.
- Hilo de 704 px con padding de 16 px; bienvenida/editor centrados en vacío y editor al pie
  al conversar. Se conserva la forma del chat elegido, incluida la flecha y las burbujas.
- El estado abierto/plegado es presentación; no activa conversaciones ni crea filas. En esta
  fase vive en memoria, sin almacenar contenido o IDs en localStorage.
- Revisar es/en a 1440, 390 y 320 px, abierto/plegado, lista larga, títulos extensos y teclado.

Estados del tablero: nueva, guardada, historial cargando, carga fallida con recuperación,
sin conversaciones, contenido bloqueado, contenido vencido, operación activa, aceptación
incierta y falta de permiso. Se puede probar selección, descarte, borrado y envío simulado.

### Revisión del panel: base existente de la biblioteca

El usuario pidió una base similar a Codex/Claude para mejorar el menú de la primera maqueta.
assistant-ui publica [ThreadList](https://www.assistant-ui.com/elements/thread-list) y
[ThreadListSidebar](https://www.assistant-ui.com/elements/thread-list-sidebar), este último
compuesto con un shell de Sidebar. No se afirma que sean componentes internos de esos productos.
El producto usa las primitivas instaladas de ThreadList/Item/More, New/Trigger/Title y foco de
menú sobre ExternalStoreRuntime; conserva el shell y el drawer del proyecto. No ejecuta init,
instala un runtime de generación alternativo ni incluye branding/acciones del template sin contrato.

En 0.15.23, `ExternalStoreAdapter.adapters.threadList` y sus callbacks de cambio están marcados
deprecated/experimentales por la biblioteca. La frontera se mantiene detrás del adaptador de IA,
con versión fijada y regresiones de aceptación/navegación/paginado/foco antes de promover. No
utilizar sus tipos en contratos HTTP o tablas. No ignorar ese estado al actualizar el paquete.
La biblioteca representa selección/títulos; el backend conserva la autoridad de las conversaciones.

El New nativo sólo pide el cambio autorizado al chat vacío. Cambiar el ID del runtime ocurre
después de resolver borrador/operación/incertidumbre, y crear una conversación real sigue esperando
aceptación del servidor. El menú pide la confirmación de la feature, sin Delete/Archive/Rename
del runtime que cambien el estado por su cuenta. La lista paginada deriva índices desde el snapshot
del runtime, para que una aceptación nueva no se renderice con un índice que todavía no existe.

El Artifact privado de procedencia no se declara sincronizado. El código y las capturas de
regresión del producto conservan el contrato revisable. La elección explícita satisface el
requisito del AGENTS del front; la evidencia del tablero se conserva como procedencia histórica.

## 3. Navegación, creación e historial: comportamiento completo

| Disparador | Comportamiento |
| --- | --- |
| Entrar mediante Inicio `/` | Chat vacío; nunca abrir automáticamente el último historial. |
| Recargar `/?conversacion=<id>` | Recuperar esa conversación autorizada; sólo GET. |
| Primera pregunta aceptada | Crear conversación/turno/comprobante dentro de la transacción existente; confirmar ID en URL. |
| Seleccionar historial | Actualizar `conversacion` mediante useQueryUpdate y leer revisión/turnos. |
| Nueva conversación | Quitar `conversacion`; no POST ni fila hasta enviar. |
| Borrador sin enviar | Confirmar descarte antes de cambiar; cancelar conserva texto, selección y foco. |
| Operación activa | Ofrecer seguir en segundo plano o pedir cancelación y continuar; nunca reenviar al navegar. |
| Aceptación incierta | Conservar UUID/body congelado; consultar estado antes de abandonar/repetir. |
| Eliminar | ConfirmDialog; DELETE existente, autorización propia, retirada inmediata de acceso y cancelación durable. |
| Eliminar la seleccionada | Tras confirmación del servidor, volver a `/` y actualizar listado; un fallo conserva la selección. |
| Cambiar idioma/zona | Textos/agrupación/formato se actualizan; no genera ni altera la pregunta enviada. |
| Logout/cambio de cuenta/permisos | Cancelar lecturas, eliminar caches privados, borrar borradores/títulos y rechazar respuestas tardías. |

El menú actual ya enlaza `/`, la página lee `conversacion` y la creación ya es diferida.
La mayoría de estos comportamientos se conserva y se prueba en la nueva composición; no se
reemplazan guards ni operaciones durables por estado optimista del panel.

La URL contiene ID y paginado (`page`, `pageSize`, `sort`) del historial; nunca pregunta,
respuesta, título ni request body. Nuevas escrituras relacionadas van juntas en una llamada
useQueryUpdate para evitar la pisada entre actualizaciones del mismo tick. La aceptación usa
replace como hoy; abrir historial es navegación, conserva Atrás/Adelante. Nueva conversación
limpia de manera explícita ID y página del historial cuando corresponda.

## 4. Listado, títulos y lecturas del backend

Rutas existentes bajo `/api/ai/assistant`: bootstrap, conversaciones, detalle, turnos,
aceptación, recuperación por requestId, cancelar y eliminar. Ninguna ruta adicional para el panel.

### 4.1 Listado acotado

Reutilizar `GET /conversations?page=…&pageSize=20&sort=-lastActivityAtUtc` y PagedRequest.
El panel muestra una página y carga la siguiente por una acción explícita. No descargar toda
la cuenta, todos los cuerpos o toda la base al entrar. La página queda en URL como el contrato
de listados común. No polling del listado entero: invalidar tras aceptación confirmada, cierre
de operación y borrado, además de recuperación explícita. El polling existente queda para la
operación elegida, se detiene en terminal/error y no corre con la pestaña oculta.

Offset y TotalCount existentes son apropiados para esta fase acotada. Si la medición demuestra
un costo relevante en páginas profundas, abrir una evolución a cursor con orden estable
`LastActivityAtUtc + Id` y contrato nuevo revisado, no fingir que «Ver más» ya es keyset.

### 4.2 Título autorizado sin migración

1. Infrastructure filtra propietario y estado como hoy y proyecta sólo una primera pregunta
   por conversación de la página, usando el índice único `(ConversationId, Ordinal)`.
2. La proyección mínima contiene TurnId, PayloadRevision, ProtectedQuestion y ContentExpiresAtUtc.
   No trae respuestas, evidencias ni herramientas para dibujar una fila.
3. Application comprueba sesión/cuenta y permisos actuales de la conversación antes de descifrar.
   Puede recuperar una instantánea de permisos actual una vez por request para evaluar la página;
   no conserva esa decisión entre requests ni evita las comprobaciones de ejecución/publicación.
4. Sólo si el primer contenido sigue vigente y tiene acceso, descifra con IAiConversationProtector
   y el propósito existente `question`, dueño/conversación/turno/revisión originales.
5. Un helper puro normaliza espacios/saltos y limita a 80 elementos de texto Unicode con elipsis;
   no corta surrogate pairs o grafemas. Devuelve texto seguro, sin HTML/Markdown ni heurística de roles.
6. Bloqueo, contenido purgado o vencido: título genérico localizado. El título tiene la misma
   vida y restricciones que la primera pregunta; no se conserva como copia después de los 30 días.
7. Una clave incorrecta/ciphertext inválido devuelve ContentUnavailable y el ProblemDetails
   existente; no ocultar corrupción como un título genérico ni volver a generar.
8. Listado y detalle emplean la misma política; no descifrar con una consulta separada por fila.
   Verificar SQL generado/consultas instrumentadas y ausencia de N+1 antes de cerrar.
9. Los modelos/contratos que pasan a transportar título sensible redactan ToString; logs y
   métricas no incorporan preguntas, títulos, ciphertexts ni identidades como etiquetas.

La primera pregunta puede mencionar información sensible: acortarla no la anonimiza.
El acceso del título se valida en el servidor y la UI limpia también su listado cacheado al
perder permisos. No mostrar la pregunta completa como tooltip oculto para una fila bloqueada.
Una conversación antigua aún activa puede tener título genérico porque venció su primera pregunta;
ese resultado es deliberado en esta fase. Un título editable o de conservación independiente
necesitaría un caso de uso y política de retención propios, fuera de esta propuesta.

## 5. Árbol real de cambios de la fase del panel

`M`: modificado; `A`: creado; `D`: retirado. Caminos relativos a cada repositorio.

```text
ArquitecturaBaseFront/
├─ src/features/artificial-intelligence/
│  ├─ pages/AiAssistantPage.tsx                           M composición y decisiones de navegación existentes
│  ├─ pages/AiAssistantPage.test.tsx                      M aceptación, borrador, selección, paginado y plegado
│  ├─ hooks/useAssistantConversationList.ts               A GET privado paginado; idioma y revisión de permisos
│  ├─ hooks/useAssistantConversation.ts                   M invalidación de lista tras estado terminal
│  ├─ hooks/useAssistantTurnActions.ts                    M retirada de caché tras borrado confirmado
│  ├─ assistant/conversationGroups.ts                    A agrupación pura por ahora UTC y zona del perfil
│  ├─ assistant/conversationGroups.test.ts               A medianoche, cambio de año y horario de verano
│  ├─ components/assistant/AssistantConversationPanel.tsx A datos, grupos, estados y paginado comunes
│  ├─ components/assistant/AssistantRuntimeProvider.tsx   M adaptador nativo de listado controlado
│  └─ components/assistant/AssistantHistoryDialog.tsx     D consumidores reemplazados por el panel interno
├─ src/shared/ui/assistant/
│  ├─ AssistantWorkspace.tsx                             A marco, plegado y drawer sin API ni sesión
│  ├─ AssistantConversationList.tsx                      A filas nativas, menú More y foco
│  └─ conversationActions.ts                            A contexto de callbacks de presentación
├─ src/index.css                                         M geometría elegida y tokens existentes
├─ src/locales/{es,en}/assistant.json                     M textos, grupos y nombres accesibles
├─ src/test/visual/
│  ├─ assistantFixtures.ts                              M escenarios ficticios e instrumentación de peticiones
│  ├─ assistant.spec.ts                                 M referencias del Inicio y decisiones de borrado
│  ├─ assistant-layout.spec.ts                          M geometría con panel interno
│  ├─ assistant-conversations.spec.ts                   A seis recorridos completos es/en y responsive
│  └─ screenshots/                                     M referencias revisadas del diseño aprobado
├─ docs/{architecture.md,mapa-de-instrucciones.md}         M fuentes canónicas y responsabilidades
├─ docs/design/visual-baseline.md                         M elección explícita y promoción
└─ design-lab/history/home-assistant-conversations-2026-10-05/ archivo de procedencia único, inactivo

ArquitecturaBase/
├─ src/ArquitecturaBase.Application/Modules/ArtificialIntelligence/
│  ├─ Models/AiAssistantRows.cs                          M proyección mínima cifrada; ToString redactado
│  ├─ Models/AiAssistantResponses.cs                     M ToString redactado para títulos sensibles
│  ├─ Services/AiAssistantReadPolicy.cs                   M autorización actual de títulos por página/detalle
│  ├─ Services/AiAssistantResponsePolicy.cs               M misma política de vigencia y descifrado para ambos GET
│  └─ Services/AiAssistantConversationTitlePolicy.cs      A normalización pura Unicode, sin EF ni DI adicional
├─ src/ArquitecturaBase.Infrastructure/Modules/ArtificialIntelligence/
│  └─ Persistence/Readers/AiAssistantReader.cs            M LEFT JOIN del primer turno; índice conversación/ordinal
├─ tests/ArquitecturaBase.Application.UnitTests/Modules/ArtificialIntelligence/
│  └─ AiAssistantConversationTitleTests.cs                A permisos, expiración, cifrado y Unicode
├─ tests/ArquitecturaBase.Api.IntegrationTests/
│  ├─ Modules/ArtificialIntelligence/AiAssistantConversationHistoryTests.cs A HTTP, propiedad, borrado y SQL
│  └─ TestFeatures/Modules/ArtificialIntelligence/AssistantReadCommandRecorder.cs A interceptor de pruebas
└─ docs/{features,architecture,plans,reviews}/             M decisiones implementadas y comprobaciones
```

Los AGENTS locales de feature, UI compartida, pruebas y módulo, y ambos mapas se actualizan con estas responsabilidades.
No se crean los hooks de navegación ni tests por componente sugeridos en el árbol inicial:
las decisiones ya existentes permanecen en AiAssistantPage y se verifican en su recorrido real.
Controller, contratos HTTP, entidades, escrituras, configuraciones EF, registro DI y versiones de paquetes se conservan.
El panel no cambia WhatsApp, el router, el menú general ni introduce una segunda fuente de conversaciones.

## 6. Idioma, fechas, timer, errores y privacidad transversales

- i18next assistant es/en, paridad, voseo; permisos/configuración del servidor conservados.
- Título derivado conserva el texto original del usuario; no se traduce por IA al cambiar idioma.
  El genérico y grupos sí se localizan; sin claves de traducción concatenadas con datos del usuario.
- Servidor TimeProvider → UTC/Z. UI usa zona del perfil y shared/lib/dateTime para fechas/grupos.
  `conversationGroups` recibe ahora UTC; pruebas con reloj controlado y cruces de medianoche.
- Cooldowns con useCountdown existente; no setInterval propio para reenvíos. Idioma/refetch no
  reinician la cuota ni generan. Etiquetas de día se recalculan al abrir/reanudar la vista.
- Query keys incluyen usuario, idioma y revisión de permisos. Panel cerrado/móvil oculta la UI
  sin cancelar el trabajo del servidor; cancelar lecturas no demuestra cancelación/rollback del POST.
- Operaciones por shared/api/httpClient y acciones actuales, sin MutationCache para body/UUID.
  El texto sólo queda en memoria de editor/caché privada confirmada, nunca URL/storage/logs.
- QueryCache o manejador local tienen un único dueño del aviso; no doble Toaster/Banner.
  Campo inválido junto al editor; estado duradero junto al contenido; ConfirmDialog para pérdida.
- Dialog compartido para drawer y decisiones, ActionTooltip/IconButton comunes, foco y Escape.
  Sin botones invisibles al teclado, sin tooltip nativo, sin cortar acciones a 320 px.

## 7. Capacidad por instalación: etapa independiente del panel

La empresa tiene su instalación, base/volumen, claves de cifrado e InstallationId propios.
La capacidad se mide **por instalación**; diez empresas no multiplican las filas de una sola
base. Compartir un servidor físico o servicio PostgreSQL aún comparte CPU/IO/conexiones y se
dimensiona como operación; no equivale a implementar multi-tenancy en estas tablas.

Como referencia aritmética, 10 personas con 5 consultas por día durante 365 días producen
18.250 turnos al año, no millones de turnos por mes. Conversaciones, herramientas y receipts
son filas adicionales: el tamaño depende de su distribución, contenido cifrado e índices,
no sólo del conteo de turnos. Ese ejemplo no fija la cuota ni acredita una prueba de carga.

### 7.1 Lo que existe y los límites de sus garantías

- Índices: propietario/actividad/ID para conversaciones, conversación/ordinal único para turnos,
  activos únicos por actor/conversación, estado/fecha y vencimientos, tools por turno/ordinal,
  receipts por dueño/request, día/dueño y vencimiento.
- HTTP del proveedor fuera de las transacciones. Worker con scopes nuevos, fencing, claims,
  cuotas/capacidad compartidas en PostgreSQL y pasos externos reservados antes de enviar.
- Los locks de presupuesto/conexión serializan ciertas escrituras breves: aportan consistencia,
  pero agregar réplicas no garantiza crecimiento lineal de throughput. Medir tiempo/contención.
- No SQL arbitrario del modelo ni lectura de toda la base. Contexto limitado: 12 turnos/24.000
  caracteres iniciales; no todas las conversaciones del usuario en cada pregunta nueva.
- Piloto propuesto: 10 consultas/actor/día, 100/instalación/día y 4 Running, pendientes 50.
  Una empresa que necesite mayor concurrencia requiere calibración, cuota del proveedor y
  carga controlada; estos números no son una certificación de capacidad de producción.

### 7.2 Ciclo de vida real y trabajo pendiente

IA purga contenido por turno a los 30 días iniciales, tools asociadas y receipts vencidos,
en lotes de 100 mediante mantenimiento cada hora. Conv/turnos no retirados conservan metadata
indefinidamente hoy. Conv retiradas se borran con turnos/tools después de vencer todos sin activos.
WhatsApp vacía texto a los 90 días/día y conserva filas; sus purgas no están acotadas por lote
y la del historial carece de índice específico por fecha con Body no nulo.

No existe archivado automático, tablas mensuales, particiones ni traslado a otra base.
Una migración EF cambia el esquema; no es un archivo periódico de conversaciones.

Antes de producción con crecimiento sostenido:

1. Acordar ventanas de contenido, metadata, comprobantes/deduplicación y backups por empresa.
   No asumir 12 meses como aprobado; operaciones activas/inciertas no se borran para «limpiar».
2. Diseñar una purga física de terminales/conv sin contenido y el registro mínimo necesario
   para no repetir UUID, eludir cuota o reprocesar webhooks/envíos. Conservar referencias de
   WhatsApp/read-state/contactos y validar seguridad de replay antes de borrar IDs de Meta.
3. Medir purga IA sobre muchas filas ya vaciadas: estudiar índice parcial de contenido presente
   y planes SQL; no declarar que el índice actual de ContentExpiresAtUtc basta para cada OR/join.
4. Acotar purga WhatsApp e índice parcial fecha/ID con Body presente; considerar también salida
   durable sin Pending/Sending. TDD/integración y migración específica según guía.
5. Autovacuum, estadísticas, tamaño de tablas/índices/WAL, backups/restore y atraso de purga.
   Borrar contenido permite reutilizar espacio; no garantiza encoger archivos en disco.
6. Particionado por fecha sólo si mediciones justifican su costo y patrón de retiro. Revisar
   claves únicas, FKs y filtros, no proponer «tabla por mes» en controllers ni una base por usuario.

Estos cambios de lifecycle afectan reglas de producto y pueden necesitar migración/ADR;
se planifican en un desarrollo propio antes de afirmar almacenamiento acotado. No se ejecutan
silenciosamente durante el reemplazo visual del historial.

### 7.3 Ensayo de carga reproducible, separado de las suites normales

Hitos de lectura: 10.000, 100.000 y 1.000.000 de turnos sintéticos de una instalación, con
distribución realista por cuentas/conversaciones, contenido vigente/vencido/purgado y tools.
No son volúmenes que deban sembrarse en CI en cada PR ni en la base real del usuario.

- Base de ensayo desechable, SQL instrumentado y EXPLAIN (ANALYZE, BUFFERS) de listado,
  apertura, últimas páginas, cuotas/claims y purga; registros sin datos reales.
- Ensayar 10, 50 y 100 sesiones con lecturas y escrituras concurrentes contra dos hosts y
  Redis/PostgreSQL reales. Coordinar misma aceptación/revisión, cancelación y borrado.
- Proveedor HTTP controlado para medir backend sin cobro; ensayo real pequeño separado para
  medir latencia/capacidad/costos de modelos calificados, nunca con reintentos implícitos.
- Medir p50/p95/p99, conexiones, CPU/IO, locks, SQL por request, memoria, payloads, pendientes,
  duración/pasos/tokens informados y atraso/fallas de purga; contenido fuera de etiquetas.
- Objetivo inicial de ensayo, a aprobar contra hardware: p95 <= 300 ms para listado/lectura
  de página y aceptación <= 500 ms, excluyendo proveedor; cero generación en GET y cero
  activos duplicados; backlog acotado bajo capacidad declarada. No es un resultado medido hoy.
- Si falla un objetivo, localizar SQL/locks/capacidad antes de cambiar contratos o índices.
  El reporte declara recursos, distribución, carga y límites; no promete «clientes ilimitados».

Fuentes operativas: PostgreSQL [particionado](https://www.postgresql.org/docs/current/ddl-partitioning.html)
y [vacuum](https://www.postgresql.org/docs/current/routine-vacuuming.html). Las mediciones
del proyecto todavía no acreditan particionado ni cifras de capacidad.

## 8. Orden de implementación y criterio de cierre

1. **Diseño (realizado):** plan, tablero local y revisión es/en/responsive/recorridos; elección
   explícita del usuario antes de modificar la portada real.
2. **Backend (implementado):** TDD de títulos/privacidad/paginado, proyección EF y política Application.
   Ejecutar build y unit/architecture/integration Docker; conservar ADR 0017.
3. **UI (implementada):** tests rojos de panel/navegación y adopción de la composición elegida, reaprovechando
   runtime/acciones. Revisión visual contra este tablero, no contra capturas del formulario rechazado.
4. **Integración:** lista y chat reales con casos de draft, aceptación perdida, activo en otra
   conversación, permisos revocados, expiración, cuenta distinta y respuestas tardías.
5. **Cierre:** fuentes funcionales/instrucciones/mapa, evidencia de checks, procedencia y retirada
   del tablero promovido según guía documental. El plan de capacidad sigue pendiente si no se ejecutó.

Pruebas obligatorias de producto: build backend sin advertencias + dotnet test con Docker;
npm run build/lint/test y visual con capturas revisadas. No actualizar snapshots indiscriminadamente.
Si se levanta Aspire, aspire stop al terminar; no interrumpir la instancia abierta por el usuario.

Casos que deben estar cubiertos antes de cerrar el panel:

- `/` vacío no hace POST; reload, idioma, plegar y GET no generan ni crean registros.
- Guardar primera pregunta añade una sola conversación y actualiza lista/URL; reintento mismo UUID no duplica.
- Historial sólo propio; otro usuario/conv ajena 404; sin sesión 401; permiso perdido 403/blocked.
- Títulos no salen del ciphertext sin autorización o tras vencimiento; clave ausente no regenera.
- Página acotada y orden estable; ninguna petición por fila ni carga de todas las respuestas.
- Borrador se conserva al cancelar el diálogo/error; incertidumbre conserva body/UUID.
- Activo en segundo plano recuperable al seleccionarlo; cambiar de hilo no lo cancela implícitamente.
- Borrado confirmado retira también título/caché; conflicto/fallo no finge éxito.
- es/en, zona del perfil, límites de día, keyboard/IME, drawer Escape/foco y 320 px sin overflow.
- Módulo IA retirado: Inicio base sigue funcionando, sin rutas/capacidades inventadas.

## 9. Estado de esta entrega

- [x] Modelo de instalación independiente por empresa confirmado por el usuario.
- [x] Recorrido existente y archivos relevados; panel sin tablas nuevas ni rutas adicionales.
- [x] Tablero local con componentes reales preparado.
- [x] Verificación y revisión final del tablero refinado: 36 recorridos es/en a 1440/390/320, sin
  overflow horizontal, errores de navegador/avisos de consola ni llamadas al asistente.
  [Evidencia histórica](../../../ArquitecturaBaseFront/design-lab/history/home-assistant-conversations-2026-10-05/evidence/verification.json).
  TypeScript del tablero, build/lint del front, sus 1.009 pruebas y 191 visuales en verde.
  El teardown de Vite en Windows requirió detener su PID propio para terminar; detalle en el
  [registro del laboratorio](../../../ArquitecturaBaseFront/design-lab/history/home-assistant-conversations-2026-10-05/README.md).
  Estos resultados pertenecen al tablero, no acreditan las suites posteriores de producto.
- [x] Elección explícita del panel refinado el 2026-10-05.
- [x] Cambios de producto y suites funcionales: backend build sin advertencias y 2.340 pruebas
  correctas con Docker; front build/lint, 1.014 pruebas y 197 recorridos visuales verificados.
  Los cuatro selectores móviles corregidos se repitieron con resultado correcto.
  [Evidencia de implementación](../reviews/2026-10-05-panel-conversaciones.md).
- [ ] Retención completa de metadatos y ensayo de carga por instalación.
- [ ] Sincronización del Artifact privado y piloto real del proveedor.
