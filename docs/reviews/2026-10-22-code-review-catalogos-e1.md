# Code review: Catálogos E1 — 2026-10-22

Revisión de solo lectura de backend `e871e292..4a4c283e` y front `0365ec5..738300c`, contra la [spec de catálogos](../specs/2026-10-21-catalogos-design.md), el plan retirado (`catalogos-carga-tipo-y-varias.md`) y los tableros CA1-A y CA2–CA6. Cubre la carga de una vez, el tipo de valor y elegir varias.

**Lo que está bien:**
- **Arreglos previos:**
  - el código único es diferido, así que intercambiar códigos funciona;
  - el tope de 10.000 se cumple en todos los lugares, sin cortes residuales a 500;
  - el prellenado acepta r8 y r9;
  - las listas de las respuestas se clasifican por su mapeo.
- **Migraciones:**
  - los datos existentes quedan como texto, sin valor y en la posición 0;
  - los CHECK están en una línea;
  - los `Down` se niegan antes de perder datos;
  - el snapshot no tiene `\r`.
- **Huellas:** todo lo nuevo que se sella lleva `JsonIgnore(WhenWritingNull)`. Las sesiones viejas conservan su huella.
- **Importación:**
  - la vista previa no abre transacción;
  - aplicar relee el archivo, compara el SHA-256 y revalida permisos y revisión bajo lock;
  - las confirmaciones son exactas;
  - exportar y reimportar da cero diferencias;
  - los permisos están bien;
  - las rutas están en el inventario y en OpenAPI.
- **Elegir varias:**
  - la identidad nueva al cambiar el tipo;
  - la captura con mínimo, máximo, repetidos, opción no ofrecida e ítems inactivos;
  - «alguna de» en filtros, reglas y sugerencias;
  - Excel y PDF;
  - el asistente y las sugerencias la excluyen;
  - en WhatsApp r9, `CheckboxGroup` con su presupuesto y el decode validado.
- **Front:**
  - **CA1-A completo:** la tabla editable en pantallas anchas, el editor por opción en el celular, la selección por página, el contador, los repetidos, generar códigos, A–Z como marca, «Usado en N» y la vista previa con el ajuste de claridad;
  - **pegar e importar:** pegar va por la misma vista previa del servidor; el archivo nunca va a la URL, el storage ni la MutationCache; el envío multipart en `shared/api` no rompe a los otros consumidores;
  - **los valores:** se convierten sin perder precisión;
  - **los contratos:** zod estrictos contra los cuerpos exportados.

Se corrige en un solo chat, back y front juntos, cada arreglo con su test y commits chicos. Las decisiones no cambian. Después va el piloto con Meta junto con lo pendiente ([verificación](../plans/verificacion-y-produccion.md)).

## Arreglar ahora

**Media: pueden guardar datos mal sin que se note**

1. **Importar interpreta números y fechas sin el idioma del catálogo** (`FormCatalogValuePolicy.NormalizeImport`, `:37-48`).
   - **Qué pasa:**
     - en un catálogo decimal en español, «1.500» se guarda como **1,5**, porque primero prueba el formato invariante;
     - en inglés, «1,500» también queda 1,5;
     - una fecha «3/4/2026» en inglés se lee siempre día primero.
   - **Por qué no se nota:** el editor del front lo lee distinto (1.500), y la vista previa no muestra el valor de las filas nuevas. El error queda sellado en las respuestas, el Excel y el PDF.
   - **Arreglo:**
     - interpretar con la cultura que ya llega a la importación, igual que el editor del front;
     - si un decimal queda ambiguo (`\d{1,3}(\.\d{3})+` sin coma), rechazarlo como ambiguo;
     - mostrar el valor en la vista previa de las filas nuevas y cambiadas.
2. **Una columna opcional que falta borra datos de las opciones existentes** (`FormCatalogImportPolicy.cs:58-60,158`).
   - **Qué pasa:** pegar o importar solo «Código / Texto» sobre opciones existentes pone en blanco el inglés y los detalles.
   - **Arreglo:** si la columna no está, se conserva el valor actual; solo una columna presente y vacía lo limpia. La reactivación cuando falta «Activa» sigue como está, que es intencional.
