# WhatsApp: tests de Api.IntegrationTests

Prueba webhook, envío y registro de entregas, clasificación/acceso explícito, cuota, vinculación, rutas y convivencia con el núcleo sobre PostgreSQL y Redis. La cola de acceso sigue en memoria. El chat manual acredita salida durable, revisión de resultados inciertos, lectura por operador, receipts tempranos, migración con mensajes previos y leases entre hosts; Meta está simulado.

## Al modificar

- `WhatsAppTestModule` aporta settings, servicios e inventario al arnés común. ApiFactory conserva estado propio, pero no implementa un partial void compartido con otros módulos (ADR 0013).

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Probá firma, idempotencia, concurrencia y privacidad del webhook con el arnés existente.
- Las rutas e inventarios del módulo se amplían en sus partes; desvincular el número propio sigue siendo del núcleo.
- Restaurá configuración de proveedores y verificá los escenarios con el módulo apagado cuando corresponda.
- Los tests de Flows usan `IStructuredFormChannel` y `IStructuredFormSource` neutrales con snapshots/fuentes ficticios; no importan BusinessForms. Sus dobles están en `TestFeatures/Modules/WhatsApp` y se retiran con el módulo.
- Verificá la pertenencia WABA/PhoneNumberId antes de persistir el inicio de Create/Upload/Publish; un rechazo previo puede reintentar el mismo paso, una cancelación luego de invocar sólo reconcilia.
- La captura local recorre webhook firmado, protección y lectura autorizada con puertos neutrales. BusinessForms prueba por separado sink/source reales con PostgreSQL; ninguno de estos tests acredita round-trip con Meta.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).
