# WhatsApp: coordinación funcional

Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
En `Services`, el bot es `WhatsAppInboundService` (el lock del contacto y el único límite), `WhatsAppReplyPolicy` (la cuenta y la respuesta) y `WhatsAppLinkIssuer` (el enlace y el alta desde el chat): el reparto está en `whatsapp.md`, en "El bot no tiene estado de conversación". `ProfileWhatsAppService` pide el código y confirma el número; la desvinculación propia vive en `Services/Users/ProfilePhoneService`, del núcleo. Los pedidos de un código por número (`WhatsAppCodeIssuer`, con el tope diario de `WhatsAppCodeQuotaGuard`) también viven ahí: los emite el `LoginCodeIssuer` del núcleo.

## Al modificar

- El bot coordina un lock de contacto y un único límite; sus policies e issuers no guardan.
- Los códigos usan el emisor común y una cuota propia por número; conservá propósito y límites aunque el pedido provenga de otro flujo.
- Los adaptadores de `Channels` implementan puertos del núcleo sin mover la implementación al núcleo.
- El módulo se registra en su bloque; no introducir referencias del núcleo a sus clases.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).
