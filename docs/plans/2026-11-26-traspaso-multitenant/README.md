# Traspaso 19 al multitenant: el cierre de ArquitecturaBase, hasta el 26/11 (2026-11-26)

Autor: una sesión de ArquitecturaBase, a pedido de la sesión que cerró los planes terminados. Destino: ArquitecturaBaseMutitenant
y su front.

## Para qué es

El [traspaso 18](../2026-11-23-traspaso-multitenant/README.md) dejó la prueba integral al 23/11 y el diseño del 17 a medias.
Este trae lo que entró desde entonces en los dos repos, hasta que el usuario dio el proyecto por terminado el 26/11.

- **Rango.** Backend `208d1ad0..76bca76d` (42 commits, del 23 al 26/11; cinco son del traspaso 18) y front
  `4e6d9a5c..45c5115f` (90 commits, del 22 al 26/11; el 18 ya contó lo que entró la mañana del 23/11). Los tests y la
  documentación que no cambian el comportamiento van sólo cuando te sirven.
- **Lo nuevo:** lo que terminó de programarse del diseño (el front del orden por columnas, la pasada final de la pieza única, el
  cierre de la respuesta como formulario, Formularios en una sola entrada e Información del negocio) y dos decisiones nuevas, la
  falta de permiso dentro de la app y las tarjetas con datos del chat (01); el cierre de la prueba integral, con O28 y O45 a O60
  resueltas, la respuesta perdida de un Flow (O49) y las decisiones O47 y O48 (02); la regla de cuándo se corre cada test y las
  dos suites en paralelo (03).
- **El estado** al 26/11, con la auditoría de cierre (04), y **cinco preguntas** nuevas, de la 85 a la 89, más la 83 del 18, que
  cambia de recomendada (05).
- **Es el último por ahora.** El proyecto quedó cerrado; lo que falta es a mano o con cuentas reales.
- **No es código para copiar** (tu decisión 18) y tus tableros son tuyos (tu decisión 108).

## Dónde está ArquitecturaBase

- **La prueba integral cerró el 26/11** sin los pasos a mano, por decisión del usuario, con la corrida completa en verde en los
  dos repos. Lo que quedó a mano o con cuentas reales (el micrófono, un celular real, el lector de pantalla, ARCA en
  homologación, el primer despliegue en Azure) está en `docs/plans/verificacion-y-produccion.md`.
- **Se retiraron los planes terminados,** quince del backend y ocho del front: sus decisiones están en `docs/features/`, en el
  fundamento visual del front y en las premisas.
- **Sin programar:** las acciones del asistente (ADR 0034), los procesos del campo (ADR 0033), los tickets en Jira, los
  recorridos con Playwright y los cupos de la IA por persona en pantalla (RC5).

## Reglas para leerlo

1. **Va encima del 16, el 17 y el 18.** El 15 ya lo incorporaste (tus decisiones 224 a 259); esos tres, todavía no. Seguí las
   tandas del 15 y sumá cada archivo de este en la tanda que dice la tabla de abajo.
2. **Manda lo más nuevo.** Si este dice distinto que un traspaso anterior, vale este. Si choca con `docs/features/` de
   ArquitecturaBase, o con `docs/design/` de su front, valen esos.
3. **Lo que deja viejo:**
   - el 05 del 18 (el estado al 23/11): lo reemplaza el 04;
   - el §5 del 03 del 18 («Lo que falta»): lo reemplaza el 02 §4;
   - O48 «decidido el 22/11» en el 03 del 18 y la recomendada de su pregunta 83: la fecha de un Flow queda como la dibuja
     WhatsApp (02 §3 y 05);
   - lo que el 18 dice sin programar o a medias del orden por columnas, la pieza única, la respuesta como formulario,
     Formularios en una sola entrada e Información del negocio: está todo programado (01);
   - «Dónde está el multitenant» y el prompt del README del 18: los reemplazan los de este.
4. **Lo que quedó abierto** (02 §4) se anota como riesgo, sin armar etapas.

## Cómo leerlo

| Archivo | Qué cuenta | Tanda del 15 |
| --- | --- | --- |
| [01 · Lo que terminó de programarse](01-lo-que-termino-de-programarse.md) | El front de OT1-C, la pasada final de la pieza única, el cierre de VF1-A y VF2-A, FU1-B y FU2-A, IN1-C, la falta de permiso (SP1-B) y las tarjetas del chat (TC1-B a TC5-A). | 1 (diseño), 2 (E14 y E15), 3 (E21) y 4 (E23) |
| [02 · El cierre de la prueba integral](02-cierre-de-la-prueba-integral.md) | Las observaciones resueltas desde el 18, la respuesta perdida de un Flow (O49), las decisiones O47 y O48, lo que quedó abierto y lo que enseñó. | Todas |
| [03 · La forma de trabajo y los tests](03-forma-de-trabajo-y-tests.md) | Cuándo se corre cada test, la integración del backend en 32 colecciones y el Vitest con la mitad de los hilos. | 1 (forma de trabajo) |
| [04 · El estado](04-estado-al-26-11.md) | El proyecto cerrado, la auditoría de cierre y cómo quedó cada desarrollo. | — |
| [05 · Las preguntas](05-preguntas.md) | Las preguntas 85 a 89 y las de antes que cambian. | Cada una, en la suya |

