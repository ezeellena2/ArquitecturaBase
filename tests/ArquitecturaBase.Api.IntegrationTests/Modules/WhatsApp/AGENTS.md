# WhatsApp: tests de Api.IntegrationTests

Prueba webhook, outbox, bot, cuota, vinculación, rutas y convivencia con el núcleo sobre PostgreSQL y Redis.

## Al modificar

- Probá firma, idempotencia, concurrencia y privacidad del webhook con el arnés existente.
- Las rutas e inventarios del módulo se amplían en sus partes; desvincular el número propio sigue siendo del núcleo.
- Restaurá configuración de proveedores y verificá los escenarios con el módulo apagado cuando corresponda.

## Referencias

- [Reglas funcionales del módulo](../../../../docs/features/whatsapp.md).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).
