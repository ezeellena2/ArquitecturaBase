# 06 · Forma de trabajo

Complementa el [02 del traspaso del 17/10](https://github.com/ezeellena2/ArquitecturaBaseMutitenant/blob/main/docs/plans/2026-10-17-formularios-traspaso/02-forma-de-trabajo.md). Lo que sigue es lo que cambió o se aprendió desde entonces.

## Tres chats con roles fijos

| Chat | Qué hace | Qué no hace |
| --- | --- | --- |
| **Guía** | Coordina: planes, briefs, ADR, specs, prompts completos para el codificador, code reviews con agentes de solo lectura y decisiones con el usuario | No programa ni dibuja |
| **Diseño** | Dibuja el lienzo (tableros para elegir), mantiene la maqueta viva y anota lo elegido en `docs/design/` del front | No toca código ni decide producto |
| **Codificador** | Programa una etapa entera a partir de un prompt completo: TDD, commits chicos y cierre | No reabre decisiones |

- **Desde el 2026-10-23 el codificador es un chat de Claude,** con agentes en paralelo y verificación de cada lote, y no Codex.
- **Los chats se pasan mensajes entre sí.** El usuario elige y decide; la guía le presenta las opciones con una recomendación y nunca da nada por elegido sin él.

## Prompts completos, de una sola vez

El usuario pidió que los prompts para el codificador sean **completos y en un solo bloque**, no por partes. Cada prompt lleva:
- **el alcance cerrado,** punto por punto, aunque remita a un documento con el detalle;
- **las decisiones nuevas,** marcadas como del usuario y con fecha;
- **cómo trabajar:**
  - contrastar cada punto con el código antes de arreglarlo;
  - la regresión en rojo primero;
  - commits chicos;
  - chequeos rápidos por punto;
- **el cierre una sola vez:** suite completa con Docker, export conjunto de contratos, front completo, visuales, recorrido en el navegador es/en a 390 y 320 px, y `aspire stop`;
- **qué no hacer:** no empezar el piloto, no hacer push y no reabrir decisiones.

## Code reviews

- **Al cerrar cada etapa, un review independiente:**
  - tres revisores de solo lectura en paralelo, por ejemplo dos para el back y uno para el front;
  - cada hallazgo verificado en el código, con gravedad, ubicación, escenario y arreglo.
- **El review queda como documento,** en `docs/reviews/`, con:
  - «Lo que está bien»;
  - «Arreglar ahora», agrupado por gravedad;
  - «Después, sin bloquear»;
  - «Para el piloto».
- **Lo que el usuario encuentra al usar la app se suma al mismo review** como puntos extra. Así se corrige todo en una sola tanda.
- **El codificador registra el cierre del review** con una fila por punto.

## Maqueta viva

- **Qué es:** un repo aparte (`maqueta-formularios`) con una copia del front real, tomada con `git archive`. Tiene:
  - un backend falso en memoria;
  - personas para elegir (Ana, Juan, Laura…) e idioma;
  - un celular simulado que recibe los Flows;
  - las pantallas no programadas, marcadas «Propuesta».
- **Cómo se publica:** como Artifact, en https://claude.ai/artifact/CM56W2Yc4VVzwMmxCKFpdP.
- **Se refresca** cuando el codificador cierra una etapa.
- **Por qué existe:** el usuario la pidió porque los tableros estáticos no le alcanzaban para entender un recorrido.

## Guías por carpeta y su tamaño

- **Cada carpeta de capa, área o adaptador tiene su `AGENTS.md`** con su `CLAUDE.md`, que lo importa.
- **El tope de tamaño:** Codex lee hasta 32 KiB de la cadena desde la raíz; al pasarse, perdía reglas sin avisar. Desde el 21/10 cada cadena queda por debajo de 30 KB ([regla](../../guides/instrucciones-por-carpeta.md#tamaño-de-las-guías)).
- **Si el multitenant usa Codex o un agente con un límite parecido,** conviene medir sus cadenas.

## Decisiones del usuario

- **Las decisiones de un plan no se vuelven a preguntar.** Lo que el plan no fija se resuelve con la opción más simple que cumpla las reglas.
- **Cuando hay que elegir,** se presentan pocas opciones claras, con una recomendada y la consecuencia de cada una.
- **El usuario pierde el hilo de lo pendiente** y pidió que se le recuerde. La fuente es el índice de planes.
