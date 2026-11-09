# El Excel de respuestas, en una sola hoja

Fecha: **2026-11-09**. Estado: **propuesta, sin elegir**. Nada está programado.

En la prueba integral, exportando «Remito de prueba» (RMP-20261109.xlsx, dos respuestas y cinco ítems), el usuario pidió
que el Excel de «Exportar a Excel» quede en una sola hoja y bien diseñado. Los tableros están en el lienzo aparte
[Diálogos con la lista de chequeo](https://claude.ai/artifact/NUTGFtcXh7QPq8wnpJ6K6c), fila «El Excel de respuestas, en
una sola hoja»: XL0 (hoy), XL1-A, XL1-B y XL1-C (las formas) y XL2 (lo que vale con cualquiera y el diálogo).

## Hoy

Lo arma `FormExportService` con `FormExportPolicy` y lo escribe `ClosedXmlFormWorkbookWriter`; los textos están en
`Modules/BusinessForms/Resources/Responses.resx`. La regla vigente está en
[formularios](../features/formularios.md) («El Excel», en renglones) y en
[formularios-servicios](../features/formularios-servicios.md#planilla-detalle-y-excel).

- **Dos hojas.** «Respuestas», una fila por respuesta, y una hoja por bloque («Artículo»), con «Número», «Ítem» y sus
  preguntas, una fila por ítem.
- **El bloque en «Respuestas» es su cantidad de ítems**, en «Artículo (desde v4)». Al lado, «Artículo» con «—» es la
  pregunta de la versión 1: dos columnas con casi el mismo nombre.
- **La hora en UTC.** «Recibida (UTC)» con el formato «2026-11-09 18:42:06»; la planilla muestra 15:42, la hora de la cuenta.
- **Otras palabras.** «Teléfono» donde la planilla dice «WhatsApp» (el detalle, «Por WhatsApp»), y «Número» donde dice «N°».
- **El formato.** El encabezado va en negrita, fijo al bajar y con filtros; todas las columnas miden 26, quepa o no.

## Las formas (XL1)

| Forma | Cómo queda | A favor | En contra |
| --- | --- | --- | --- |
| **XL1-A · Una fila por ítem** (recomendada) | Una hoja. Cada ítem es una fila, con los datos de su respuesta repetidos a la izquierda; en el lugar del bloque, «Ítem» y sus preguntas. Una respuesta sin ítems ocupa una fila con «Ítem» vacío. Una línea más marcada separa las respuestas. | Es la forma que Excel espera para renglones: se filtra («Estado» = «Dañado»), se ordena y se arma una tabla dinámica sin unir hojas. Sirve igual con 3 ítems o con 100. | Los datos de la respuesta se repiten: sumar una pregunta de la respuesta suma cada fila. Para contar respuestas, «Ítem» = 1 o vacío, o «N°» sin repetir. |
| **XL1-B · Una fila por respuesta, los ítems en columnas** | «Ítems» con la cantidad y, a la derecha, «Ítem 1 · Artículo», «Ítem 1 · Estado», «Ítem 2 · …», hasta el que más ítems tenga en el archivo. | Una fila por respuesta, como la planilla. | Con 100 ítems y tres preguntas, 300 columnas. Un mismo dato repartido en muchas columnas: no se filtra ni se arma una tabla dinámica por ítem. |
| **XL1-C · Cada respuesta con sus ítems debajo** | La fila de la respuesta y, debajo, una fila por ítem con sólo sus columnas, agrupadas con el esquema de Excel (se pliegan con «−»). | Se lee como el detalle de la app, sin repetir datos. | Dos clases de fila en una tabla: el filtro de un ítem deja afuera su respuesta, ordenar los separa y no hay tabla dinámica. |

**Recomendación: XL1-A.** Es la forma en que se trabajan renglones en Excel. Filtrar o contar ítems es lo que se
quiere hacer con un bloque, como el filtro «en algún ítem de Artículo» de la planilla, y lo único que se repite son
datos que ya existen. El diálogo dice cómo quedan las filas.

## Lo que vale con cualquier forma (XL2)

- **La hora de la app.** «Recibida» en la zona de la cuenta que exporta (la de su perfil y, si no tiene, la de la
  configuración general), como fecha de Excel y con el formato del idioma. La conversión se hace al escribir; lo guardado
  sigue en UTC.
- **Las palabras de la planilla.** «N°», «Recibida», «Cargó», «Canal» con «WhatsApp» o «App», «Estado» con los estados de
  la app y «Anulada» con «Sí».
- **«(desde vN)» se queda.** El archivo no tiene «Columnas» para decirlo y distingue dos columnas con el mismo nombre
  (la «Artículo» de la versión 1 y la del bloque), aunque la planilla lo saque del encabezado (UD4).
- **El formato.** El encabezado, como hoy, y con el tono de la planilla; cada columna del ancho de lo que lleva, con un
  tope para los textos largos.
- **Igual que hoy:** «—» para una pregunta que no existía y vacío para una sin respuesta; de fotos y documentos, la
  cantidad y los nombres; qué respuestas salen, el tope de 10.000 y la espera de un 429.

## El diálogo

El renglón «Hojas» del diálogo propuesto (EX1-A) pasa a ser «Filas»: «Una por ítem de Artículo», con la (i) «Los datos
de cada respuesta se repiten en sus ítems. Una respuesta sin ítems ocupa una fila.»; sin bloque, o con el bloque fuera
de «Solo las visibles», «Una por respuesta». Lo programa el
[plan de los diálogos](../../../ArquitecturaBaseFront/docs/plans/2026-11-09-dialogos-con-la-lista-de-chequeo.md).

## Plan, con XL1-A

Cada tarea arranca con su test en rojo y cierra con el build y los tests unitarios y de arquitectura del área.

1. **Los textos.** `Responses.resx` y `.en.resx`: «N°», «Recibida», «WhatsApp» para el canal `phone` y «App» («Ítem»
   ya existe). `ResourceParityTests` y `FormExportServiceTests` (los encabezados).
2. **La hora de la cuenta.** El servicio toma la zona del actor (`UserAccount`) o la general, y convierte
   `SubmittedAtUtc` al escribir; el formato de fecha y hora sale de la cultura del pedido. Test con `FakeTimeProvider` y
   una zona de prueba: 18:42 UTC sale 15:42.
3. **Una hoja, una fila por ítem.** `FormExportService` deja de armar una hoja por bloque: en el lugar de la columna del
   bloque van «Ítem» y sus preguntas (con «· valor» y los campos de `catalogFields`, como en la hoja de hoy), y cada
   respuesta suma una fila por ítem. Una respuesta sin ítems lleva «Ítem» vacío; una de una versión sin el bloque, «—» en
   sus preguntas. Sin el bloque en «Solo las visibles», no hay columnas del ítem. Si dos versiones tienen bloques
   distintos, comparten «Ítem» y cada bloque suma sus preguntas. `FormExportPolicy.SheetName` se retira. Tests:
   bloque con ítems, sin ítems, versión sin bloque, «Solo las visibles» con y sin el bloque, campos de catálogo del ítem
   y anuladas.
4. **El formato.** `FormWorkbookSheet` dice dónde empieza cada respuesta; `ClosedXmlFormWorkbookWriter` pone el tono del
   encabezado, el ancho por contenido con tope y la línea entre respuestas. Test del escritor sobre el archivo generado.
5. **El tope, medido.** El tope sigue siendo de 10.000 respuestas, y los ítems ya salen hoy, uno por fila, en la hoja del
   bloque; ahora también repiten las columnas de su respuesta. Se mide con el máximo (10.000 respuestas con 100 ítems)
   dentro de los 30 segundos de hoy. Si no entra, se decide con el usuario un tope de filas, con su motivo en «Exportar
   a Excel».
6. **Integración y documentos.** Los tests de exportación con PostgreSQL (`FormResponseHttpTests`, `FormRowsReadHttpTests`
   y `FormRecordAmendmentHttpTests`) esperan una hoja. «El Excel» en [formularios](../features/formularios.md) y en
   [formularios-servicios](../features/formularios-servicios.md#planilla-detalle-y-excel) dicen lo que hace el código.
7. **El front.** «Filas» en el diálogo, con el plan de los diálogos. El contrato HTTP del pedido no cambia.

Cierre: `dotnet test` completo con Docker y el build sin advertencias; en el front, los tests del diálogo. A mano: exportar
«Remito de prueba» y abrirlo en Excel, con el filtro de un ítem y una tabla dinámica.

## Antes de empezar

- La elección del usuario: XL1 (A, B o C) y los puntos de XL2.
- El front quieto para la tarea 7, que va con el diálogo (EX1-A).
- Si UD4 saca «(desde vN)» del encabezado de la planilla, el Excel lo conserva igual.