3. **Los topes del XLSX no acotan lo que carga ClosedXML** (`ClosedXmlFormSpreadsheetReader.Archive.cs:118-126`, `ClosedXmlFormSpreadsheetReader.cs:63`). Es plausible.
   - **Qué pasa:** se cuentan solo las celdas con valor. Las celdas vacías con estilo, las filas vacías y las otras hojas se cargan igual: un archivo de pocos MB puede ocupar mucha memoria en cada pedido.
   - **Arreglo:** contar todas las celdas y filas de todas las hojas contra un tope total antes de cargar, o leer solo la primera hoja en streaming.
4. **El freno de más de 20 opciones no dice la causa.**
   - **Dónde:**
     - en el chat, `Forms.Record.CatalogTooLargeForMultiple` cae en «el catálogo no se puede usar» (`FormCatalogPreparation.tsx:8-11,21`);
     - el texto de la comprobación de publicar (`Forms.Publication.CatalogTooLargeForMultiple`) tampoco dice qué hacer.
   - **Arreglo:** un texto propio en los dos lugares, es/en, que diga que le tocarían más de 20 opciones y que hay que filtrar o usar una pregunta de una opción, como pide CA4.
5. **Pegar rechaza todo si una celda sin comillas tiene `"`** (`catalogPaste.ts:167-168`). Por ejemplo, `Caño 1/2"`, que es común en repuestos.
   - **Arreglo:** una comilla dentro de una celda sin comillas es literal; abre comillas solo al empezar la celda.
6. **La revisión de importación mezcla errores y avisos** (`CatalogImportDialog.tsx:82,99`).
   - **Qué pasa:**
     - los avisos (texto repetido, columna desconocida) se ven igual que las filas que no entran, y como el servidor corta en 100, pueden tapar los errores;
     - la columna desconocida aparece como «Columna», sin nombre;
     - «Repetido» no dice con qué fila.
   - **Arreglo:**
     - separar o marcar Error y Aviso;
     - mostrar el encabezado real y «fila N» desde `relatedRow`;
     - alinear el fixture de la prueba con el texto real del servidor.

**Baja**

7. **Una segunda hoja ancha rechaza un archivo válido,** sin decir cuál (`ClosedXmlFormSpreadsheetReader.Archive.cs:124-126`). Pasa, por ejemplo, con una exportación con varias hojas.
   - **Arreglo:** nombrar la hoja, o aplicar a las demás solo el tope de bytes.
8. **Una excepción rara de ClosedXML sale como 500** en lugar de «no se pudo leer el archivo» (`ClosedXmlFormSpreadsheetReader.cs:35`).
   - **Arreglo:** mapear cualquier excepción que no sea de cancelación a `FileUnreadable`.
9. **Los decimales de fórmulas salen con 17 cifras** (`ClosedXmlFormSpreadsheetReader.cs:97`): `0.1+0.2` da `0.30000000000000004`.
   - **Arreglo:** redondear a 15 cifras significativas, como Excel.
10. **El `Down` de elegir varias solo mira las preguntas publicadas** (`20261022125008_AddFormMultipleChoiceValues.cs:54`). Un borrador con `multiChoice` pasa el rollback.
    - **Arreglo:** mirar también el esquema del borrador.
11. **Pegar usa textos pensados para archivos** (`CatalogImportDialog.tsx:55,57,94`): se ve «Pegado.csv» también en inglés, y el error de tamaño habla de archivos.
12. **La vista previa dice «hasta 200 opciones» también en «Varias»** (`CatalogPreview.tsx:124`), cuando el tope es 20.
13. **La tarjeta del chat con varias opciones repite el catálogo** (`FormReplyValues.tsx:32`) y no dice a qué opción corresponde cada detalle.
14. **Textos distintos del diseño** (`es/forms.json:528-541`):
    - «Pegar de Excel» → «Pegar desde Excel»;
    - «Plantilla vacía» → «Descargar la plantilla»;
    - «Generar códigos faltantes» → «Generar los códigos que faltan»;
    - «sólo» → «solo».
15. **Las tablas de Altas, Cambios y Bajas de la revisión no tienen encabezados** (`CatalogImportDialog.tsx:100-102`).

**Tests que faltan:**
- decimal y fecha por idioma, con «1.500» y «3/4/2026»;
- columnas ausentes con inglés y detalle cargados;
- celdas con estilo y varias hojas;
- pegado con comillas internas;
- el código de más de 20 en el chat;
- el 409 en la vista previa;
- avisos con severidad Warning.

## Después, sin bloquear

