# WhatsApp: borde HTTP del módulo

Aloja controllers, contratos y convenciones de rutas de webhook, códigos y perfil del canal.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`MeWhatsAppController` pide el código y confirma el número; `DELETE /api/me/whatsapp` vive en el `MeController` del núcleo.

## Al modificar

- Historial/preview agregan propósito y evento; Access no expone cuerpos sensibles. Bot/Manual y rutas se conservan; los códigos pedidos en la web no dependen del modo.

- `WhatsAppChatsController` expone siete rutas de bandeja, historial, lectura, modo, respuesta, consulta propia y revisión. Requiere read y, en escritura correspondiente, send/manage. 202 confirma cola durable, no entrega. Las rutas desaparecen al apagar mensajería.

- El webhook conserva contrato y autenticación por firma; las rutas de perfil usan sesión y las reglas del área.
- Los contratos sensibles sobrescriben `ToString()`. El rate limit y los errores mantienen las garantías del borde.
- Las rutas del módulo se inventarían y prueban en sus partes; no introducirlas en el registro o inventario del núcleo.

## Verificación y ejemplos

- [Pruebas de rutas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Borde y módulos opcionales](../../../../docs/architecture/backend.md).
