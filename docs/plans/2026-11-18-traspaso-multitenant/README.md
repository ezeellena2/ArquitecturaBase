# Traspaso 15 al multitenant: el orden para incorporar del 4 al 14 (2026-11-18)

Autor: una sesión de ArquitecturaBase, a pedido del usuario. Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

El multitenant incorporó el traspaso 2 (su evaluación del 24/10) y el 3 (sus decisiones 83 a 98). Su `main` sigue en
`c339b58d`, con el front en `c5172e65`, del 1/11, igual en GitHub. Le faltan once traspasos, del 4 al 14, escritos uno por
vez entre el 2 y el 16 de noviembre, cada uno con su prompt. Se pisan entre sí: lo que uno daba «en curso» o «sin programar»,
otro lo cuenta terminado, y las preguntas para el usuario quedaron repartidas en nueve archivos.

Este traspaso no trae desarrollos nuevos: ordena los anteriores.

- Un solo recorrido, en cinco tandas, siguiendo las etapas del multitenant ([01](01-orden-de-lectura.md)).
- Cómo quedó cada desarrollo al 18/11, con lo poco que se hizo después del 14 ([02](02-estado-al-18-11.md)). Reemplaza las
  secciones de estado de todos los anteriores.
- Una sola lista de preguntas para el usuario, sin repetidas y en el orden de las tandas ([03](03-preguntas.md)).
- Un solo prompt, al final, que reemplaza los once de los traspasos 4 a 14.

## Dónde está ArquitecturaBase

- **Todo lo decidido está programado.** El 17/11 cerró en verde: el backend dio 11.656 de 11.656 sobre `02c60e91`, con las
  28 cohortes de contratos del front (`844a5c13`); el front pasó tipos, lint, build y 6.022 tests sobre `93eb8eb0`; la CLI de
  Aspire quedó en 13.6.5 y pasó una prueba de humo.
- **Falta la prueba integral con el usuario:** 181 pasos en nueve secciones. Lo que encuentre se arregla sólo si bloquea.
  Después vienen la auditoría documental, que retira los planes terminados, y el **traspaso 16**, con lo que cambie la
  prueba.
- **Para después de la prueba:** homologar ARCA con CUIT reales, la corrida real del robot, Azure dev y los horizontes de
  «varias copias» y «gran escala» de las revisiones de arquitectura.

## Reglas para leer todo junto

1. **Manda lo más nuevo.** Si dos traspasos dicen distinto, vale el de número mayor. Si un traspaso choca con
   `docs/features/` de ArquitecturaBase, vale `features`, que se actualizó con cada arreglo.
2. **Las secciones de estado de los traspasos 4 a 14 quedaron viejas:** «Pendiente acá», «Lo que falta acá», «En curso o
   sólo planeado», el 02 del 10, el 07 del 13 y el §1 del 07 del 14. Las reemplaza el [02](02-estado-al-18-11.md).
3. **Los prompts de los traspasos 4 a 14 no se usan:** se usa el de abajo. Siguen valiendo sus «No repetir», sus
   «Adaptación» y sus «Para el multitenant».
4. **Las rutas de código** de cada «Dónde está cada cosa» son del día en que se escribió el traspaso. Si una ya no existe,
   buscá la clase por su nombre.
5. **No es código para copiar** (tu decisión 18), y tus tableros son tuyos (tu decisión 108): el lienzo de ArquitecturaBase
   es un ejemplo para adaptar. La interfaz real se ve en las capturas de `ArquitecturaBaseFront/src/test/visual/screenshots/`
   y lo elegido, tablero por tablero, en `ArquitecturaBaseFront/docs/design/`.
6. **Nada de lo programado pasó la prueba integral.** Ya podés ajustar specs, reglas y tableros; las etapas nuevas se arman
   después del traspaso 16.

## Cómo leerlo

| Archivo | Qué cuenta |
| --- | --- |
| [01 · El orden de lectura](01-orden-de-lectura.md) | Las cinco tandas: qué secciones de qué traspasos leer para cada una de tus etapas, en qué orden, y qué saltear. |
| [02 · Cómo quedó todo al 18/11](02-estado-al-18-11.md) | El estado de cada desarrollo, lo hecho después del 14 (la marca de la planilla, la prueba de quitar Formularios, dos arreglos) y lo planeado sin programar. |
| [03 · Las preguntas para el usuario](03-preguntas.md) | 68 preguntas, agrupadas por tanda y cada una con su contexto: las 67 de los traspasos 4 a 14, sin repetidas, y una que suma este orden. |

## Los traspasos que ordena

Todos están en `ArquitecturaBase/docs/plans/`.

