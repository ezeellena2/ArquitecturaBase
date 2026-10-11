# BusinessForms: validación y recursos

Leer [formularios](../../../../docs/features/formularios.md). Probar presupuestos, defaults, paths y paridad de textos sin proveedores externos.
Dobles sólo del borde; las garantías PostgreSQL/DataProtection/restore pertenecen a integración real.

- Composición de chequeo rechaza ChannelId duplicado/vacío. Dobles exclusivos en TestFeatures/Channels.

D5: nombre por cultura, vacíos secundarios/normalización, original obligatorio, ayuda opcional/parcial y completitud con paths/cultura/IDs. Duplicados sólo originales, sin nueva regla secundaria por inferencia.

BusinessFormRecordSinkTests y BusinessFormRecordSourceTests verifican autorización, schema sellado, replay/conflicto, precisión y distinción de ausencia/false con dobles de repositorio/puertos; PostgreSQL y constraints se prueban en Api.IntegrationTests.
