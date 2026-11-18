# Traspaso 14 al multitenant: las revisiones de arquitectura y sus arreglos (2026-11-16)

Autor: la sesión de arquitectura de ArquitecturaBase, a pedido del usuario. Destino: ArquitecturaBaseMutitenant y su front.

## Para qué es

Entre el 12 y el 14 de noviembre se revisó la arquitectura de los cinco desarrollos de ArquitecturaBase con una misma guía de 30
criterios: WhatsApp, ARCA, el núcleo, IA y Formularios. El usuario tomó las decisiones de cada revisión, y del 14 al 16 se
programaron sus arreglos. Este traspaso le cuenta al multitenant qué cambió, para que ajuste su documentación: las specs de
cada etapa, la hoja de ruta con sus decisiones, sus reglas y los tableros del lienzo.

- **Rango.** Lo programado desde el traspaso 13 (backend `23797fff` y front `7b95095b`, del 2026-11-14) hasta backend
  `72510c01` y front `ecc0f561` (2026-11-16): 288 commits del backend y 45 del front.
- **Lo que no repite.** La revisión de WhatsApp y las etapas 1 a 5 de sus arreglos están en el
  [03 del traspaso 13](../2026-11-14-traspaso-multitenant/03-whatsapp-y-asistente.md).
- **No es código para copiar** (la decisión 18 del multitenant): se toman las reglas de producto, las pantallas y las
  decisiones, y el código sale del multitenant, con su modelo de organizaciones, RLS, feature gates, empresas y clientes.
- **Está programado, pero no pasó la prueba integral.** El 2026-11-17 se terminaron el cierre en verde, la marca F2 de la
  planilla de Formularios y la prueba de quitar Formularios; falta la prueba integral con el usuario. Hasta entonces pueden
  cambiar detalles; el traspaso siguiente traerá lo que cambie.
- **Si todavía no incorporaste los traspasos 4 a 13,** empezá por el [traspaso 15](../2026-11-18-traspaso-multitenant/README.md)
  (2026-11-18): ordena del 4 al 14 en un solo recorrido, junta sus preguntas y trae lo hecho después de este.

## Cómo leerlo

| Archivo | Qué cuenta |
| --- | --- |
| [01 · Las revisiones de arquitectura](01-revisiones-de-arquitectura.md) | La guía de 30 criterios, lo que salió de cada revisión, las decisiones del usuario y las reglas comunes que dejaron, como la de versiones. |
| [02 · El núcleo](02-nucleo.md) | Las 31 tareas: quién puede qué, ediciones simultáneas e historia, el correo, Redis, la salud y el arranque, datos, archivos y retención, y documentación. |
| [03 · WhatsApp](03-whatsapp.md) | Lo posterior al 13: el contenido del chat cifrado, la traza de punta a punta, la regla de versiones y los últimos avisos. |
| [04 · ARCA](04-arca.md) | Las 34 tareas: lo que trababa la emisión, quién pone a emitir, restauraciones y esperas, la conexión, el padrón y los clientes, el robot. |
| [05 · IA](05-ia.md) | Las 29 tareas: cupo, borrado y vencimientos, versiones del motor, fallas del proveedor, apagado, fechas y el anillo de claves. |
| [06 · Formularios](06-formularios.md) | Las 41 tareas: números por región, avisos, permisos de las reglas, temas que faltan, la traza, pedidos repetidos y versiones. |
| [07 · En curso y pendiente](07-en-curso-y-pendiente.md) | Lo que falta para cerrar, lo que quedó para después y las preguntas que el chat del multitenant tiene que hacerle al usuario. |

Cada archivo termina con «Para el multitenant»: dónde cae en su modelo y qué tiene que adaptar.

## Los traspasos anteriores

Visto desde acá, el `main` del multitenant (local y en GitHub) sigue en `c339b58d`, con el front en `c5172e65`, del 1/11:
no hay rastro de que haya incorporado los traspasos 4 a 13. Si los incorporó sin subirlos, se saltean.

| Traspaso | Fecha | Qué cuenta |
| --- | --- | --- |
| 2 a 9 | 2026-10-23 a 2026-11-06 | Los lista, en orden, el [README del 10](../2026-11-07-traspaso-multitenant/README.md). |
| [10](../2026-11-07-traspaso-multitenant/README.md) | 2026-11-07 | ArquitecturaBase entero, para rehacer la maqueta y la documentación. |
| [11](../2026-11-08-traspaso-multitenant-ia-pregunta-y-corrida.md) | 2026-11-08 | La IA: el pedido ambiguo por WhatsApp pregunta y espera, y la corrida con el proveedor real. |
| [12](../2026-11-11-traspaso-multitenant-renglones.md) | 2026-11-11 | Renglones entero. |
| [13](../2026-11-14-traspaso-multitenant/README.md) | 2026-11-14 | Lo decidido y programado del 7 al 14 de noviembre. |
| **14** | 2026-11-16 | Este paquete. |

