# WhatsApp: proveedores, workers y persistencia

Implementa Meta, firma, parsing del webhook, cola de acceso transaccional cifrada, workers, historial de entregas y persistencia del módulo. El registro reúne sus adaptadores.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Conservar IDs de botones históricos, historial sin credenciales y acceso en Manual. La migración preserva filas, relaciones, lecturas, receipts y envíos; los tests usan Meta simulado.

- Validá firma e idempotencia antes de aplicar efectos; no registrar cuerpos sensibles, números completos ni logs de HttpClient con secretos.
- `WhatsAppSendQueue` persiste códigos, invitaciones y orientación en la transacción del llamador; sólo el consumidor durable llama a Meta después del commit. El chat manual usa una salida durable separada; no repetir automáticamente Sending/Uncertain. Ambos workers comparten lease Redis por destino, detrás de RedisCache. Los scopes de reservar, llamar a Meta y completar son separados; la llamada externa no mantiene una transacción abierta. Los receipts preservan estados que llegan antes del registro del saliente.
- Las consultas EF de negocio usan lectores o repositorios propios. La persistencia se registra en `WhatsAppInfrastructureRegistration`.
- Probá reintentos, fallas, carreras y privacidad con el nivel de logs usado por los tests.

## Verificación y ejemplos

- [Pruebas técnicas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Configuración local](../../../../docs/guides/whatsapp-en-local.md).
- [Eliminación del módulo](../../../../docs/guides/quitar-whatsapp.md).
