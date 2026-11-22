# Traspaso 17 al multitenant: cómo se diseñan los catálogos y formularios dinámicos (2026-11-22)

Autor: una sesión de ArquitecturaBase, a pedido del usuario. Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

El usuario notó que costaba fijar pautas de diseño para las pantallas de catálogos y formularios dinámicos: el orden, cómo
se diseñan las tablas, los filtros, los desplegables, la selección múltiple. Pidió aprender de todo lo que se hizo y se tuvo
que corregir en ArquitecturaBase, para que el multitenant diseñe bien cada cosa desde el principio y no tenga los mismos
problemas, porque si no, los formularios quedan feos al verlos y la información no se entiende.

- **No cuenta un desarrollo:** junta en una guía las reglas de diseño de esas pantallas al 22/11, cada una con el error que la
  enseñó, y las compara con lo que dicen hoy las reglas y las specs del multitenant.
- **Trae lo que se decidió el 22/11,** durante la prueba integral: la respuesta que se lee como el formulario (VF1-A), la
  actividad fila por fila (VF2-A), «nada debajo de otro dato», ordenar las tablas por sus columnas (OT1-C) y la pieza única
  para todo lo que se elige, también las opciones fijas, la carga y los filtros.
- **Para estas pantallas, manda sobre el diseño repartido en los traspasos anteriores** (T4 §2, T5 §2 a §5, T9 §2, T13/05,
  T14/06 §1 y el 03 del 16). Lo que no cubre sigue en ellos.
- **Lo demás que pasó desde el 16** (los arreglos de la prueba, las reglas que pasan a Configuración, el aviso del
  certificado de ARCA) va al traspaso 18, con el cierre de la prueba integral. Acá entra sólo O29, porque responde la
  pregunta 14 del 15.
- **No es código para copiar** (tu decisión 18) y tus tableros son tuyos (tu decisión 108).

## Qué está programado y qué no

| Reglas | Estado en ArquitecturaBase |
| --- | --- |
| Un dato por columna, líneas entre columnas y números a la derecha, fechas y números, lo elegido, las esperas, los avisos, el motivo de lo apagado, las filas con íconos, las acciones del encabezado, los filtros simples, las lecturas en pasos y lado a lado, la carga con dos anchos y el editor de catálogos | Programado y en la app |
| La pieza única para elegir (DS1-A y DS2-A) y su extensión del 22/11 | Programada en una copia del front; se aplica al cerrar la prueba integral |
| Ordenar por columnas (OT1-C) | El backend de la regla común, Formularios, Usuarios y Roles, programado; las demás listas y el front, después de la pieza única |
| La respuesta como el formulario (VF1-A), la actividad fila por fila (VF2-A) y «nada debajo de otro dato» | Decidido el 22/11, sin programar |

## Cómo leerlo

| Archivo | Qué cuenta | Tanda del 15 |
| --- | --- | --- |
| [01 · Los principios y lo aprendido](01-principios-y-lo-aprendido.md) | Diez principios, cada uno con el error que lo enseñó; cómo se trabaja un diseño para no repetirlos; y la lista de chequeo. | 1 |
| [02 · Los listados](02-listados.md) | La tabla, el orden por columnas, el paginado, las acciones y los estados. | 1 |
| [03 · Los filtros y elegir](03-filtros-y-elegir.md) | La barra de filtros y la pieza única para todo lo que se elige. | 1 |
| [04 · La respuesta](04-la-respuesta.md) | Cómo se carga, se lee (VF1-A), se sigue (VF2-A), se ve en la planilla y se exporta. | 2 y 3 (E15, E19, E20 y E21) |
| [05 · Los editores y los diálogos](05-editores-y-dialogos.md) | El editor de un catálogo y el de un formulario, y cómo se arma un diálogo. | 2 (E12 a E14) |
| [06 · Las preguntas](06-preguntas.md) | Las preguntas 74 a 78, y las del 15 y el 16 que cambian. | Cada una, en la suya |

Del 01 al 05, cada archivo termina con «Para el multitenant»: qué coincide, qué choca y qué falta en tus reglas y tus specs.

## Reglas para leerlo

1. **Va encima del 15 y del 16.** Si todavía no incorporaste los traspasos 4 a 16, seguí las tandas del 15 y leé el 01, el 02
   y el 03 de este al empezar la tanda 1, como base del diseño; el 04 y el 05, al llegar a sus etapas.
