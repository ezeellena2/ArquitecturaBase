# Traspaso 14 · 01 · Las revisiones de arquitectura

El 12 de noviembre el usuario pidió poder revisar cada desarrollo contra unos 30 criterios de arquitectura: que no se mezclen
datos entre personas, varias copias de la app, volumen, terceros, seguridad, datos, idioma, fechas, errores, paginado y
filtros. Se armó una guía, se probó con WhatsApp y, entre el 14 y el 16, se revisaron y arreglaron los otros cuatro
desarrollos. Este archivo cuenta la guía, lo que salió y las reglas comunes que quedaron. El detalle de cada área está del 02
al 06.

## La guía

Está en [`docs/guides/revisar-arquitectura.md`](../../guides/revisar-arquitectura.md).

- **30 criterios en 8 bloques**, contestados en **tres horizontes**: «Hoy» (una copia, el volumen actual), «Varias copias»
  (antes de pasar de una réplica) y «Gran escala» (millones de usuarios). Cada criterio da Cumple, Con reservas, No cumple o No
  aplica, con evidencia de archivo y línea.
- **La ficha** del desarrollo (qué abarca, qué terceros toca, qué datos guarda) la arma un agente y la confirma el usuario la
  primera vez. Un desarrollo grande se parte en piezas, cada una con su ficha.
- **Los revisores sólo leen** una copia del commit: no compilan, no corren tests ni levantan la app. **Cada «No cumple» y
  «Con reservas» pasa por un verificador** que intenta refutarlo con el escenario y el código, sin la conclusión. Al final,
  **un revisor de contraste** lee el informe entero contra el código. En el núcleo encontró 23 correcciones, 5 serias.
- **La robustez es proporcional al efecto** (el ADR 0018): el usuario elige qué horizonte se paga. Lo de «Varias copias» y «Gran
  escala» no se programa sin que lo elija.
- **El registro** va a `docs/reviews/` y los arreglos se planifican aparte, anexados al plan del área.

## Lo que salió

| Desarrollo | Revisión | Hallazgos | Decisiones del usuario | Plan | Estado al 16/11 |
| --- | --- | --- | --- | --- | --- |
| WhatsApp | 12/11, `f7da5e4b` | H1–H34 y B1–B4 | Seis, el 13/11, las recomendadas (`77de7dc1`) | 22 tareas en 7 etapas (`be294cdb`) | Programado entero del 12 al 15/11 |
| ARCA | 14/11, `e4febec6` | B1–B5, H1–H37 y O1–O13 | Ocho, las recomendadas (`e7373fc4`) | 34 tareas en 8 etapas (`b8582e48`) | Programado entero el 14 y 15/11; H11 lo resolvió F2 de Formularios. Falta medir el robot (H22) |
| Núcleo | 14/11, `6866d323` | N1–N34, B1–B5 y O1–O12 | Diez: nueve recomendadas y, en N7, Gmail en producción con sus topes (`3627ea95`) | 31 tareas en 6 etapas (`b3e52805`) | Programado entero el 15 y 16/11 |
| IA | 14/11, `f39865a9` | I1–I20 y B1–B5 | Ocho, las recomendadas (`2efcfe77`) | 29 tareas en 7 etapas (`ca38ae78`) | Programado entero el 15 y 16/11 |
| Formularios | 14/11, `39b6de46` | 48 verificados (F1–F48) | Ocho, las recomendadas (`739d8ea5`); el 15/11, «Todo antes» | 41 tareas en 7 etapas (`9825fb2f`) | 39 programadas el 15 y 16/11; faltan F2 y repetir la prueba de quitar el módulo |

En todas el usuario eligió el horizonte «Hoy», más algunos puntos sueltos de los otros que son baratos o evitan perder datos
(por ejemplo N27 del núcleo e I17 de IA). El resto de «Varias copias» y «Gran escala» quedó anotado en cada revisión.

## Las reglas comunes que dejaron

- **Una sola regla de versiones** (N27 del núcleo y H25 de WhatsApp, que IA y Formularios aplicaron a lo suyo). En cada
  despliegue conviven dos versiones un momento, y al volver a la imagen anterior también:
  - lo que una copia no puede leer no se borra: vuelve a la cola con una espera y un tope de intentos;
  - la reserva toma sólo los tipos de mensaje que su versión conoce;
  - un campo, un tipo o un valor nuevo se escribe un despliegue después del que ya sabe leerlo, y un enum guardado por nombre
    lee un nombre desconocido como su valor neutro.
- **Nadie da lo que no tiene** (N1): delegar la administración de usuarios o de roles ya no equivale a ser administrador. Vale
  para los permisos de todos los módulos, y obligó a poner al día los tests que cambian roles.
- **Ediciones simultáneas con revisión y 409** también en usuarios y roles (N2), como ya pasaba con la configuración. Se
  retiró el PUT de ajustes que no la tenía.
- **El contenido sensible se guarda cifrado** (H16 y el ADR 0030): todo lo que dice un chat de WhatsApp, con el nombre y el
  número en claro.
- **Sin Redis la app sigue** (N9 y el ADR 0032): degrada en vez de caerse, y los permisos se leen de la base una vez por
  pedido.
- **Lo que sale del sistema se distingue por su motivo** (fallas del proveedor en IA y en ARCA): cada causa tiene su código y
  su texto, y se puede rastrear en los logs (N10).

## Lo que enseñó la forma de trabajo

- **Varios agentes commiteando a la vez se pisan.** Un commit armado sobre una copia vieja deshizo 34 archivos de N1 (se
  repuso enseguida), y un texto de F5 se borró y volvió. La receta del índice privado tiene que armar el árbol sobre el HEAD
  del momento y fallar si otro commiteó en el medio.
- **Las copias de compilación llenan el disco.** El 15/11 el disco quedó lleno con 387 GB de copias viejas en `.artifacts`. Cada
  sesión borra su copia al terminar.
- **El cupo semanal se acaba.** Las cinco revisiones y sus arreglos, en paralelo, llevaron el cupo al 98 %. Conviene
  planificar las revisiones con su costo medido (cada una usó de 11 a 20 agentes).

## Para el multitenant

- **La guía sirve tal cual** para revisar los módulos del multitenant, con un bloque más pesado: que no se mezclen datos entre
  organizaciones (RLS, el contexto de la organización en los workers, las colas y el caché). Conviene correrla sobre cada
  módulo antes de abrirlo a varias organizaciones.
- **La regla de versiones y «nadie da lo que no tiene»** son reglas de plataforma: si el multitenant las adopta, van en su
  arquitectura y no en un módulo.
- **Las decisiones de horizonte** de ArquitecturaBase asumen una sola instalación por cliente. En el multitenant, varias
  organizaciones comparten copias desde el principio, así que parte de «Varias copias» puede ser «Hoy» allá. Es una pregunta
  para el usuario (07, §3).
