# BusinessForms: validación y recursos

Leer [formularios](../../../../docs/features/formularios.md). Probar presupuestos, defaults, paths y paridad de textos sin proveedores externos.
Dobles sólo del borde; las garantías PostgreSQL/DataProtection/restore pertenecen a integración real.

- Composición de chequeo rechaza ChannelId duplicado/vacío. Dobles neutrales en TestFeatures/Channels; los que usan tipos/errores BusinessForms viven en TestFeatures/Modules/BusinessForms.

D5: nombre por cultura, vacíos secundarios/normalización, original obligatorio, ayuda opcional/parcial y completitud con paths/cultura/IDs. Duplicados sólo originales, sin nueva regla secundaria por inferencia.

BusinessFormRecordSinkTests y BusinessFormRecordSourceTests verifican autorización, schema sellado, replay/conflicto, precisión y distinción de ausencia/false con dobles de repositorio/puertos; PostgreSQL y constraints se prueban en Api.IntegrationTests.

Planilla/Excel: probar columnas por FieldId y versión, ausencia frente a opcional vacía, cero/false, operadores tipados y permisos antes/después. El export usa dobles de lector/escritor para tope y revocación; la validez física del XLSX y el SQL se verifican en integración.

FormCatalogHistoricalLanguageTests cubre oferta y referencia por idioma, fallback total sólo por textos usados y ausencia de respuestas duplicadas en nuevos snapshots. PDF verifica lectura por Id con autor/decisión fijados y referencias históricas localizadas antes del renderer.

Preview PDF verifica fotos de ejemplo sin persistencia. La completitud incluye el nombre del formulario es/en y usa el NameJson capturado para PDF y avisos aunque el borrador actual esté completo; nunca mezcla el idioma secundario incompleto.

La presencia tiene expectativas explícitas para pregunta ausente, opcional vacía y respondida, tanto escalar como archivo: answered false/false/true, unanswered true/true/false y notAsked true/false/false; cero y false son respuestas presentes.

FormExportServiceTests verifica el presupuesto de 30 segundos en lectura/escritura, su renovación por pedido y la precedencia de cancelación del caller, incluso si vence el presupuesto al mismo tiempo. Un resultado tardío se descarta; no se afirma que ClosedXML síncrono pueda interrumpirse a los 30 segundos.

FormOfferWhenTests verifica normalización, límite, controles/persistibilidad, ausencia de traducción y aislamiento del schema/huella de publicación. La serialización de instrucciones deshabilitadas conserva los bytes del request anterior para el replay de comprobantes existentes.
