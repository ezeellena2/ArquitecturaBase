# WhatsApp: borde HTTP del módulo

Aloja controllers, contratos y convenciones de rutas de webhook, códigos y perfil del canal.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`MeWhatsAppController` pide el código y confirma el número; `DELETE /api/me/whatsapp` vive en el `MeController` del núcleo.

## Al modificar

- Historial/preview agregan propósito y evento; Access no expone cuerpos sensibles. Assistant/Manual conservan sus rutas; los códigos pedidos en la web no dependen del modo.

- `WhatsAppChatsController` expone las seis rutas de bandeja, historial, lectura, respuesta, consulta propia y revisión, con read y send al escribir. `WhatsAppChatModeController` conserva el PUT de modo y pide sesión; su servicio exige Send para Manual o ConfigureAssistant para Assistant, sin agregar lectura ni el permiso del otro destino. El resultado200 informa modo/revisión y el hecho observado de un envío del asistente ya iniciado. No hay una política OR genérica. 202 en enviar confirma cola durable, no entrega. Las rutas desaparecen al apagar mensajería.

- El webhook conserva contrato y autenticación por firma; las rutas de perfil usan sesión y las reglas del área.
- Los contratos sensibles sobrescriben `ToString()`. El rate limit y los errores mantienen las garantías del borde.
- Las rutas del módulo se inventarían y prueban en sus partes; no introducirlas en el registro o inventario del núcleo.

## Verificación y ejemplos

- [Pruebas de rutas](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Modules/WhatsApp).
- [Borde y módulos opcionales](../../../../docs/architecture/backend.md).

H6: WhatsAppAudioController expone POST messages/{messageId}/audio-transcription sin body y GET messages/{messageId}/audio privado. Ambos exigen Read, sin Send ni Home; POST202 fija un único pedido por mensaje y GET sólo lee. La reproducción devuelve bytes verificados Ogg, no-store/nosniff y Range, sin claves o URLs externas. El DTO Audio sólo agrega estado y CanPlay al mensaje; Body aparece únicamente al completar y mientras siga vigente.

Las rutas assistant-suggestions requieren Read+Send: POST solicita un análisis explícito e idempotente; GET sólo lee; descartar y confirmar comparten el recibo durable. Confirmar recibe UUID e IDs de campos, nunca valores, identidad de actor, motivo ni un payload del modelo. Otro operador autorizado puede recuperar el primer recibo confirmado.
