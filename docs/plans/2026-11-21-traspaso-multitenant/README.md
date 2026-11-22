# Traspaso 16 al multitenant: lo que cambió con la prueba integral, hasta el 21/11 (2026-11-21)

Autor: una sesión de ArquitecturaBase, a pedido del usuario, mientras sigue la prueba integral. Destino:
ArquitecturaBaseMutitenant y su front.

## Para qué es

El [traspaso 15](../2026-11-18-traspaso-multitenant/README.md) ordenó los once traspasos que el multitenant todavía no
incorporó (del 4 al 14) y dejó dicho que lo que cambiara la prueba integral iría al 16. La prueba arrancó el 19/11 y sigue.
Este paquete trae todo lo que se actualizó en ArquitecturaBase desde el 15, en el backend y en el front, y lo que encontró la
prueba hasta hoy.

- **Rango.** Backend `eaefd851..a7cee7cf` (28 commits, del 18 al 20/11) y front `cf247979..d0cb2c9c` (15 commits, del 18 al
  20/11). Más el registro de la prueba integral, que no entra a Git: lo resume el 05, al 21/11 a las 12:30.
- **Lo nuevo:** el editor de roles con áreas plegables y los permisos que incluyen a otros (01), las carpetas como menús del
  menú lateral (02), dos reglas de diseño para toda la app (03), la IA que prepara y la pantalla que confirma, con los
  procesos del campo (04), y la prueba integral con sus observaciones (05).
- **El estado** al 21/11, con lo que sigue en curso y el barrido documental del 18/11 (06), y **cinco preguntas** nuevas, que
  siguen la numeración del 15, de la 69 a la 73 (07).
- **Es parcial.** La prueba integral no terminó: faltan la mayor parte de WhatsApp, lo que se prueba a mano y el cierre. Lo
  que cambie desde hoy va al traspaso 17.
- **El [traspaso 17](../2026-11-22-traspaso-multitenant/README.md)** (2026-11-22) trae la guía de diseño de los catálogos y
  formularios, con las reglas de diseño del 22/11, y responde la pregunta 14 del 15 (O29 se arregló). Lo demás de la prueba
  integral va al traspaso 18.
- **No es código para copiar** (tu decisión 18) y tus tableros son tuyos (tu decisión 108).

## Dónde está ArquitecturaBase

- **La prueba integral corre desde el 19/11,** con la app levantada. Están hechas, salvo lo de WhatsApp, lo que va a mano y
  dos pasos que hace el usuario: ingreso y cuenta; usuarios y roles; formularios, de los catálogos a la carga; respuestas,
  planilla, aprobación, reglas y avisos; IA y Casos; ARCA sin emitir; y configuración, salud y toda la app. WhatsApp volvió a
  recibir mensajes el 21/11 (O27) y su sección arrancó ese día.
- **Entraron durante la prueba,** con la aprobación del usuario: RO3-D y los permisos que incluyen (19/11), las filas sólo
  con íconos (20/11), el arreglo de la pantalla «Asistente» de WhatsApp (O25, 20/11) y las carpetas como menús (20/11).
- **Esperan el cierre de la prueba:** la corrida completa de las carpetas, la pieza única para elegir (programada en una copia
  del front y sin aplicar) y una corrida completa en verde con la app apagada.
- **Sin programar hasta que el usuario lo diga:** las acciones del asistente (ADR 0034, aceptado) y los procesos del campo
  (ADR 0033, en propuesta).

## Reglas para leerlo

1. **Va encima del 15.** Si todavía no incorporaste los traspasos 4 a 14, seguí las tandas del 15 y sumá cada archivo de este
   en la tanda que dice la tabla de abajo. Si ya los incorporaste, este se lee solo.
2. **Manda lo más nuevo.** Si este dice distinto que un traspaso anterior, vale este. Si choca con `docs/features/` de
   ArquitecturaBase, vale `features`.
3. **Lo que deja viejo:**
   - el T13/05 §3 «Roles» y su «Para el multitenant», que pedía redibujar tu editor como matriz: conservás tus áreas
     plegables (01);
   - lo que el T13/01 §4 contaba del panel de carpetas de los listados (CP1-A), de «En el menú…» (CP5-A) y de los cinco
     niveles (02);
   - la pregunta 30 del 15, que la reemplaza la 72, y la 14, que queda en suspenso por O29 (07);
   - el §1 del 02 del 15 y las filas de su §2 que nombra el 06.
4. **Lo de la prueba es parcial:** anotalo como riesgos a mirar, sin armar etapas nuevas.

## Cómo leerlo