2. **Manda lo más nuevo.** Si este dice distinto que un traspaso anterior, vale este. Si choca con `docs/features/` o con
   `docs/design/` del front de ArquitecturaBase en algo programado, valen esos, que se actualizan con cada cambio. Lo
   decidido el 22/11 todavía no está escrito ahí (entra cuando se programe): para eso vale este.
3. **Lo que deja viejo:** el detalle de una respuesta en dos columnas con tablas chicas (TL1-A) y la actividad de TL3-A, que
   reemplazan VF1-A y VF2-A; la regla de poner de dos a cuatro opciones fijas en radios o un segmentado, del 03 del 16; y la
   pregunta 71 del 16, que queda reemplazada por la 74.
4. **Lo decidido sin programar** (VF1-A, VF2-A, «nada debajo de otro dato» y el front de OT1-C) ya podés tomarlo para tus
   specs y tus tableros: el usuario lo eligió. Los detalles finos pueden cambiar al programarlo acá, y el traspaso que siga
   los trae.

## Dónde está el multitenant

Visto desde acá el 22/11, tu `main` sigue en `c339b58d` y tu front en `c5172e65`, igual en GitHub. Tus formularios están en
specs, sin programar: los tableros de la E15 están «A revisar», los de la E20 sin dibujar y varios de la E21 «Por dibujar».
Es el momento de aplicar esta guía, antes de dibujarlos.

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Hoy tienen el
  backend hasta `67d5b944` y el front hasta `91b5556d`. Los traspasos 16 y 17 están sólo en la PC del usuario hasta que pida
  subirlos.
- **La interfaz real** se ve en las capturas de `ArquitecturaBaseFront/src/test/visual/screenshots/`; los planes y diseños que
  cita cada archivo están en `docs/plans/` del backend y en `docs/plans/` y `docs/design/` del front.

## Prompt para el chat guía del multitenant

```text
Vas a incorporar el traspaso 17 de ArquitecturaBase: una guía de cómo se diseñan las pantallas de catálogos y formularios
dinámicos (listados, orden, filtros, desplegables, selección múltiple, la respuesta, los editores), con lo que
ArquitecturaBase tuvo que corregir y por qué. Está en
ArquitecturaBase/docs/plans/2026-11-22-traspaso-multitenant/README.md: en la PC del usuario, dentro de
C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios, si el usuario ya lo subió.

El usuario quiere que tus formularios y catálogos se diseñen bien desde el principio y no repitan los problemas de
ArquitecturaBase. No se programa nada: se ajustan tu documentación (las specs de cada etapa, la hoja de ruta, tus reglas de
docs/rules) y los tableros del lienzo.

1. Leé el README del 17 y su 01 (los principios, lo aprendido y la lista de chequeo).
2. Si todavía no incorporaste los traspasos 4 a 16, seguí el prompt del README del traspaso 15
   (ArquitecturaBase/docs/plans/2026-11-18-traspaso-multitenant/README.md), sumá el 16 como dice su README y usá el 01, el 02
   y el 03 de este como base de la tanda 1; el 04 y el 05, en las etapas que indica su tabla «Cómo leerlo».
3. Para cada archivo del 01 al 05:
   a. armá una tabla con cada regla, dónde cae en tus reglas, tus specs y tus tableros, y si coincide, choca o falta;
   b. mostrale al usuario el plan de cambios y esperá su visto bueno;
   c. hacele las preguntas del 06 que correspondan, de a una, con las opciones y la recomendada primero. Lo de
      ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
      y decí por qué. Lo que decida va como decisión nueva, con el número que siga a la última de tu hoja de ruta;
   d. aplicá los cambios, con commits chicos en español, en main, sin push salvo que el usuario lo pida.
4. Sumá la lista de chequeo del 01 como paso de tu Diseño: todo tablero la pasa antes de mostrarse y antes de programarse.
   Pasala sobre los tableros de la E15, la E20 y la E21 antes de dibujarlos o revisarlos.
5. Lo que el 17 da como decidido sin programar, tomalo para tus specs y tableros; los detalles pueden cambiar con el
   traspaso que siga.
6. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108). En ArquitecturaBase sólo se lee.

Al terminar, contale al usuario qué cambió en tus reglas, tus specs y tus tableros, y qué decisiones quedaron pendientes.
```
