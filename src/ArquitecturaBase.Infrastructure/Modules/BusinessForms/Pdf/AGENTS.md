# Pdf: copia fija de una respuesta

`SkiaFormPdfRenderer` implementa `IFormPdfRenderer`: arma con SkiaSharp el PDF A4 de una respuesta aprobada y el preview del diseño. `FormPdfTextLayout` mide, envuelve y dibuja con HarfBuzz; `FormPdfFonts` carga las fuentes incrustadas de `Fonts/`; `FormPdfTexts` lee los textos propios de `Application/Modules/BusinessForms/Resources/Pdf.resx` y `Pdf.en.resx`. Decidir cuándo emitir, subir la copia y confirmar su metadata bajo fence pertenece al motor de acciones de Application y a los archivos privados del núcleo.

## Al modificar

- El renderer recibe un `FormPdfRenderRequest` efímero: la configuración compacta sellada en el run y la respuesta inmutable releída por Id. Application entrega las referencias de catálogo ya localizadas para la cultura completa elegida.
- Sólo consume esos datos y los puertos de archivos (`IFormFileReader`, `IStoredFileReader`, `IPrivateFileStore`). No accede a EF, HTTP, catálogos actuales ni fuentes del sistema.
- Una referencia tipada muestra texto y valor capturados: Integer N0, Decimal entre 2 y 28 decimales sin double y Date sólo con números (`FormPdfDates`: `dd/MM/yyyy` en español, `MM/dd/yyyy` en inglés, nunca el mes en letras); sin valor conserva texto. La recepción y la decisión suman la hora después de una coma, como el front y el Excel: «03/11/2026, 21:05» en español y «11/03/2026, 9:05 PM» en inglés. MultiChoice lista cada elección en orden capturado con ese mismo formato; [] muestra sin respuesta.
- El bloque de renglones va como tabla bajo su nombre (`WriteTable`, con el `FormPdfTable` que arma `Table`): el número del ítem y una columna por pregunta de su versión, con línea entre columnas, los números a la derecha, el texto partido en su celda y el encabezado repetido si sigue en la página siguiente; sin ítems, «Sin respuesta». Sus preguntas nunca salen sueltas, aunque un diseño viejo las tenga.
- Una copia real exige una cultura completa del snapshot y la aprobación confirmada; si falta, lanza. El preview usa ejemplos: no lee archivos reales, no emite ni guarda.
- A4 multipágina con el contenido completo, sin truncar. Las horas van en la zona fijada en el run (`TimeZoneId`, la de la cuenta de quien cargó), con su nombre; sin ella, o si no se encuentra, en UTC.
- Las fotos se dibujan chicas desde la miniatura privada, después de comprobar que el archivo pertenece a la respuesta y es una foto; si falta la miniatura, falla. Los documentos sólo se nombran.
- Public Sans Regular y Bold es la fuente principal; los grafemas que no cubre usan Noto Emoji monocromo. Ambas son OFL y su procedencia y hash están en `Fonts/SOURCE.md`: cambiar una fuente actualiza ese archivo y su licencia.
- Medir, envolver y dibujar comparten la misma composición, para no partir modificadores, secuencias ZWJ ni banderas.
- Skia emite Noto Emoji variable como Type3 con CharProcs autocontenidos y sin BaseFont: los oráculos observan esos recursos, el bitmap y el PNG final, no el nombre de la fuente. ToUnicode puede extraer incompletos los ZWJ y las banderas; no prometas una copia Unicode exacta de esos emojis.
- Verificá wrap, fotos, fuentes y paginado con los tests técnicos sin Docker; antes de cerrar, exportá los PDF técnicos y revisá los PNG de Poppler.

## Referencias

- [Diseño y copia fija del PDF](../../../../../docs/features/formularios.md#diseño-y-copia-fija-del-pdf--etapa-4-g2) y [archivos privados](../../../../../docs/architecture/backend.md#archivos-privados).
- [Procedencia de las fuentes](Fonts/SOURCE.md).
- Pruebas: [renderer](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfRendererTests.cs), [emoji](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfEmojiTests.cs) y [retención de copias pendientes](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfRetentionIntegrationTests.cs).

E2-4: sólo campos sellados de papel data, localizados individualmente; no consulta configuración actual. Los de filter se omiten. Nada va debajo del valor (R4, 2026-11-22): cada dato es su renglón, «Pregunta · Dato» (`Fields`), con los valores de las opciones que lo tienen por coma y en su orden, y en el bloque una columna a la derecha de su pregunta (`Table`).

Ubicación (UB4-A): el punto va como texto («-34.603722, -58.381592») y debajo «Ver en el mapa», con `DrawUrlAnnotation` sobre su renglón (`WriteField` con enlace); una pregunta de ubicación nunca va en el bloque.