Si el 13 está pendiente, se hace junto con este: el 13 cuenta los desarrollos y el 14, cómo quedaron después de revisarlos.

## Dónde leer y dónde ver

- **La guía y las revisiones:** `docs/guides/revisar-arquitectura.md` y `docs/reviews/2026-11-12-arquitectura-whatsapp.md`,
  `2026-11-14-arquitectura-arca.md`, `2026-11-14-arquitectura-nucleo.md`, `2026-11-14-arquitectura-ia.md` y
  `2026-11-14-arquitectura-formularios.md`, cada una con su sección de decisiones.
- **Cada tarea con sus commits:** la sección «Arreglos de la revisión de arquitectura» de
  `docs/plans/2026-10-01-bandeja-whatsapp.md`, `2026-10-31-arca-formularios-estandar.md` y
  `2026-10-04-panel-conversaciones-inicio.md` (IA), y los planes de arreglos del núcleo y de Formularios,
  retirados el 2026-11-18 ([procedencia](../../history/README.md#barrido-documental-del-2026-11-18)).
- **Las reglas implementadas:** `docs/features/`, al día con cada arreglo.
- **Las decisiones nuevas:** los ADR 0030 (el contenido del chat cifrado), 0031 (la fecha rechazada y el número adelantado de
  ARCA) y 0032 (sin Redis la app sigue). Cambiaron el 0007, el 0009, el 0010 y el 0028.

## Antes de empezar

- **En la PC del usuario:** `C:\Users\ezequ\source\repos\ArquitecturaBase` y `C:\Users\ezequ\source\repos\ArquitecturaBaseFront`.
- **En otra PC:** en GitHub, `ezeellena2/ArquitecturaBase` y `ezeellena2/ArquitecturaBaseFront`, rama `main`. Los dos se
  subieron el 2026-11-16 a pedido del usuario: el backend con este traspaso y el front hasta `ecc0f561`.

## Prompt para el chat guía del multitenant

```text
Vas a trabajar el traspaso 14 de ArquitecturaBase al multitenant. Está en
ArquitecturaBase/docs/plans/2026-11-16-traspaso-multitenant/README.md: en la PC del usuario, dentro de
C:\Users\ezequ\source\repos\ArquitecturaBase; si no, en GitHub (ezeellena2/ArquitecturaBase y
ezeellena2/ArquitecturaBaseFront, rama main), después de traer los cambios.

El usuario quiere que ajustes la documentación del multitenant (las specs de cada etapa, la hoja de ruta con sus
decisiones, tus reglas y los tableros del lienzo) con lo que ArquitecturaBase revisó, decidió y programó del 14 al 16 de
noviembre.

1. Leé el README del 14. Si todavía no incorporaste el 13 (y los anteriores que lista el 10), hacelos juntos: el 13
   cuenta los desarrollos y el 14, cómo quedaron después de revisarlos.
2. Leé del 01 al 07. El 01 trae la guía de revisión de arquitectura y las reglas comunes que dejó, como la de versiones:
   tomalas como reglas tuyas desde ahora.
3. Armá una tabla por área con lo que trae el 14, dónde cae en tus etapas, en tus reglas y en tus tableros, y qué cambia
   en cada uno.
4. Mostrale al usuario el plan de cambios a tu documentación (qué specs, decisiones, reglas y tableros cambian, se suman
   o se retiran, y en qué orden) y esperá su visto bueno.
5. Hacele las preguntas del 07, §3, y las que encuentres, de a una, con las opciones y la recomendada primero. Lo de
   ArquitecturaBase es lo más reciente que eligió el usuario: proponelo como recomendado salvo que tu modelo lo impida,
   y decí por qué. Lo que decida va como decisiones nuevas, con el número que siga.
6. Lo que el 07 da como pendiente o todavía sin la prueba integral, anotalo sin armar etapas: puede cambiar.
7. No es código para copiar (tu decisión 18) y en ArquitecturaBase sólo se lee: no programes. Commits chicos en español,
   en main, sin push salvo que el usuario lo pida.

Al terminar cada paso, contale al usuario qué cambió en tus specs, tus reglas y tus tableros, y qué decisiones quedaron
pendientes.
```
