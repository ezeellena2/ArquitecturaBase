# Asistente de Inicio: capacidad, ciclo de vida y calibración

Estado: **pendiente, sin empezar**. Inicio, historial privado, panel interno y título derivado ya
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
  purga cada hora un lote de hasta 100 elementos repartido entre contenido, conversaciones
  retiradas y comprobantes vencidos.
- **Las conversaciones activas y sus turnos sin contenido conservan metadata sin plazo.** Sólo una
  conversación retirada se borra físicamente, cuando vencieron todos sus turnos y no queda uno
  activo. Una cuenta borrada retira y cancela lo suyo; una desactivada queda bloqueada sin purga anticipada.
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

- Los valores 10 por actor y 100 por instalación por día UTC, 4 Running, 50 Pending y 30 días
  son propuestas del piloto, no premisas.
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
