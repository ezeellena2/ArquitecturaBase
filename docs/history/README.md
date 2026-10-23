# Procedencia de la documentación consolidada

El 2026-09-30 se retiraron los planes cumplidos y los inventarios previos a MVC después de
consolidar sus decisiones como [premisas](../architecture/premisas-de-la-plantilla.md) y rescatar
[verificaciones y preparación pendientes](../plans/verificacion-y-produccion.md).
Los ADR conservan decisiones, razones, alternativas y enmiendas. Los diseños conservan su
valor de referencia con la precedencia de las fuentes vigentes; no se ejecutan como planes.

## Dónde quedó cada grupo

| Planes retirados | Fuente vigente |
| --- | --- |
| Fases 1 y 2; migración MVC; etapas 1, 2, 3 y 7; contratos previos | [Backend](../architecture/backend.md), [identidad](../features/identidad.md) y [ADR](../decisions/README.md). El inventario vivo está en `ExplicitRouteInventoryTests`. |
| Fase 4 y etapa 4 | [Administración](../features/administracion.md) y [agregar un área](../guides/agregar-un-area.md). |
| Fases 3 y 5; rol en pantalla propia; renovación estricta de sesión | [Arquitectura del front](../../../ArquitecturaBaseFront/docs/architecture.md), [premisas](../../../ArquitecturaBaseFront/docs/guides/premisas-de-desarrollo.md) y [fundamento visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md). |
| Ingreso por WhatsApp y etapa 6 | [WhatsApp](../features/whatsapp.md), [ADR 0007](../decisions/0007-whatsapp-como-modulo-opcional.md), [prueba local](../guides/whatsapp-en-local.md) y [quitar el módulo](../guides/quitar-whatsapp.md). |
| Forwarded headers | [Proxy y encabezados](../guides/despliegue.md#proxy-y-encabezados-reenviados). |
| Plan maestro | [Premisas](../architecture/premisas-de-la-plantilla.md), reglas canónicas y ADR 0001 a 0008. Sus ocho etapas estaban cerradas. |
| Redis | [Caché](../architecture/backend.md#caché-compartido-en-redis), [ADR 0009](../decisions/0009-redis-como-unico-cache-de-datos.md) y [receta](../guides/agregar-cache.md). |
| Configuración general y su archivo de redirección | [Contrato funcional](../features/administracion.md#configuración-general-2026-09-30), [precedencia](../architecture/backend.md#presentación-general-y-precedencia-2026-09-30) y [evidencia visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#configuración-general-2026-09-30). |

## Recuperar un documento original

Commit del backend anterior a esta limpieza: `6ed49ba3ea9a947d931a66a023d55a23cd61d05a`.
Commit correspondiente del front: `976a97321bf406529bc554a9bd6d2a7ea89b0e21`.
Desde el backend, se puede inspeccionar un plan sin restaurarlo en el árbol de trabajo:

```powershell
git ls-tree -r --name-only 6ed49ba3ea9a947d931a66a023d55a23cd61d05a docs/history/plans docs/plans
git show 6ed49ba3ea9a947d931a66a023d55a23cd61d05a:docs/plans/2026-09-26-plantilla-estandar-por-etapas.md
```

Para el resto, usar su ruta de aquel commit con `git show`. Una auditoría posterior conserva
la misma trazabilidad sin volver a copiar aquí los planes completos.

## Diseños redundantes retirados el 2026-10-02

Después de la alineación documental y por pedido del usuario, se retiraron estos dos diseños
implementados. Sus reglas ya estaban consolidadas; describían rutas, handlers y guardados
anteriores que el producto reemplazó. El commit anterior al retiro sigue siendo
`6ed49ba3ea9a947d931a66a023d55a23cd61d05a` en el backend.

| Documento retirado (ruta original) | Fuente vigente y pendientes conservados |
| --- | --- |
| `docs/specs/2026-09-20-fase-4-administracion-design.md` | [Administración](../features/administracion.md), [identidad](../features/identidad.md), [ADR 0004](../decisions/0004-roles-como-area-de-referencia.md) y arquitectura del front. La propuesta heredada de administrar dispositivos/sesiones y el paso faltante de hosting se conservan en [verificación y producción](../plans/verificacion-y-produccion.md). Forwarded headers ya tiene su [guía vigente](../guides/despliegue.md#proxy-y-encabezados-reenviados). |
| `docs/specs/2026-09-30-configuracion-general-design.md` | [Configuración general](../features/administracion.md#configuración-general-2026-09-30), [precedencia técnica](../architecture/backend.md#presentación-general-y-precedencia-2026-09-30) y [elección y evidencia visual](../../../ArquitecturaBaseFront/docs/design/visual-baseline.md#configuración-general-2026-09-30). La adopción transversal de mensajes conserva su plan propio. |

Para consultar los originales, usar `git show <commit>:<ruta original>` con el commit y
las rutas de esta sección. Las decisiones posteriores prevalecen en las fuentes vigentes;
el original recuperado documenta su momento histórico.

La separación de acceso/atención del 2026-10-02 está consolidada en ADR 0011 y features/whatsapp.md.
Su plan se retiró el 2026-10-12 (ver abajo); la prueba de teléfono real está en verificacion-y-produccion.md.

## Notas de formularios retiradas el 2026-10-11

Las notas por corte del 9–11 de octubre y los fixtures históricos de docs/reviews se retiraron por pedido humano y ADR 0018. Reglas en [formularios](../features/formularios.md) y [WhatsApp](../features/whatsapp.md); resultados y pendientes en el plan principal (retirado después; ver abajo) y su cierre breve. El incremento 0 versionado se recupera desde bb8d01e; su estado previo no representa la implementación actual. Las demás notas nunca se commitearon: no se inventa una recuperación desde Git. Se conservó copia local ZIP con manifiesto SHA256 en `.test-artifacts/forms-step5/provenance/`, fuera de Git, antes de borrarlas.

## Planes de formularios retirados el 2026-10-11 (depuración)

Por pedido humano, para que los agentes no lean más de 300 KB antes de cada tarea. Se recuperan desde el commit `f4d2a9f`.

| Documento retirado | Dónde quedó |
| --- | --- |
| Plan principal de formularios y Flows (`docs/plans/2026-10-07-formularios-dinamicos-y-whatsapp-flows.md`) | Un resumen por etapa en [`plans/formularios`](../plans/formularios/README.md); reglas implementadas en [formularios](../features/formularios.md) y [WhatsApp](../features/whatsapp.md); pendientes en [planes](../plans/README.md). |
| Acuerdos del 2026-10-08 | Las decisiones, en el resumen de cada etapa. La demo de datos sigue en `plans/formularios-modelo-demo`. |
| Contrato de la Etapa 1, contrato del incremento 2 y coordinación de la Etapa 1 | [Formularios](../features/formularios.md), con la máquina de estados de publicación, la numeración y los tombstones rescatados; el contrato vigente son los DTO, el OpenAPI y los ejemplos exportados. |
| Revisiones del 7 y 8 de octubre sobre la maqueta, el plan y los acuerdos | Sin información vigente: revisaban documentos retirados. |
| Preparación de la Etapa 1 del front | `AGENTS.md` de las features `business-forms` y `whatsapp` del front y el [diseño de formularios](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). |

## Planes retirados el 2026-10-12 (segunda depuración)

Se recuperan desde el commit `d687e34c`. Todos estaban versionados desde `ceaed208`.

| Plan retirado o condensado | Dónde quedó |
| --- | --- |
| Inicio con assistant-ui (retirado) | [IA](../features/inteligencia-artificial.md), [ADR 0017](../decisions/0017-inicio-asistente-y-operaciones-durables.md), [configurar IA](../guides/configurar-ia.md) y [verificación y producción](../plans/verificacion-y-produccion.md#inteligencia-artificial). |
| Configuración general de LLM (retirado) | [IA](../features/inteligencia-artificial.md), ADR 0013 y 0014, [configurar IA](../guides/configurar-ia.md), [quitar IA](../guides/quitar-ia.md) y verificación y producción. |
| Separar acceso y chat de WhatsApp (retirado) | [ADR 0011](../decisions/0011-separacion-acceso-y-atencion-whatsapp.md), [WhatsApp](../features/whatsapp.md), administración e identidad. |
| Calidad del arnés del front (retirado) | Fila «Deuda menor del front y del arnés» en [planes](../plans/README.md). |
| Panel de conversaciones, bandeja de WhatsApp, sugerencias de IA, archivos en Azure y solicitudes de ingreso (condensados) | El mismo archivo, reducido a alcance, decisiones vigentes y pendientes; el diseño extenso se recupera desde `d687e34c`. |

## Formularios: Etapa 2B — 2026-10-15

El resumen de fotos/documentos se consolida tras el piloto real en [formularios](../features/formularios.md#cierre-de-la-etapa-2b--2026-10-15), [WhatsApp](../features/whatsapp.md#fotos-y-documentos-en-flows--2b-2026-10-15) y la [guía Azure](../guides/azure-blob-en-local.md). El comportamiento reusable de almacenamiento vive en [arquitectura](../architecture/backend.md#archivos-privados); antivirus y los otros consumidores conservan su [plan](../plans/2026-10-02-almacenamiento-de-archivos.md). El resumen con el resultado del piloto se recupera con `git show ed53fb8:docs/plans/formularios/etapa-2b-fotos-y-documentos.md`. No se retiran propuestas de Etapas 3–5 ni los pendientes reservados del review.

## Formularios: Etapa 3 — 2026-10-16

El resumen de planilla/detalle/Excel se consolida después de la prueba real en [formularios](../features/formularios.md#planilla-detalle-y-excel--etapa-3), los AGENTS de BusinessForms, los puertos neutrales de origen y el [diseño elegido del front](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#implementación-de-t1t5--2026-10-16). Se recupera con `git show 577d1c3:docs/plans/formularios/etapa-3-planilla.md`. Al retirar ese resumen, Etapas 4–5, carga web/importación y correcciones se conservaban como pendientes; su estado vigente está en [planes](../plans/README.md) y en el [índice de etapas](../plans/formularios/README.md).

## Formularios: Etapa 4 y review de la Etapa 3 — 2026-10-16

El plan de reglas, aprobación, PDF y avisos y el review de la planilla se retiran tras su implementación y [verificación local definitiva](../features/formularios.md#verificación-de-la-etapa-4--2026-10-16). La procedencia inmediatamente anterior al retiro es backend `98fa775ca51985cfcb3a6fa07481d49be912fa85` y front `a7ab04d97251af01454363473fc7e7fb51d46c5a`. Se recuperan con `git show 98fa775ca51985cfcb3a6fa07481d49be912fa85:docs/plans/formularios/etapa-4-reglas-aprobacion-pdf.md` y `git show 98fa775ca51985cfcb3a6fa07481d49be912fa85:docs/reviews/2026-10-16-code-review-etapa-3.md`.

Las reglas funcionales y los resultados quedan en [formularios](../features/formularios.md#reglas-con-revisiones-propias--etapa-4-g1) y [WhatsApp](../features/whatsapp.md#avisos-de-reglas-de-formularios--etapa-4-g6); almacenamiento y retención, en [arquitectura](../architecture/backend.md#archivos-privados); responsabilidades, en los AGENTS y sus mapas; los ajustes G2/G5 de la versión 75 y la experiencia, en el [diseño del front](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#implementación-de-g1g6--2026-10-16). Los 14 puntos del review están corregidos; los hallazgos opcionales ya resueltos o con consumidores reales no se convierten en deuda nueva. El historial del chat en URL conserva su pendiente existente en [planes](../plans/README.md).

El retiro inicial dejó pendiente el piloto externo. Después se acreditó el [recorrido real en español con foto](../features/formularios.md#piloto-de-aprobación-y-pdf--2026-10-17); los casos restantes están en [verificación y producción](../plans/verificacion-y-produccion.md#formularios-piloto-de-aprobación-y-pdf). El [índice de etapas](../plans/formularios/README.md) conserva la flexibilización de idiomas posterior, sin declararla implementada.

## Formularios: Etapa 5 y review de la Etapa 4 — 2026-10-18

El resumen `docs/plans/formularios/etapa-5-asistente.md` y el review `docs/reviews/2026-10-17-code-review-etapa-4.md` se recuperan desde el commit anterior a esta limpieza, `184fd222b82fc540afd40e19d2de2aebd2f52430`. Se retiran tras implementar los 18 arreglos, D2 y H1–H7/s1–s4, cerrar la [verificación local](../features/formularios.md#cierre-local-de-la-etapa-5-y-del-review-de-la-etapa-4) y registrar la [calificación OpenAI](../guides/configurar-ia.md#evidencia-para-habilitar-perfiles).

Las reglas quedan en [formularios](../features/formularios.md), [WhatsApp](../features/whatsapp.md), [IA](../features/inteligencia-artificial.md) y sus premisas; los modelos/perfiles y recuperación, en [configurar IA](../guides/configurar-ia.md). El [piloto físico con Meta e IA](../plans/verificacion-y-produccion.md#formularios-piloto-del-asistente-y-audios), los idiomas pendientes de la Etapa 4 y la flexibilización posterior conservan su alcance abierto. No se atribuye a la calificación ficticia una prueba física con Meta.

## Formularios: idioma original y traducción opcional — 2026-10-19

El resumen `docs/plans/formularios/idioma-de-los-formularios.md` conserva la decisión del 2026-10-16 y los tableros I1–I3 elegidos el 2026-10-18. Se retira tras implementar y verificar localmente publicación por culturas completas, editor, envío y catálogo individual. Se recupera con `git show db4f5fbfeb2babbcf1c2c59e56c05acbb7f63eb4:docs/plans/formularios/idioma-de-los-formularios.md`; ese commit es la procedencia inmediatamente anterior a la limpieza.

Las reglas quedan en [formularios](../features/formularios.md#idioma-original-y-traducción-opcional--2026-10-19), [WhatsApp](../features/whatsapp.md#publicación-durable-de-flows--2b-2026-10-10), las [premisas](../architecture/premisas-de-la-plantilla.md), los ámbitos existentes y el [diseño I1–I3](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md#los-tableros). Los resultados y límites se conservan en la [verificación local](../features/formularios.md#verificación-local-de-idioma-original-y-traducción-opcional--2026-10-19). El [piloto físico agrupado](../plans/verificacion-y-produccion.md#formularios-idioma-original-y-traducción-opcional), incluido el faltante exclusivo de aviso/PDF del punto 24, sigue pendiente; los resultados históricos de etapas y pilotos anteriores permanecen fechados.

## Formularios: baja, reactivación y eliminación — 2026-10-21

El plan `docs/plans/formularios/dar-de-baja-formularios.md` se recupera desde el commit `8abea365`, anterior a su retiro. Sus decisiones de ciclo de vida, permisos, disponibilidad, prefijo reservado y conservación de respuestas están en [formularios](../features/formularios.md#dar-de-baja-reactivar-y-eliminar--2026-10-21); la operación durable por Flow y su evidencia local, en [WhatsApp](../features/whatsapp.md#deprecación-durable-de-flows--2026-10-21). El [índice de premisas](../architecture/premisas-de-la-plantilla.md) enlaza ambas fuentes.

MF1/DC0/DC1 se implementaron después, según las [reglas de menú y detalle](../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21); no formaban parte de este cierre de ciclo de vida. El code review y el [piloto real con Meta](../plans/verificacion-y-produccion.md#formularios-baja-reactivación-y-eliminación) quedan separados del cierre local; no se inició el piloto.

## Formularios: menú y detalle completo — 2026-10-21

El plan se recupera con `git show 8ecaf23b:docs/plans/formularios/menu-y-detalle.md`, procedencia anterior al retiro. Sus decisiones MF1, D2, DC0/DC1 y versión 96 se consolidan en [formularios](../features/formularios.md#gestión-de-formularios-y-detalle-completo--2026-10-21), sus guías por carpeta y el [diseño del front](../../../ArquitecturaBaseFront/docs/design/formularios-whatsapp.md). El aislamiento previo de turnos queda en [IA](../features/inteligencia-artificial.md#motor-único-y-propuestas-por-canal--etapa-5) y [WhatsApp](../features/whatsapp.md#aislamiento-entre-conversaciones--2026-10-21).

Copiar formularios y la retención permanecen en [planes](../plans/README.md); la limpieza de datos de prueba sigue fuera del producto. El [code review y el piloto con Meta](../plans/verificacion-y-produccion.md#formularios-menú-y-detalle-completo) tienen su comprobación separada; esta implementación no acredita un piloto nuevo.

## Catálogos: E1 — 2026-10-22

El plan de carga de una vez, tipo de valor y elegir varias se recupera con `git show 114f50c3:docs/plans/formularios/catalogos-carga-tipo-y-varias.md`; ese commit es la procedencia inmediatamente anterior al retiro. Sus decisiones se consolidan en [formularios: base de E1](../features/formularios.md#catálogos-base-de-la-etapa-e1--2026-10-21), [valor tipado](../features/formularios.md#catálogos-con-tipo-de-valor--e1-b-2026-10-22), [carga de planillas](../features/formularios.md#carga-de-catálogos-desde-una-planilla--e1-a-2026-10-22), [elegir varias](../features/formularios.md#elegir-varias-base-y-captura--e1-c1-2026-10-22), las guías locales y el [diseño del front](../../../ArquitecturaBaseFront/docs/design/catalogos-mejoras.md#recorrido-local-de-e1--2026-10-22).

La [verificación local](../features/formularios.md#verificación-local-de-catálogos-e1--2026-10-22) distingue backend con PostgreSQL/Redis, contratos HTTP exportados y recorrido del navegador con escenas independientes. El review de E1 se cerró después; el [piloto único con Meta](../plans/verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2) se conserva para después del review de E2. Las [reglas de E2](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23) y su procedencia se registran abajo; las propuestas posteriores permanecen en la [hoja de ruta](../plans/2026-10-21-catalogos-con-origenes-externos.md).

## Catálogos: campos extra y filtros por persona — E2, 2026-10-23

El brief `docs/plans/formularios/catalogos-campos-extra.md` se recupera desde `2718ed102f51941e5cffae4366b11d28c1995ed5`, el commit inmediatamente anterior a su retiro. Git conserva el orden del desarrollo y sus decisiones. La [spec conjunta de E1/E2](../specs/2026-10-21-catalogos-design.md) permanece completa como diseño histórico, con las referencias corregidas; sus supuestos no sustituyen el canon vigente. Las reglas implementadas, el canon de importación, la captura histórica, los permisos y el alcance de los checks se consolidan en [formularios](../features/formularios.md#catálogos-con-campos-extra-y-filtros-por-persona--e2-2026-10-23), las [premisas](../architecture/premisas-de-la-plantilla.md) y las guías de cada capa. Los tableros y sus referencias elegidas quedan en [el diseño del front](../../../ArquitecturaBaseFront/docs/design/catalogos-campos-extra.md); no se retiran sus imágenes.

El review de E2 y el [piloto único con Meta](../plans/verificacion-y-produccion.md#formularios-piloto-único-con-meta-después-de-e2) son posteriores. Búsqueda en vivo, autocompletar otras preguntas, IA que crea catálogos, orígenes externos y la guía para quitar formularios mantienen su alcance pendiente. El retiro documental no acredita una ejecución con proveedores reales.
