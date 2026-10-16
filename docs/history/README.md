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
