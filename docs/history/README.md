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
Su plan original sigue en plans porque todavía estaba sin versionar al desarrollar, sin un commit
recuperable, y conserva la trazabilidad del alcance. No se eliminó ni se inventó una referencia de Git;
la prueba de teléfono real está trasladada a verificacion-y-produccion.md.
