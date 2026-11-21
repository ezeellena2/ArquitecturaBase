# Traspaso 16 · 06 · Cómo quedó todo al 21/11

Reemplaza el §1 del [02 del 15](../2026-11-18-traspaso-multitenant/02-estado-al-18-11.md) y cambia las filas de su §2 que se
nombran abajo. Lo demás de ese 02 sigue: lo hecho después del 14 (§3), lo de después de la prueba integral (§4) y lo
planeado sin programar (§5), con lo que suma el §5 de este.

## 1. Dónde está ArquitecturaBase

- **La prueba integral corre desde el 19/11,** con la app levantada. Su estado, sección por sección, está en el
  [05](05-prueba-integral.md). Lo que encuentra se arregla sólo si bloquea; los cambios de diseño que pidió el usuario
  entraron con su aprobación.
- **El código:** backend `a7cee7cf` y front `d0cb2c9c`, del 20/11. Desde el 15 entraron 28 commits del backend y 15 del front:
  el barrido documental del 18/11 (§4), roles (01), carpetas (02), las filas con íconos (03 §1), O25 (05) y los documentos
  de la pieza única (03 §2), de las acciones del asistente y de los procesos del campo (04).
- **La última corrida completa en verde** es la del 17/11 (backend 11.656 de 11.656; front, 6.022 tests). Después, con roles
  y la app levantada, el backend dio 11.672 y 11.674 de 11.678 (sólo fallas por tiempos, que pasan solas) y el front 6.030 de
  6.031, con 14 escenas visuales que ya fallaban antes del cambio. La corrida en verde con la app apagada va al cierre de la
  prueba.
- **En GitHub** están el backend hasta `67d5b944` y el front hasta `91b5556d`, con el barrido del 18/11. Lo posterior, con
  este paquete, está sólo en la PC del usuario hasta que pida subirlo.
- **Lo que sigue:** terminar la prueba (WhatsApp, lo manual y el cierre), lo que está en curso (§3), la auditoría documental,
  que lleva las decisiones a las premisas y retira los planes terminados, y el traspaso 17.

## 2. Lo que cambia en la tabla del 15

| Desarrollo | Al 21/11 | Dónde leer |
| --- | --- | --- |
| Reglas de diseño para toda la app y Roles (RO1-A) | Roles pasó a las áreas plegables (RO3-D), con los permisos que incluyen, en la app desde el 19/11. Las filas de una tabla van sólo con íconos desde el 20/11. | [01](01-roles-y-permisos.md) y [03](03-diseno-para-toda-la-app.md) §1 |
| El motor para documentos, la biblioteca, las carpetas, «sólo en la app», el Excel en una hoja y «Ordenar por…» | Las carpetas pasaron a ser menús del menú lateral (CM1 a CM6), en la app desde el 20/11. Faltan su corrida completa y el segundo despliegue de su migración. | [02](02-carpetas-como-menus.md) |
| La revisión de WhatsApp y sus 22 arreglos | Más O25, la pantalla «Asistente», arreglada el 20/11. O29 (la coma en el Flow) está por decidir. | [05](05-prueba-integral.md) |
| Una sola pieza para elegir (DS1-A y DS2-A), nueva | Programada en una copia del front, sin aplicar. | [03](03-diseno-para-toda-la-app.md) §2 |
| Las acciones del asistente (ADR 0034), nueva | Aceptado, sin programar. | [04](04-ia-y-procesos-del-campo.md) §1 |
| Los procesos del campo (ADR 0033), nueva | Propuesta, sin programar. | [04](04-ia-y-procesos-del-campo.md) §2 |

## 3. En curso, esperando el cierre de la prueba

El orden acordado: al terminar la prueba se apaga la app; la sesión de las carpetas hace su corrida completa; después se
aplica la pieza única al front y se corre la suite completa.

- **Las carpetas como menús:** la corrida completa, con la exportación de los ejemplos HTTP. Después, el segundo despliegue
  de su migración, cuando el primero esté en todos los ambientes.
- **La pieza única para elegir:** 57 commits en una copia del front, sobre una base igual al front de hoy. Se aplican con la
  app apagada y se corre la suite (oxlint, build, vitest y las escenas visuales).
- **Roles:** una corrida completa en verde con la app apagada. Las 14 escenas visuales que ya fallaban antes del cambio son de
  otras áreas y siguen anotadas como pendientes.

## 4. El barrido documental del 18/11

- **Qué fue.** Nueve agentes contrastaron con el código la documentación de los dos repos, y cada área se corrigió por
  separado (backend `0a139574` a `67d5b944`, front `4e090049` a `91b5556d`). Sólo cambiaron documentos y `AGENTS.md`, no el
  código.
- **La lista de lo que falta acá:** 97 pendientes en `docs/reviews/2026-11-18-barrido-documental.md` (26 a medias, 16 sin
  terminar, 8 diseñados sin programar y 47 propuestos sin decidir), cada uno con quién lo decide. El índice de planes la
  enlaza.
- **Se retiraron nueve planes terminados:** el diseño y la implementación de la etapa 1 de ARCA, la etapa 2 de ARCA (el motor
  para documentos), la etapa 1 de la IA que arma formularios, la etapa 2 de Formularios (catálogos), la demo de datos de
  formularios, las sugerencias de texto con revisión humana (las reemplazó el borrador de los casos del asistente) y los
  arreglos del núcleo y de Formularios. Sus decisiones están en `docs/features/`, los ADR y las premisas.
- **En los traspasos,** que son fotos de un momento, los enlaces a esos planes apuntan a la entrada «Barrido documental del
  2026-11-18» de `docs/history/README.md`. Cada plan se recupera desde el commit anterior a su retiro, por ejemplo con
  `git show 1cc6307c:docs/plans/2026-11-14-arreglos-del-nucleo.md`.
- **Cinco premisas nuevas del núcleo,** que juntan lo que ya contó el T14/02: nadie da lo que no tiene (N1 y N25), cuentas y
  roles con revisión y 409 (N2), la retención de los datos del ingreso (N16), los topes del correo y los dos medios
  verificados (N7 y N11), y la historia administrativa sin datos personales (N13).

## 5. Lo que se suma a lo planeado y sin programar

- Las acciones del asistente y los procesos del campo ([04](04-ia-y-procesos-del-campo.md)).
- MO1-B, los módulos que prende y apaga el administrador, sin documento (04 §3), y el CRM, sin nada decidido.
- Los recorridos de punta a punta con Playwright siguen como propuesta, sin confirmar ni programar.
