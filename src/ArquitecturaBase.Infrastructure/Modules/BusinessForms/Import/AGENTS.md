# Import: lectura acotada de planillas

Implementa `IFormSpreadsheetReader` con ClosedXML y un lector CSV propio. Lee bytes privados en memoria; el caso de uso mantiene permisos, revisión y la decisión de aplicar. No abre transacciones ni guarda archivos o resultados.

## Al modificar

- El caller conserva el stream. Leer desde su posición actual, con cancelación y tope de 10 MiB, incluso sin Length/Seek; SHA-256 hexadecimal minúscula sobre esos bytes originales, sin normalizar el archivo.
- Sólo XLSX y CSV UTF-8 estricto, con BOM opcional. CSV detecta coma, punto y coma o tabulación en el primer registro; conserva campos entrecomillados, comillas escapadas y saltos internos. Tabulación sigue usando extensión `.csv`.
- El presupuesto técnico es 10.002 registros con contenido: encabezado, posible leyenda de plantilla y hasta 10.000 datos. Los números de fila no son cantidades: una hoja dispersa conserva sus direcciones hasta el límite real de Excel. CSV omite registros de datos íntegramente vacíos, sin gastar el presupuesto ni renumerar los posteriores. Un encabezado vacío en la primera fila se conserva para no adoptar datos como encabezado. La Policy reconoce la leyenda y controla el límite exacto de datos. CSV conserva números lógicos de registro; XLSX, números de fila originales. Hasta 64 columnas con contenido; el formato vacío no amplía la grilla.
- Antes de ClosedXML, el preflight del ZIP comprueba tanto tamaños declarados como bytes efectivamente entregados por ZipArchive, hasta 100 MiB. En .NET 10 una longitud falsa menor trunca la entrega a esa longitud; no se interpreta el directorio central de nuevo ni se infla contenido oculto. Validar también la sintaxis de sharedStrings antes de ClosedXML para devolver FileUnreadable ante XML truncado, sin ocultar bugs con un catch general. Valida filas/columnas de todas las hojas que ClosedXML va a cargar, aunque sólo devuelve la primera. XML sin DTD ni resolución externa; ninguna entrada se extrae al disco.
- Tope físico global: 10.002 elementos `row` y 640.128 unidades de trabajo (10.002 × 64). Incluye celdas XML vacías/con estilo y expansión de fórmulas matriciales, menos el ancla ya contada; un array denso gasta ambos trabajos. Seguir cada `workbook.sheet`, aunque comparta `r:id` o target: ClosedXML vuelve a cargarlo. Las direcciones dispersas no gastan su número ni amplían la grilla devuelta. Las pruebas de rangos enormes invocan sólo el preflight, sin ClosedXML ni DOM de la grilla. Un tope devuelve metadata `sheet` con el nombre real, leído antes de ClosedXML: el borde HTTP la conserva para mostrar qué hoja lo incumple.
- Abrir con `RecalculateAllFormulas=false` y leer exclusivamente `CachedValue`: nunca `Value`, `GetFormattedString`, cálculo ni macros. Una fórmula sin valor guardado produce `FormulaValueMissing`; un error de celda o tipo no representable produce `CellUnreadable`, sin copiar su contenido al error.
- Convertir escalares a strings independientes de cultura, sin formato visual: números sin notación exponencial ni coerción localizada, booleanos true/false y fechas ISO. Una fecha con hora conserva la hora para que la Policy no la trunque silenciosamente. El texto que empieza con `=` sigue siendo literal.
- El lector es stateless, singleton y no agrega caché, archivos temporales, sesiones ni trabajo durable. No loguear nombres, hashes o valores. Las fallas de formato usan errores seguros de Domain; la cancelación se propaga.

## Referencias

- [Regla funcional de formularios](../../../../../docs/features/formularios.md).
- [Pruebas físicas sin ApiFactory](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/BusinessForms/FormSpreadsheetReaderTests.cs).
- [Fixture técnico](../../../../../tests/ArquitecturaBase.Api.IntegrationTests/TestFeatures/Modules/BusinessForms/FormSpreadsheetFixture.cs).
