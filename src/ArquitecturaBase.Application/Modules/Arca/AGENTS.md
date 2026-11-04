# ARCA: módulo opcional

Los permisos del módulo se aportan desde `Channels`, con sus textos en `Resources` en español y en inglés. Cada familia de servicios tiene su carpeta dentro de `Interfaces/{Services,Persistence,Integrations}`, `Models` y `Services`, nunca arriba de esas carpetas: las reglas de ubicación comparan el namespace sin `Modules.Arca`. Una familia usa sólo `Connection` y la raíz del módulo; `Connection` conoce a las demás sólo por su interfaz de familias. El registro tiene una línea por familia.

Hereda las instrucciones del proyecto. Sus decisiones son el [ADR 0023](../../../../docs/decisions/0023-arca-como-modulo-opcional.md) y el [ADR 0025](../../../../docs/decisions/0025-familias-de-servicios-arca.md); el diseño vigente, [ARCA · Etapa 1](../../../../docs/plans/2026-11-03-arca-etapa-1-conexion.md). No importar otros módulos. Actualizar mapa y fuentes con un cambio de responsabilidad.
