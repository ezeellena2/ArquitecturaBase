# BusinessForms: validación y recursos

Leer [formularios](../../../../docs/features/formularios.md). Probar presupuestos, defaults, paths y paridad de textos sin proveedores externos.
Dobles sólo del borde; las garantías PostgreSQL/DataProtection/restore pertenecen a integración real.

- Composición de chequeo rechaza ChannelId duplicado/vacío. Dobles neutrales en TestFeatures/Channels; los que usan tipos/errores BusinessForms viven en TestFeatures/Modules/BusinessForms.

D5: nombre por cultura, vacíos secundarios/normalización, original obligatorio, ayuda opcional/parcial y completitud con paths/cultura/IDs. Duplicados sólo originales, sin nueva regla secundaria por inferencia.

BusinessFormRecordSinkTests y BusinessFormRecordSourceTests verifican autorización, schema sellado, replay/conflicto, precisión y distinción de ausencia/false con dobles de repositorio/puertos; PostgreSQL y constraints se prueban en Api.IntegrationTests.

Planilla/Excel: probar columnas por FieldId y versión, ausencia frente a opcional vacía, cero/false, operadores tipados y permisos antes/después. El export usa dobles de lector/escritor para tope y revocación; la validez física del XLSX y el SQL se verifican en integración.