- **Una columna obligatoria que falta o está repetida** llega como 400 y el front muestra un aviso genérico, en lugar del error en el diálogo. Es el error típico de pegar sin encabezados.
- **«Aplicar» con cero diferencias** sube la revisión sin cambios.
- **Las opciones de las reglas** devuelven hasta 10.000 por pregunta de catálogo: la respuesta crece con preguntas × opciones.
- **El primer endpoint que modifica datos con `multipart/form-data`:** confirmar que la Api autentica con bearer y no con cookies. Si alguna vez acepta cookies, exigir un encabezado propio contra CSRF.
- **La spec,** a alinear con lo que se implementó:
  - los archivos del front quedaron en `CatalogImportDialog`, `catalogEditing.ts` y `CatalogPreview`, en lugar de `useCatalogImport.ts`, `catalogCodes.ts` y `CatalogWhatsAppPreview.tsx`;
  - el error de valor por fila es `Forms.Catalog.ItemValueInvalid`, no `Forms.CatalogImport.ValueInvalid`.

## Contraste y corrección

El contraste inicial con las fuentes confirma los fallos de 1–6, 8–13 y 15. El punto 7 conserva deliberadamente la validación de todas las hojas: el defecto es no identificar cuál incumple el límite. El 14 es parcial: algunas etiquetas del diálogo ya coinciden y sólo se corrigen las restantes. Las regresiones de cada punto se ejecutan antes de su arreglo; el cierre se registra al quedar en verde.

| Punto | Estado |
| --- | --- |
| 1 | Corregido: cultura, ambigüedad, origen canónico y valores visibles. Regresiones en verde: 152 unitarias de backend, 33 del lector físico y 41 del front; builds sin advertencias. La calificación HTTP conjunta se ejecuta al cerrar. |
| 2 | Corregido: cabecera opcional ausente conserva cada valor; presente vacía o fila corta lo limpia. Reactivación intencional intacta. 72 regresiones de importación en verde y build sin advertencias. |
| 3 | Corregido: presupuesto físico global y expansión matricial antes de ClosedXML, contando cada hoja incluso con alias. 46 tests técnicos, 7 de arquitectura y 12 de paridad en verde; build sin advertencias. |
| 4 | Corregido en chat y publicación: causa y recuperación propias en es/en para más de 20 opciones. 58 tests de backend y 25 del front en verde; builds sin advertencias. |
| 5 | Corregido: las comillas internas de una celda sin comillas son literales. 15 tests de pegado y diálogo en verde y build sin advertencias. |
| 6 | Corregido: errores primero, Error/Aviso visible, encabezado real y fila relacionada; los conteos completos se conservan. 75 regresiones de backend y 36 del front en verde, incluido el 409 de vista previa; builds sin advertencias. |
| 7 | Corregido el diagnóstico con el nombre real de la hoja en es/en; se conserva el tope. 2 regresiones del lector, 7 de arquitectura y 42 del front en verde; builds sin advertencias. |
| 8 | Corregido: cualquier excepción no cancelación del constructor ClosedXML se convierte en FileUnreadable, sin cubrir el algoritmo propio. 35 tests técnicos en verde y build sin advertencias. |
| 9 | Corregido: G15 solo para cachés numéricas de fórmulas, con exponentes y cero normalizados. Texto exacto y números comunes intactos. 39 tests técnicos en verde y build sin advertencias. |
| 10 | Corregido: Down también revisa el borrador, con lock previo al DDL. 8 casos PostgreSQL y 7 de portabilidad SQL en verde; build sin advertencias. |
| 11 | Corregido: Filas/Rows, nombre CSV por idioma y errores de texto propios del pegado. 48 tests del front en verde y build sin advertencias. |
| 12 | Corregido: la vista previa distingue 200 para Una opción y 20 para Varias. 28 tests del front en verde y build sin advertencias. |
| 13 | Corregido: catálogo histórico una sola vez y cada detalle asociado a su opción; privacidad intacta. 26 tests del front en verde y build sin advertencias. |
| 14 | Corregidas solo las etiquetas pendientes del diseño y «solo» sin tilde. Siete regresiones observaron el fallo; 17 tests focales y paridad es/en en verde, build sin advertencias. |
| 15 | Corregido: encabezados accesibles en Altas, Cambios y Bajas, incluidos los valores tipados. 52 tests del front en verde y build sin advertencias. |

Fuera de alcance: «Después, sin bloquear», salvo alinear los nombres de archivos y el código de error de la spec. El piloto real con Meta espera al cierre del review.
