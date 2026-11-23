# Traspaso 18 al multitenant: lo último de ArquitecturaBase, hasta el 23/11 (2026-11-23)

Autor: una sesión de ArquitecturaBase, a pedido del usuario («actualicemos con todo lo último a la arquitectura
multitenant»). Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

El [traspaso 16](../2026-11-21-traspaso-multitenant/README.md) dejó la prueba integral al 21/11, y el
[17](../2026-11-22-traspaso-multitenant/README.md) fue la guía de diseño de los catálogos y formularios. Este trae todo lo
demás que se actualizó en ArquitecturaBase hasta hoy, en el backend y en el front, lo que se programó del diseño del 17 y la
prueba integral hasta el 23/11.

- **Rango.** Backend `a7cee7cf..208d1ad0` (42 commits, del 21 al 23/11; cinco son de los traspasos 16 y 17) y front
  `d0cb2c9c..4e6d9a5c` (102 commits, del 20 al 23/11). Más el registro de la prueba integral, que no entra a Git: lo resume el
  03, al 23/11 a la mañana.
- **Lo nuevo:** ninguna regla de negocio fija en el código, con el tope de ofrecimientos del asistente y RC1-A a RC7-B (01); lo
  que se programó del diseño del 17: el orden de las tablas en el backend, la pieza única en el front y casi toda la respuesta
  como formulario, más Formularios en una sola entrada, elegida el 23/11 (02); la prueba integral, con WhatsApp probado entero
  y las observaciones O31 a O49 (03); y la forma de trabajo con varias sesiones a la vez (04).
- **El estado** al 23/11 (05) y **seis preguntas** nuevas, de la 79 a la 84 (06).
- **Sigue parcial.** La prueba integral no cerró: faltan los pasos a mano y el cierre. Lo que cambie va al traspaso 19.
- **No es código para copiar** (tu decisión 18) y tus tableros son tuyos (tu decisión 108).

## Dónde está ArquitecturaBase

- **La prueba integral** probó WhatsApp entero el 21 y el 22/11. Quedan los pasos que van a mano (el micrófono, un celular
  real, el lector de pantalla), dos que hace el usuario y el cierre.
- **Entraron durante la prueba,** con la aprobación del usuario: el tope de ofrecimientos configurable (TO1-A), las reglas
  que pasaron a Configuración, el orden de las tablas en el backend, la pieza única en el front, casi toda la respuesta como
  formulario y cinco arreglos.
- **Esperan la reconstrucción final de la app,** con todo lo que ya está en `main`, y la pasada final de la pieza única.
  Después, una corrida completa en verde con la máquina quieta.
- **Sin programar:** el front del orden de las tablas, Información del negocio, Formularios en una sola entrada, la fecha de un
  Flow sin calendario (O48), las acciones del asistente (ADR 0034) y los procesos del campo (ADR 0033).

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
| [02 · Lo programado del diseño](02-lo-programado-del-diseno.md) | El orden de las tablas en el backend, la pieza única en el front, la respuesta como formulario, Información del negocio y Formularios en una sola entrada. | 1 (diseño), 2 (E14 y E15) y 3 (E20) |
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
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. El backend con
  este paquete y el front con todos los commits que cita este traspaso están en GitHub desde el 2026-11-23.

## Prompt para el chat guía del multitenant: revisar todo, del 4 al 18

Reemplaza a los prompts de los traspasos 15, 16 y 17: con este se empieza por todo lo que el multitenant todavía no tomó.

```text
Vas a revisar e incorporar todo lo que ArquitecturaBase te pasó y todavía no tomaste: los traspasos 4 al 18. El 2 y el 3
ya están en tu hoja de ruta. No se programa nada: se ajustan tu documentación (las specs de cada etapa, la hoja de ruta con
sus decisiones, tus reglas y tu forma de trabajo) y los tableros del lienzo.

Dónde están: en ArquitecturaBase/docs/plans/, con el índice en docs/plans/README.md. En la PC del usuario,
C:\Users\ezequ\source\repos\ArquitecturaBase y C:\Users\ezequ\source\repos\ArquitecturaBaseFront; si no, en GitHub
(ezeellena2/ArquitecturaBase y ezeellena2/ArquitecturaBaseFront, rama main): traé los cambios antes de leer. En
ArquitecturaBase sólo se lee.

1. Leé primero los cuatro README que ordenan todo:
   - el del traspaso 15 (docs/plans/2026-11-18-traspaso-multitenant/README.md): ordena del 4 al 14 en cinco tandas por tus
     etapas (su 01) y junta las preguntas 1 a 68 (su 03);
   - los del 16, el 17 y el 18 (docs/plans/2026-11-21-traspaso-multitenant/, 2026-11-22-traspaso-multitenant/ y
     2026-11-23-traspaso-multitenant/): cada uno dice qué suma, en qué tanda del 15 cae cada archivo y qué deja viejo.
   El estado vigente es el 05 del 18: reemplaza las secciones de estado de todos los anteriores. Si ya incorporaste del 4
   al 17, saltealos y aplicá el mismo método sólo al 18.
2. Reglas: manda lo más nuevo. Si un traspaso dice distinto que uno anterior, vale el más nuevo; si choca con
   docs/features de ArquitecturaBase, o con docs/design de su front en algo programado, valen esos. Para el diseño de
   catálogos y formularios, el 17 manda sobre lo repartido en los anteriores. Lo que figura en programación, sin programar
   o visto en la prueba integral se anota como riesgo, sin armar etapas.
3. Recorré las cinco tandas del 15 en orden. En cada una:
   a. armá una tabla con lo que traen para esa tanda el 15 y los archivos del 16, el 17 y el 18 que caen ahí, dónde cae
      cada cosa en tus specs, tu hoja de ruta, tus reglas y tus tableros, y qué cambia;
   b. mostrale al usuario el plan de cambios de la tanda y esperá su visto bueno;
   c. hacele las preguntas de esa tanda, de a una, con las opciones y la recomendada primero: las del 15 (1 a 68), el 16
      (69 a 73), el 17 (74 a 78) y el 18 (79 a 84), con los cambios que cada traspaso anota a las anteriores. Lo de
      ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
      y decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta; lo ya
      decidido no se vuelve a preguntar;
   d. aplicá los cambios con commits chicos en español, en main, sin push salvo que el usuario lo pida;
   e. contale al usuario qué cambió y qué quedó pendiente, y esperá su visto bueno antes de la tanda siguiente.
4. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108): lo que sirva se adapta a tu modelo
   de organizaciones, empresas y plataforma.

Empezá por los cuatro README. Antes de cambiar nada, mostrale al usuario una tabla corta con los traspasos que vas a
incorporar, cómo se reparten en las cinco tandas y cuántas preguntas tiene cada tanda, y arrancá por la tanda 1 cuando te
dé el visto bueno.
```
