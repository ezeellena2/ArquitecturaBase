# PDF de formularios: copia fija

- Renderer técnico con SkiaSharp; sólo consume datos de render y puertos de archivos. No accede a EF, HTTP ni fuentes del sistema.
- A4 multipágina, contenido completo sin truncar; diseño y aprobación se fijan en el run; etiquetas y valores vienen de la captura inmutable.
- Fotos pequeñas desde miniaturas privadas, verificando pertenencia; documentos sólo se nombran. Preview usa ejemplos y no lee ni guarda archivos reales.
- Public Sans Regular/Bold y Noto Emoji se incrustan desde Fonts con licencia OFL y procedencia fijada. Public Sans sigue como fuente principal; los grafemas no cubiertos usan Noto Emoji monocromo con HarfBuzz, con la misma composición para medir, envolver y dibujar. Textos propios en Application/Modules/BusinessForms/Resources/Pdf.resx y Pdf.en.resx.
- Verificar wrap, fotos, fuentes y paginado con tests técnicos sin Docker; exportar PDFs técnicos y revisar PNG de Poppler antes de cerrar.

El renderer recibe FormPdfRenderRequest efímero: configuración compacta sellada y respuesta inmutable releída por Id. Application entrega las referencias ya localizadas para la cultura completa elegida; el renderer no consulta catálogos actuales.

Skia puede emitir Noto Emoji variable como Type3 con CharProcs autocontenidos, sin nombre BaseFont. El oráculo PDF observa esos recursos junto con bitmap y PNG final, no el nombre literal de la fuente. ToUnicode de Skia puede extraer de forma incompleta ZWJ/banderas; no prometer una copia Unicode exacta de esos emojis.