Del 01 al 03, cada archivo termina con «Para el multitenant»: dónde cae en tu modelo y qué tenés que adaptar.

## Dónde está el multitenant

Visto desde acá el 26/11, tu `main` está en `b2334284` (20/11), igual que tu copia de GitHub, y tu front en `c5172e65`: el 15
está adentro, con tus decisiones 224 a 259, y seguís con la segunda parte de la base de la 12. Tu módulo de formularios todavía
no tiene código. El 18 decía que seguías en `c339b58d`: era una lectura vieja.

## Dónde leer

- **La procedencia de los planes retirados,** con la regla de cada uno y sus pendientes: la sección «Cierre del 2026-11-26» de
  `docs/history/README.md`.
- **Las reglas implementadas:** `docs/features/` (formularios, administración, identidad, IA, WhatsApp y ARCA) y, en el front,
  `docs/design/visual-baseline.md`.
- **Lo de a mano y con cuentas reales:** `docs/plans/verificacion-y-produccion.md`.

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. El front con todo
  lo que cita este traspaso está en GitHub (hasta `45c5115f`), y el backend hasta `76bca76d`; este paquete todavía no: se sube
  cuando el usuario lo pida.

## Prompt para el chat guía del multitenant: incorporar del 16 al 19

Reemplaza al prompt del traspaso 18: con este se empieza por todo lo que el multitenant todavía no tomó.

```text
Vas a revisar e incorporar lo que ArquitecturaBase te pasó y todavía no tomaste: los traspasos 16, 17, 18 y 19. Del 2 al 15
ya están en tu hoja de ruta (el 15 con tus decisiones 224 a 259). No se programa nada: se ajustan tu documentación (las specs
de cada etapa, la hoja de ruta con sus decisiones, tus reglas y tu forma de trabajo) y los tableros del lienzo.

Dónde están: en ArquitecturaBase/docs/plans/, con el índice en docs/plans/README.md. En la PC del usuario,
C:\Users\ezequ\source\repos\ArquitecturaBase y C:\Users\ezequ\source\repos\ArquitecturaBaseFront; si no, en GitHub
(ezeellena2/ArquitecturaBase y ezeellena2/ArquitecturaBaseFront, rama main): traé los cambios antes de leer. En
ArquitecturaBase sólo se lee.

1. Leé primero los cuatro README: los del 16, el 17, el 18 y el 19 (docs/plans/2026-11-21-traspaso-multitenant/,
   2026-11-22-traspaso-multitenant/, 2026-11-23-traspaso-multitenant/ y 2026-11-26-traspaso-multitenant/). Cada uno dice qué
   suma, en qué tanda del 15 cae cada archivo y qué deja viejo. El estado vigente es el 04 del 19: reemplaza las secciones de
   estado de todos los anteriores. Usá las cinco tandas del 15 (docs/plans/2026-11-18-traspaso-multitenant/01-*.md) para
   ordenar el trabajo; el 15 ya está incorporado y no se vuelve a revisar.
2. Reglas: manda lo más nuevo. Si un traspaso dice distinto que uno anterior, vale el más nuevo; si choca con docs/features de
   ArquitecturaBase, o con docs/design de su front, valen esos. Para el diseño de catálogos y formularios, el 17 manda sobre
   lo repartido en los anteriores, con lo que el 18 y el 19 cuentan que se programó. Lo que figura sin programar, abierto o a
   mano se anota como riesgo, sin armar etapas.
3. Recorré las cinco tandas en orden. En cada una:
   a. armá una tabla con lo que traen para esa tanda los archivos del 16 al 19, dónde cae cada cosa en tus specs, tu hoja de
      ruta, tus reglas y tus tableros, y qué cambia;
   b. mostrale al usuario el plan de cambios de la tanda y esperá su visto bueno;
   c. hacele las preguntas de esa tanda, de a una, con las opciones y la recomendada primero: las del 16 (69 a 73), el 17
      (74 a 78), el 18 (79 a 84) y el 19 (85 a 89), con los cambios que cada traspaso anota a las anteriores (el 19 cambia
      la recomendada de la 83). Lo de ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado
      salvo que tu modelo lo impida, y decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última
      de tu hoja de ruta (hoy, la 259); lo ya decidido no se vuelve a preguntar;
   d. aplicá los cambios con commits chicos en español, en main, sin push salvo que el usuario lo pida;
   e. contale al usuario qué cambió y qué quedó pendiente, y esperá su visto bueno antes de la tanda siguiente.
4. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108): lo que sirva se adapta a tu modelo
   de organizaciones, empresas y plataforma.

Empezá por los cuatro README. Antes de cambiar nada, mostrale al usuario una tabla corta con los traspasos que vas a
incorporar, cómo se reparten en las cinco tandas y cuántas preguntas tiene cada tanda, y arrancá por la tanda 1 cuando te
dé el visto bueno.
```
