# WhatsApp: proveedores, workers y persistencia

Implementa Meta, firma, parsing del webhook, colas, workers, outbox y persistencia del módulo. El registro reúne sus adaptadores.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Validá firma e idempotencia antes de aplicar efectos; no registrar cuerpos sensibles, números completos ni logs de HttpClient con secretos.
- La outbox es persistida; los workers resuelven un scope por operación y conservan coordinación entre réplicas.
- Las consultas EF de negocio usan lectores o repositorios propios. La persistencia se registra en `WhatsAppInfrastructureRegistration`.
- Probá reintentos, fallas, carreras y privacidad con el nivel de logs usado por los tests.

## Verificación y ejemplos

- [Pruebas técnicas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Configuración local](../../../../docs/guides/whatsapp-en-local.md).
- [Eliminación del módulo](../../../../docs/guides/quitar-whatsapp.md).
