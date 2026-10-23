# Pdf: copia fija de una respuesta

`SkiaFormPdfRenderer` implementa `IFormPdfRenderer`: arma con SkiaSharp el PDF A4 de una respuesta aprobada y el preview del diseño. `FormPdfTextLayout` mide, envuelve y dibuja con HarfBuzz; `FormPdfFonts` carga las fuentes incrustadas de `Fonts/`; `FormPdfTexts` lee los textos propios de `Application/Modules/BusinessForms/Resources/Pdf.resx` y `Pdf.en.resx`. Decidir cuándo emitir, subir la copia y confirmar su metadata bajo fence pertenece al motor de acciones de Application y a los archivos privados del núcleo.

## Al modificar

- El renderer recibe un `FormPdfRenderRequest` efímero: la configuración compacta sellada en el run y la respuesta inmutable releída por Id. Application entrega las referencias de catálogo ya localizadas para la cultura completa elegida.
- Sólo consume esos datos y los puertos de archivos (`IFormFileReader`, `IStoredFileReader`, `IPrivateFileStore`). No accede a EF, HTTP, catálogos actuales ni fuentes del sistema.
- Una referencia tipada muestra texto y valor capturados: Integer N0, Decimal entre 2 y 28 decimales sin double y Date corto es-AR/en-US; sin valor conserva texto. MultiChoice lista cada elección en orden capturado con ese mismo formato; [] muestra sin respuesta.
- Una copia real exige una cultura completa del snapshot y la aprobación confirmada; si falta, lanza. El preview usa ejemplos: no lee archivos reales, no emite ni guarda.
- A4 multipágina con el contenido completo, sin truncar. Diseño, decisión y nombre del sistema vienen fijados en el run; etiquetas y valores, de la captura inmutable. Las fechas se muestran en UTC.
- Las fotos se dibujan chicas desde la miniatura privada, después de comprobar que el archivo pertenece a la respuesta y es una foto; si falta la miniatura, falla. Los documentos sólo se nombran.
- Public Sans Regular y Bold es la fuente principal; los grafemas que no cubre usan Noto Emoji monocromo. Ambas son OFL y su procedencia y hash están en `Fonts/SOURCE.md`: cambiar una fuente actualiza ese archivo y su licencia.
- Medir, envolver y dibujar comparten la misma composición, para no partir modificadores, secuencias ZWJ ni banderas.
- Skia emite Noto Emoji variable como Type3 con CharProcs autocontenidos y sin BaseFont: los oráculos observan esos recursos, el bitmap y el PNG final, no el nombre de la fuente. ToUnicode puede extraer incompletos los ZWJ y las banderas; no prometas una copia Unicode exacta de esos emojis.
- Verificá wrap, fotos, fuentes y paginado con los tests técnicos sin Docker; antes de cerrar, exportá los PDF técnicos y revisá los PNG de Poppler.

## Referencias

- [Diseño y copia fija del PDF](../../../../../docs/features/formularios.md#diseño-y-copia-fija-del-pdf--etapa-4-g2) y [archivos privados](../../../../../docs/architecture/backend.md#archivos-privados).
- [Procedencia de las fuentes](Fonts/SOURCE.md).
- Pruebas: [renderer](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfRendererTests.cs), [emoji](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfEmojiTests.cs) y [retención de copias pendientes](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormPdfRetentionIntegrationTests.cs).

E2-4: sólo campos sellados de papel data, localizados individualmente y agrupados por opción; no consulta configuración actual. Los de filter se omiten.
