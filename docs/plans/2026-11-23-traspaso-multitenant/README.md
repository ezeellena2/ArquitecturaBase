# Traspaso 18 al multitenant: lo último de ArquitecturaBase, hasta el 23/11 (2026-11-23)

Autor: una sesión de ArquitecturaBase, a pedido del usuario («actualicemos con todo lo último a la arquitectura
multitenant»). Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

El [traspaso 16](../2026-11-21-traspaso-multitenant/README.md) dejó la prueba integral al 21/11, y el
[17](../2026-11-22-traspaso-multitenant/README.md) fue la guía de diseño de los catálogos y formularios. Este trae todo lo
demás que se actualizó en ArquitecturaBase hasta hoy, en el backend y en el front, lo que se programó del diseño del 17 y la
prueba integral hasta el 23/11.

- **Rango.** Backend `a7cee7cf..0d464ae5` (37 commits, del 21 al 23/11; cinco son de los traspasos 16 y 17) y front
  `d0cb2c9c..d4a83756` (94 commits, del 20 al 22/11). Más el registro de la prueba integral, que no entra a Git: lo resume el
  03, al 23/11 a la mañana.
- **Lo nuevo:** ninguna regla de negocio fija en el código, con el tope de ofrecimientos del asistente y RC1-A a RC7-B (01); lo
  que se programó del diseño del 17: el orden de las tablas en el backend, la pieza única en el front y el comienzo de la
  respuesta como formulario (02); la prueba integral, con WhatsApp probado entero y las observaciones O31 a O49 (03); y la
  forma de trabajo con varias sesiones a la vez (04).
- **El estado** al 23/11 (05) y **seis preguntas** nuevas, de la 79 a la 84 (06).
- **Sigue parcial.** La prueba integral no cerró: faltan los pasos a mano y el cierre. Lo que cambie va al traspaso 19.
- **No es código para copiar** (tu decisión 18) y tus tableros son tuyos (tu decisión 108).

## Dónde está ArquitecturaBase

- **La prueba integral** probó WhatsApp entero el 21 y el 22/11. Quedan los pasos que van a mano (el micrófono, un celular
  real, el lector de pantalla), dos que hace el usuario y el cierre.
- **Entraron durante la prueba,** con la aprobación del usuario: el tope de ofrecimientos configurable (TO1-A), casi todas
  las reglas que pasaron a Configuración, el orden de las tablas en el backend, la pieza única en el front, el contrato y la
  lectura de la respuesta como formulario, y cinco arreglos.
- **Esperan la reconstrucción final de la app:** la tanda de WhatsApp de «Reglas a Configuración», el front de la de
  Formularios y la pasada final de la pieza única. Después, una corrida completa en verde con la máquina quieta.
- **Sin programar:** el front del orden de las tablas, el resto de la respuesta como formulario, Información del negocio, la
  fecha de un Flow sin calendario (O48), las acciones del asistente (ADR 0034) y los procesos del campo (ADR 0033).

## Reglas para leerlo

1. **Va encima del 15, el 16 y el 17.** Si todavía no incorporaste los traspasos 4 a 17, seguí las tandas del 15 y sumá cada
   archivo de este en la tanda que dice la tabla de abajo. Si ya los incorporaste, este se lee solo.
2. **Manda lo más nuevo.** Si este dice distinto que un traspaso anterior, vale este. Si choca con `docs/features/` de
   ArquitecturaBase, o con `docs/design/` de su front en algo programado, valen esos.
3. **Lo que deja viejo:**
   - el tope fijo de dos ofrecimientos del asistente, en el T2 (anexo A, la fila de `WhatsAppAssistantOfferCapacityPolicy`;
     anexo C, el «Cupo» de W-08) y en el T13/02 («los dos ofrecimientos del día»): ahora se configura, deriva al llenarse y no
     frena un audio (01 §2);
   - el T14/05, «El cupo corta en el día UTC»: ahora es el día del negocio (01 §3, RC5);
   - el 06 del 16 y la tabla «Qué está programado y qué no» del README del 17: los reemplaza el 05;
   - el §6 del 05 del 16, «Lo que falta»: lo reemplaza el 03 §5.
4. **Lo que sigue en programación o en la prueba:** anotalo como riesgo a mirar, sin armar etapas.

## Cómo leerlo

