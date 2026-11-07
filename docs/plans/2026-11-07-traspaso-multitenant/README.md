# Traspaso 10 al multitenant: ArquitecturaBase entero, para rehacer maqueta y documentación (2026-11-07)

Autor: un chat de ArquitecturaBase, a pedido del usuario. Destino: ArquitecturaBaseMutitenant. Lo va a trabajar **otra
cuenta de Claude, en otra PC**, que no tiene ni la memoria ni los chats de esta cuenta.

## Para qué es

El usuario quiere que el multitenant vuelva a revisar ArquitecturaBase entero: qué se decidió, qué se programó y qué se
repensó. Con eso tiene que rehacer su maqueta y su documentación, basándose también en el código de acá. Este paquete
junta lo necesario para hacerlo desde cero:

- lo que cambió desde el traspaso 9 (6 y 7 de noviembre);
- lo que sigue en curso, lo que falta decidir y las pruebas manuales pendientes;
- las reglas y preferencias del usuario que hasta hoy sólo guardaba la memoria de esta cuenta;
- un mapa de dónde leer cada cosa en los dos repos;
- el prompt para el chat guía del multitenant, al final.

No es código para copiar: las pantallas y las reglas de producto se copian, y el código sale del multitenant (la decisión
18 de su hoja de ruta).

## Antes de empezar, en la otra PC

1. Traé los dos repos de GitHub, rama `main`: `ezeellena2/ArquitecturaBase` (backend y documentación) y
   `ezeellena2/ArquitecturaBaseFront` (front y diseño registrado). Este paquete está en
   `ArquitecturaBase/docs/plans/2026-11-07-traspaso-multitenant/`.
2. Abrí el chat guía del multitenant en la otra cuenta y pasale el prompt del final.

## Cómo leerlo

| Archivo | Qué cuenta |
| --- | --- |
| [01 · Qué cambió desde el traspaso 9](01-que-cambio.md) | Backend y front, por tema, con los commits y las decisiones del 6 y el 7 de noviembre. |
| [02 · En curso, por decidir y pruebas pendientes](02-en-curso-y-pendiente.md) | Cómo viene cada desarrollo, qué tiene que decidir el usuario y qué falta probar a mano. |
| [03 · Reglas del usuario](03-reglas-del-usuario.md) | Diseño, forma de trabajo, pruebas y comunicación: lo que sabía la memoria de esta cuenta. |
| [04 · Dónde leer cada cosa](04-donde-leer.md) | Las fuentes canónicas, el diseño registrado, las capturas de la interfaz real y el código de referencia. |

## Los traspasos anteriores, en orden

Todos están en `ArquitecturaBase/docs/plans/`. Cada uno se retira cuando el multitenant lo incorpora. Si ya incorporaste
alguno, saltealo. Si no, leelos en este orden antes del 01.

| Traspaso | Fecha | Qué cuenta |
| --- | --- | --- |
| [2](../2026-10-23-traspaso-multitenant/README.md) | 2026-10-23 | El paquete grande: cronología, el producto de punta a punta, reglas del usuario, calidad, forma de trabajo y anexos. |
| [3](../2026-10-30-traspaso-multitenant-carga-desde-la-app.md) | 2026-10-30 | Cargar una respuesta desde la web (etapa 1). |
| [4](../2026-11-02-traspaso-multitenant-ajustes-de-la-prueba.md) | 2026-11-02 | Lo decidido y programado durante la prueba completa del 31/10 y el 1/11. |
| [5](../2026-11-03-traspaso-multitenant-lecturas-y-filtros.md) | 2026-11-03 | Planilla, detalle, reglas en pasos, filtros simples, comparaciones y el diseño de renglones. |
| [6](../2026-11-06-traspaso-multitenant-arca-conexion.md) | 2026-11-06 | ARCA, etapa 1: conexión con el certificado de la empresa, padrón y punto de venta. |
| [7](../2026-11-06-traspaso-multitenant-carga-etapas-2-a-5.md) | 2026-11-06 | La carga desde la app, etapas 2 a 5. |
| [8](../2026-11-06-traspaso-multitenant-ia-arma-formularios.md) | 2026-11-06 | La IA que arma formularios y catálogos, etapa 1. |
| [9](../2026-11-06-traspaso-multitenant-ajustes-de-toda-la-app.md) | 2026-11-06 | Idioma, reintentar un aviso, Mi perfil, el menú sin saltos y controles de la plantilla. |
| **10** | 2026-11-07 | Este paquete. |

## Dónde ver los diseños desde la otra cuenta

- **Lo que viaja con el repo** (la fuente segura):
  - el registro de lo elegido, tablero por tablero, en `ArquitecturaBaseFront/docs/design/*.md`, con sus capturas en
    `docs/design/evidence/`;
  - la interfaz real, en las capturas de referencia de los tests visuales: `ArquitecturaBaseFront/src/test/visual/screenshots/`.
- **El lienzo provisorio «Formularios desde la app»**, con los tableros de las decisiones recientes, es un Artifact de esta
  cuenta: https://claude.ai/artifact/Kvbr5KFJrGT951JLiu8i4M. La otra cuenta no lo puede abrir salvo que el usuario lo
  comparta desde claude.ai.
- **El «Sistema visual» y la biblioteca de piezas** no son de esta cuenta. Si son de la cuenta donde vas a trabajar,
  abrilos desde ahí.

## Prompt para el chat guía del multitenant

```text
Vas a trabajar el traspaso 10 de ArquitecturaBase al multitenant. Antes, traé los dos repos de GitHub, rama main:
ezeellena2/ArquitecturaBase y ezeellena2/ArquitecturaBaseFront. Si ya los tenés, hacé pull. El traspaso está en
ArquitecturaBase/docs/plans/2026-11-07-traspaso-multitenant/README.md.

Este traspaso es distinto de los anteriores. El usuario quiere que vuelvas a revisar ArquitecturaBase entero y que
rehagas la maqueta y la documentación del multitenant con lo que allá quedó decidido y programado, basándote también en
su código. Esta cuenta no tiene la memoria ni los chats donde se decidió: todo lo que hace falta está en los dos repos.

1. Leé el README del traspaso 10 y, en orden, los traspasos 2 a 9 que todavía no hayas incorporado. Después leé el 01
   (qué cambió), el 02 (en curso y pendiente), el 03 (reglas del usuario) y el 04 (dónde leer cada cosa).
2. Desde ahora, trabajá con este usuario según las reglas del 03. Las que choquen con tus reglas o con tu hoja de ruta,
   anotalas para preguntárselas.
3. Recorré las fuentes canónicas de ArquitecturaBase: docs/features, docs/architecture/backend.md y docs/decisions, y en
   el front, docs/design y las capturas de src/test/visual/screenshots. Armá una tabla por área con lo que existe allá,
   lo que tenés vos en tu hoja de ruta, tus specs y tus tableros, y qué cambia.
4. Con esa tabla, proponele al usuario cómo rehacer tu maqueta y tu documentación: qué tableros se rehacen, cuáles se
   suman, cuáles se retiran y en qué orden. Mostrale primero el plan y esperá su visto bueno.
5. Lo que tenga que decidir él, preguntáselo de a una cosa, con las opciones y la recomendada primero.
6. El 02 cuenta lo que sigue en curso (las etapas 2 y 3 de renglones, elegir primero por un campo). Tenelo en cuenta,
   pero no armes etapas con eso hasta que cierre.
7. No es código para copiar (tu decisión 18): no programes. Commits chicos en español, en main, sin push salvo que el
   usuario lo pida.

Al terminar cada paso, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron
pendientes.
```
