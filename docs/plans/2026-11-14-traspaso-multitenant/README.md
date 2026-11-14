# Traspaso 13 al multitenant: lo decidido y programado del 7 al 14 de noviembre (2026-11-14)

Autor: un chat de ArquitecturaBase, a pedido del usuario. Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

El usuario pidió documentar los últimos ajustes y desarrollos de ArquitecturaBase para que el multitenant ajuste su
documentación: las specs de cada etapa, la hoja de ruta con sus decisiones, sus reglas y los tableros del lienzo.

- **Rango.** Lo decidido y programado desde el traspaso 10 (backend `785e8645` y front `6f440628`, del 2026-11-07) hasta
  backend `23797fff` y front `7b95095b` (2026-11-14). Son unos 450 commits del backend y 260 del front.
- **Lo que no repite.** Renglones está entero en el traspaso 12, y los ajustes de la etapa 1 de la IA, en el 11.
- **No es código para copiar** (la decisión 18 del multitenant): se toman las reglas de producto, las pantallas y las
  decisiones, y el código sale del multitenant, con su modelo de organizaciones, RLS, feature gates, empresas y clientes.
- **Casi todo está programado, pero no pasó la prueba integral.** El usuario quiere probar todo junto una sola vez, cuando
  terminen los desarrollos en curso. Hasta entonces pueden cambiar detalles; el traspaso siguiente traerá lo que cambie. Lo
  que el 07 da como en curso no se convierte en etapas todavía.

## Cómo leerlo

| Archivo | Qué cuenta |
| --- | --- |
| [01 · Formularios](01-formularios.md) | El motor para documentos (cálculos, condiciones, resultado externo), la biblioteca de estándares, los catálogos que mantiene un módulo, las carpetas, «sólo en la app», el Excel en una hoja, la pregunta «Ubicación», lo nuevo de elegir primero por un campo, «Ordenar por…» y arreglos con regla nueva. |
| [02 · Documentos e IA](02-documentos-e-ia.md) | La lectura de documentos con IA, el circuito por WhatsApp, la bandeja, armar formularios desde ella, el tablero de cada formulario, la ubicación y el contacto que se comparten, varias cargas desde un Excel y las etapas 2 y 3 de la IA. |
| [03 · WhatsApp y el asistente](03-whatsapp-y-asistente.md) | La revisión de arquitectura de WhatsApp y sus arreglos, los casos del asistente y los perfiles nuevos del Flow. |
| [04 · ARCA](04-arca.md) | La factura C, A y B, la delegación guiada, el robot y lo que sumó la conexión. |
| [05 · Diseño](05-diseno-en-toda-la-app.md) | La lista de chequeo de diseño y las reglas nuevas para todas las pantallas, Roles y los diálogos. |
| [06 · Arquitectura y forma de trabajo](06-arquitectura-y-forma-de-trabajo.md) | Lo que cambió en la arquitectura, en cómo se revisa y se valida, en el despliegue, y las reglas nuevas del usuario. |
| [07 · En curso y pendiente](07-en-curso-y-pendiente.md) | Cómo viene cada desarrollo, lo planeado sin programar y las preguntas que el chat del multitenant tiene que hacerle al usuario. |

Cada archivo termina con «Para el multitenant»: dónde cae en las etapas del multitenant y qué choca con su hoja de ruta.

## Los traspasos anteriores

Visto desde acá, el `main` del multitenant (local y en GitHub) sigue en `c339b58d`, con el front en `c5172e65`, del 1/11:
no hay rastro de que haya incorporado los traspasos 4 a 12. Si los incorporó sin subirlos, se saltean.

| Traspaso | Fecha | Qué cuenta |
| --- | --- | --- |
| 2 a 9 | 2026-10-23 a 2026-11-06 | Los lista, en orden, el [README del 10](../2026-11-07-traspaso-multitenant/README.md). |
| [10](../2026-11-07-traspaso-multitenant/README.md) | 2026-11-07 | ArquitecturaBase entero, para rehacer la maqueta y la documentación, con las reglas del usuario (su 03) y dónde leer cada cosa (su 04). |
| [11](../2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md) | 2026-11-08 | La IA: el pedido ambiguo por WhatsApp pregunta y espera, y la corrida con el proveedor real. |
| [12](../2026-11-11-traspaso-multitenant-renglones.md) | 2026-11-11 | Renglones entero, con lo que exige Meta y la prueba de punta a punta. |
| **13** | 2026-11-14 | Este paquete. |