| Archivo | Qué cuenta | Tanda del 15 |
| --- | --- | --- |
| [01 · Reglas en Configuración](01-reglas-en-configuracion.md) | El criterio, el tope de ofrecimientos (TO1-A, con O36 y O39) y las decisiones RC1-A a RC7-B, con sus valores y su estado. | 1 (el núcleo), 3 (E21), 4 (E23 y E25) y 5 (WhatsApp y ARCA) |
| [02 · Lo programado del diseño](02-lo-programado-del-diseno.md) | El orden de las tablas en el backend, la pieza única en el front, la respuesta como formulario e Información del negocio. | 1 (diseño), 2 (E15) y 3 (E20) |
| [03 · La prueba integral](03-prueba-integral.md) | Del 21 al 23/11: dónde va, lo que cambió, las observaciones O31 a O49, lo que enseñó y lo que falta. | Todas |
| [04 · La forma de trabajo](04-forma-de-trabajo.md) | El carril para lo pesado, las tandas en paralelo con una sesión que coordina y los cortes en horas. | 1 (forma de trabajo) |
| [05 · El estado](05-estado-al-23-11.md) | Cómo quedó cada desarrollo al 23/11 y lo que sigue. | — |
| [06 · Las preguntas](06-preguntas.md) | Las preguntas 79 a 84, y dos del 17 que suman contexto. | Cada una, en la suya |

Del 01 al 04, cada archivo termina con «Para el multitenant»: dónde cae en tu modelo y qué tenés que adaptar.

## Dónde está el multitenant

Visto desde acá el 23/11, tu `main` sigue en `c339b58d` y tu front en `c5172e65`, del 1/11, igual en GitHub: la última
decisión de tu hoja de ruta es la 108 y no hay rastro de los traspasos 4 a 17. Si los incorporaste sin subirlos, se saltean.

## Dónde leer

- **Los planes:** en el backend, `docs/plans/2026-11-22-reglas-a-configuracion.md`,
  `docs/plans/2026-11-22-ordenar-tablas-por-columnas.md` y `docs/plans/2026-11-22-respuesta-como-formulario.md`; en el front,
  `docs/plans/2026-11-19-una-sola-pieza-para-elegir.md` y `docs/plans/2026-11-22-informacion-del-negocio-reordenar.md`.
- **Las reglas implementadas:** `docs/features/` (administración, identidad, IA, formularios, WhatsApp y ARCA) y, en el front,
  `docs/design/visual-baseline.md`.
- **El carril:** `harnesses/carril/README.md`.

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. El backend, con
  este paquete, está en GitHub desde el 2026-11-23. El front llega hasta `fbd5538a`: los seis commits del front que cita este
  traspaso después de ese (`fbd5538a..d4a83756`) están sólo en la PC del usuario.

## Prompt para el chat guía del multitenant

```text
Vas a incorporar el traspaso 18 de ArquitecturaBase: lo último que se actualizó hasta el 23/11, con la prueba integral
hasta ese día. Está en ArquitecturaBase/docs/plans/2026-11-23-traspaso-multitenant/README.md: en la PC del usuario, dentro
de C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios.

El usuario quiere que ajustes tu documentación (las specs de cada etapa, la hoja de ruta con sus decisiones, tus reglas y
los tableros del lienzo) con lo que ArquitecturaBase decidió y programó del 21 al 23 de noviembre. No se programa nada.

1. Leé el README del 18 y su 05 (cómo quedó todo al 23/11).
2. Si todavía no incorporaste los traspasos 4 a 17, seguí el prompt del README del traspaso 15
   (ArquitecturaBase/docs/plans/2026-11-18-traspaso-multitenant/README.md), sumá el 16 y el 17 como dicen sus README, y
   sumá cada archivo del 18 en la tanda que dice su tabla «Cómo leerlo». Si ya los incorporaste, seguí con el paso 3.
3. Para cada archivo del 01 al 04:
   a. armá una tabla con lo que trae, dónde cae en tus etapas, tus reglas y tus tableros, y qué cambia en cada uno;
   b. mostrale al usuario el plan de cambios y esperá su visto bueno;
   c. hacele las preguntas del 06 que correspondan, de a una, con las opciones y la recomendada primero. Lo de
      ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
      y decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta;
   d. aplicá los cambios, con commits chicos en español, en main, sin push salvo que el usuario lo pida.
4. Lo que el 18 da como en programación, sin programar o visto en la prueba integral, anotalo sin armar etapas: puede
   cambiar con el traspaso 19.
5. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108). En ArquitecturaBase sólo se lee.

Al terminar, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron pendientes.
```