| Archivo | Qué cuenta | Tanda del 15 |
| --- | --- | --- |
| [01 · Roles](01-roles-y-permisos.md) | Las áreas plegables (RO3-D) y las once inclusiones, empezando por «Administrar incluye Ver». | 1, el núcleo |
| [02 · Carpetas](02-carpetas-como-menus.md) | Las carpetas como menús del menú lateral (CM1 a CM6), con dos niveles y su migración en dos despliegues. | 2 |
| [03 · Diseño](03-diseno-para-toda-la-app.md) | Las filas de una tabla, sólo con íconos, y la pieza única para elegir (DS1-A y DS2-A, en curso). | 1, diseño para toda la app |
| [04 · IA y procesos](04-ia-y-procesos-del-campo.md) | El ADR 0034 (la IA prepara y la pantalla de siempre confirma), los procesos del campo con el ADR 0033 y MO1-B. | 4 y 5 |
| [05 · La prueba integral](05-prueba-integral.md) | Cómo se hace, dónde va, lo que cambió por ella, las observaciones O1 a O30, lo que enseñó y lo que falta. | Todas |
| [06 · El estado](06-estado-al-21-11.md) | Cómo quedó todo al 21/11, lo que sigue en curso y el barrido documental del 18/11. | — |
| [07 · Las preguntas](07-preguntas.md) | Las preguntas 69 a 73 y las del 15 que cambian. | Cada una, en la suya |

Del 01 al 05, cada archivo termina con «Para el multitenant»: dónde cae en tu modelo y qué tenés que adaptar.

## Dónde está el multitenant

Visto desde acá el 21/11, tu `main` sigue en `c339b58d` y tu front en `c5172e65`, del 1/11, igual en GitHub: la última
decisión de tu hoja de ruta es la 108 y no hay rastro de los traspasos 4 a 15. Si los incorporaste sin subirlos, se saltean.

## Dónde leer

- **Los planes:** `docs/plans/2026-11-19-roles-areas-plegables-y-permisos-que-incluyen.md`, la sección CM1 a CM6 de
  `docs/plans/2026-11-03-carpetas-de-formularios-y-catalogos.md` y `docs/plans/2026-11-19-procesos-del-campo.md`; en el
  front, `docs/plans/2026-11-19-una-sola-pieza-para-elegir.md`.
- **Las reglas implementadas:** `docs/features/administracion.md` y `docs/features/formularios.md`; en el front,
  `docs/design/visual-baseline.md`, `docs/design/roles-y-permisos.md` y `docs/design/carpetas.md`.
- **Las decisiones:** los ADR 0033 (en propuesta) y 0034 (aceptado), con el diseño de referencia
  `docs/specs/2026-11-19-motores-configurables-design.md`.
- **Lo que falta acá:** `docs/reviews/2026-11-18-barrido-documental.md`.

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Los dos están
  en GitHub desde el 2026-11-22: el backend con este paquete y el 17, y el front hasta `fbd5538a`, con los roles con áreas
  plegables, las carpetas como menús, las filas con íconos y el plan de la pieza única.

## Prompt para el chat guía del multitenant

```text
Vas a incorporar el traspaso 16 de ArquitecturaBase: lo que cambió desde el traspaso 15, con su prueba integral hasta el
21/11. Está en ArquitecturaBase/docs/plans/2026-11-21-traspaso-multitenant/README.md: en la PC del usuario, dentro de
C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios.

El usuario quiere que ajustes tu documentación (las specs de cada etapa, la hoja de ruta con sus decisiones, tus reglas y
los tableros del lienzo) con lo que ArquitecturaBase decidió y programó del 18 al 21 de noviembre. No se programa nada.

1. Leé el README del 16 y su 06 (cómo quedó todo al 21/11).
2. Si todavía no incorporaste los traspasos 4 a 14, seguí el prompt del README del traspaso 15
   (ArquitecturaBase/docs/plans/2026-11-18-traspaso-multitenant/README.md) y sumá cada archivo del 16 en la tanda que dice
   su tabla «Cómo leerlo». Si ya los incorporaste, seguí con el paso 3.
3. Para cada archivo del 01 al 05:
   a. armá una tabla con lo que trae, dónde cae en tus etapas, tus reglas y tus tableros, y qué cambia en cada uno;
   b. mostrale al usuario el plan de cambios y esperá su visto bueno;
   c. hacele las preguntas del 07 que correspondan, de a una, con las opciones y la recomendada primero. Lo de
      ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
      y decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta;
   d. aplicá los cambios, con commits chicos en español, en main, sin push salvo que el usuario lo pida.
4. Lo que el 16 da como en curso, sin programar o visto en la prueba integral, anotalo sin armar etapas: puede cambiar con
   el traspaso 17. La pregunta 14 del 15 queda en suspenso hasta el 17.
5. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108). En ArquitecturaBase sólo se lee.

Al terminar, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron pendientes.
```