Si el 10 está pendiente, se hace junto con este: la revisión entera que pide el 10 ya lee las fuentes al día, y el 13 dice
qué cambió en esta semana, qué no se probó todavía y qué choca con la hoja de ruta del multitenant.

## Dónde leer y dónde ver

- **Las reglas implementadas:** `ArquitecturaBase/docs/features/` (`formularios.md`, `whatsapp.md`,
  `inteligencia-artificial.md`, `arca.md`, `identidad.md` y `administracion.md`, con el detalle en `formularios-*.md` y
  `whatsapp-servicios.md`). Cada sección dice desde cuándo vale.
- **La arquitectura y las decisiones:** `docs/architecture/backend.md`, `docs/decisions/` (hasta el ADR 0029) y las guías
  `docs/guides/validar-un-cambio.md`, `revisar-arquitectura.md`, `configurar-arca.md`, `robot-arca.md` y `configurar-ia.md`.
- **El diseño registrado:** `ArquitecturaBaseFront/docs/design/`, con lo elegido tablero por tablero. Son nuevos o crecieron
  `visual-baseline.md`, `motor-para-documentos.md`, `carpetas.md`, `formularios-solo-en-la-app.md`,
  `ubicacion-en-formularios.md`, `documentos.md`, `arca-delegacion.md`, `arca-factura-c.md`, `arca-factura-a-y-b.md`,
  `arca-robot.md`, `acciones-del-encabezado.md`, `dialogos-con-la-lista-de-chequeo.md` y `roles-y-permisos.md`.
- **La interfaz real, sin levantar la app:** las 428 capturas de `ArquitecturaBaseFront/src/test/visual/screenshots/`, en
  español e inglés, a 1440, 390 y 320 de ancho.
- **Los lienzos de cada desarrollo** figuran en su documento de diseño. Son Artifacts de la cuenta de ArquitecturaBase: otra
  cuenta no los abre salvo que el usuario los comparta. La fuente segura es el documento de diseño.

## Antes de empezar

- **En la PC del usuario:** los repos están en `C:\Users\ezequ\source\repos\ArquitecturaBase` y
  `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Los dos se
  subieron el 2026-11-14 a pedido del usuario: el backend con este traspaso y el front hasta `7b95095b`.

## Prompt para el chat guía del multitenant

```text
Vas a trabajar el traspaso 13 de ArquitecturaBase al multitenant. Está en
ArquitecturaBase/docs/plans/2026-11-14-traspaso-multitenant/README.md: en la PC del usuario, dentro de
C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios.

El usuario quiere que ajustes la documentación del multitenant (las specs de cada etapa, la hoja de ruta con sus
decisiones, tus reglas y los tableros del lienzo) con lo que ArquitecturaBase decidió y programó del 7 al 14 de
noviembre.

1. Leé el README del 13. Si todavía no incorporaste los traspasos 10 a 12 (y los que lista el 10), hacelos junto con
   este: el 10 pide revisar ArquitecturaBase entero y ya lee las fuentes al día; el 13 te dice qué cambió en esa semana,
   qué todavía no se probó y qué choca con tu hoja de ruta.
2. Leé del 01 al 07. El 06 suma reglas nuevas del usuario: trabajá con ellas desde ahora.
3. Armá una tabla por área con lo que trae el 13, dónde cae en tus etapas (E12 a E25 y «Después del bloque»), en tus
   reglas y en tus tableros, y qué cambia en cada uno.
4. Mostrale al usuario el plan de cambios a tu documentación (qué specs, decisiones, reglas y tableros cambian, se suman
   o se retiran, y en qué orden) y esperá su visto bueno.
5. Hacele las preguntas del 07, §4, y las que encuentres, de a una, con las opciones y la recomendada primero. Lo de
   ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
   y decí por qué. Lo que decida va como decisiones nuevas de tu §5, con el número que siga.
6. Lo que el 07 da como en curso o todavía sin la prueba integral, anotalo sin armar etapas: puede cambiar.
7. No es código para copiar (tu decisión 18) y en ArquitecturaBase sólo se lee (tu 108): no programes. Commits chicos
   en español, en main, sin push salvo que el usuario lo pida.

Al terminar cada paso, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron
pendientes.
```
