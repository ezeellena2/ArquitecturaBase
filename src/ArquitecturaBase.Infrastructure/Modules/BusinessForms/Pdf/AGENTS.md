# PDF de formularios: copia fija

- Renderer técnico con SkiaSharp; sólo consume snapshots y puertos de archivos. No accede a EF, HTTP ni fuentes del sistema.
- A4 multipágina, contenido completo sin truncar; diseño, etiquetas, valores y aprobación vienen del snapshot fijado.
- Fotos pequeñas desde miniaturas privadas, verificando pertenencia; documentos sólo se nombran. Preview usa ejemplos y no lee ni guarda archivos reales.
- Public Sans Regular/Bold se incrusta desde Fonts con licencia OFL y procedencia fijada. Textos propios en Application/Modules/BusinessForms/Resources/Pdf.resx y Pdf.en.resx.
- Verificar wrap, fotos, fuentes y paginado con tests técnicos sin Docker; exportar PDFs técnicos y revisar PNG de Poppler antes de cerrar.
