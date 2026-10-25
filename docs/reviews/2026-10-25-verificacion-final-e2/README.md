# Verificación final de las correcciones de Catálogos E2 — 2026-10-25

El chat «Codificador» corrigió los 39 puntos del [review de Catálogos E2](../2026-10-23-code-review-catalogos-e2.md), del 23 al 25 de octubre. Al final, tres verificaciones de solo lectura y las revisiones de cada lote dejaron hallazgos menores en el front, agrupados en seis lotes.

Los lotes quedaron en la carpeta temporal del codificador y se copian acá, tal como estaban, para que no se pierdan.

| Lote | Hallazgos | Estado al 2026-10-25 |
| --- | ---: | --- |
| [FX01](FX01.md) | 8 | Marcas de error de «Cambiar el tipo». Los commits están hechos, pero falta la revisión independiente |
| [FX02](FX02.md) | 9 | Sin corregir. Conteos sin separador de miles, «Generar código» que prefiere el español, foco después de un error y restos de los puntos 2, 4, 11, 14, 23, 32 y 35 |
| [FX03](FX03.md) | 9 | Sin corregir. Esperas después de un 429, entre ellas la lista de personas de «Como si lo recibiera», que se vuelve a pedir sola |
| [FX04](FX04.md) | 9 | Sin corregir. Restos del punto 38: esperas que se muestran como texto debajo de un botón, como en «Reenviar invitación» y «Escuchar audio» |
| [FX05](FX05.md) | 10 | Sin corregir. El foco no vuelve después de «Usar borrador actual», «Agregar franja» cambia de lugar y hay deshabilitados sin el motivo como tooltip |
| [FX06](FX06.md) | 6 | Sin corregir. Tests más realistas del asistente y guías que digan exactamente lo que hace el código |

Cada lote dice dónde está cada hallazgo, la evidencia y el arreglo propuesto. Los lotes mencionan otros briefs del codificador (`Bxx.md`, `Fxx.md`, `F49.md`) que no se copiaron; el pedido de cada punto está en el review.