| Traspaso | Fecha | Qué cuenta |
| --- | --- | --- |
| [2](../2026-10-23-traspaso-multitenant/README.md) y [3](../2026-10-30-traspaso-multitenant-carga-desde-la-app.md) | 2026-10-23 y 2026-10-30 | Ya incorporados: tu evaluación del traspaso 2 y tus decisiones 83 a 98. |
| [4](../2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md) | 2026-11-02 | Lo decidido en la prueba del 31/10 y el 1/11: leyendas y carga, catálogos, publicar, adjuntar el PDF y aprobar. |
| [5](../2026-11-03-traspaso-multitenant-lecturas-y-filtros.md) | 2026-11-03 | Tablas, lecturas, lo elegido, filtros, el detalle, las reglas en pasos, zonas plegadas y la carga en dos anchos. |
| [6](../2026-11-06-traspaso-multitenant-arca-conexion.md) | 2026-11-06 | ARCA, etapa 1: la conexión con el certificado de la empresa. |
| [7](../2026-11-06-traspaso-multitenant-carga-etapas-2-a-5.md) | 2026-11-06 | La carga desde la app, etapas 2 a 5. |
| [8](../2026-11-06-traspaso-multitenant-ia-arma-formularios.md) | 2026-11-06 | La IA que arma formularios y catálogos, etapa 1. |
| [9](../2026-11-06-traspaso-multitenant-ajustes-de-toda-la-app.md) | 2026-11-06 | El idioma, reintentar un aviso, Mi perfil, el menú sin saltos y la plantilla. |
| [10](../2026-11-07-traspaso-multitenant/README.md) | 2026-11-07 | El paquete para rehacer la maqueta: lo cambiado el 6 y el 7/11, las reglas del usuario y dónde leer cada cosa. |
| [11](../2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md) | 2026-11-08 | Completa el 8: por WhatsApp, un pedido ambiguo pregunta y espera; la corrida con el proveedor real. |
| [12](../2026-11-11-traspaso-multitenant-renglones.md) | 2026-11-11 | Renglones entero. |
| [13](../2026-11-14-traspaso-multitenant/README.md) | 2026-11-14 | Lo decidido y programado del 7 al 14/11. |
| [14](../2026-11-16-traspaso-multitenant/README.md) | 2026-11-16 | Las revisiones de arquitectura y sus arreglos, del 14 al 16/11. |
| **15** | 2026-11-18 | Este paquete: el orden, el estado y las preguntas. |

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Este paquete
  llega a GitHub cuando el usuario pida subirlo; hasta entonces está sólo en su PC.
- **El rango:** los traspasos 4 a 14 cubren el backend hasta `72510c01` y el front hasta `ecc0f561`, sin huecos (cada
  paquete arranca donde terminó el anterior). El 02 de este suma lo que va hasta backend `5356b282` y front `cf247979`.

## Prompt para el chat guía del multitenant

```text
Vas a incorporar los traspasos de ArquitecturaBase que te faltan, del 4 al 14, siguiendo el traspaso 15, que los ordena.
Está en ArquitecturaBase/docs/plans/2026-11-18-traspaso-multitenant/README.md: en la PC del usuario, dentro de
C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios.

El usuario quiere que ajustes tu documentación (las specs de cada etapa, la hoja de ruta con sus decisiones, tus reglas y
los tableros del lienzo) con todo lo que ArquitecturaBase decidió y programó del 2 al 17 de noviembre. No se programa nada.

1. Leé el README del 15 y su 02 (cómo quedó todo al 18/11). Si ya incorporaste algo de los traspasos 4 a 14, decíselo al
   usuario y salteá esas secciones.
2. Seguí las cinco tandas del 01, en orden. En cada una:
   a. leé las secciones que indica, en el orden que indica: lo más nuevo manda;
   b. armá una tabla con lo que traen, dónde cae en tus etapas, tus reglas y tus tableros, y qué cambia en cada uno;
   c. mostrale al usuario el plan de cambios de esa tanda y esperá su visto bueno;
   d. hacele las preguntas de esa tanda del 03, de a una, con las opciones y la recomendada primero. Lo de ArquitecturaBase
      es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida, y decí por qué.
      Lo que decida va como decisión nueva, con el número que siga (hoy la última es la 108);
   e. aplicá los cambios, con commits chicos en español, en main, sin push salvo que el usuario lo pida.
3. La tanda 1 trae reglas de plataforma (forma de trabajo, diseño para toda la app, arquitectura y núcleo): tomalas como
   tuyas desde ahora, salvo lo que el usuario decida distinto en sus preguntas.
4. Lo que el 02 da como pendiente o sin la prueba integral, anotalo sin armar etapas: puede cambiar con el traspaso 16.
5. No es código para copiar (tu decisión 18) y tus tableros son tuyos (tu decisión 108). En ArquitecturaBase sólo se lee.

Al terminar cada tanda, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron
pendientes.
```
