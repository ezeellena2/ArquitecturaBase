# WhatsApp: coordinación funcional

Agrupa casos de uso, helpers, contratos, modelos, validadores, recursos y adaptadores de canales propios del módulo.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).
`WhatsAppInboundService` toma el contacto y el único límite; `WhatsAppInboundPolicy` clasifica metadata y selecciona acciones estructuradas; `WhatsAppAccessReplyPolicy` produce orientación pública sin leer identidad. `WhatsAppAccessActions` conserva IDs externos, y los textos viven en Access.resx/es/en. Perfil y registro conservan recorridos explícitos: `WhatsAppCodeIssuer` usa LoginCodeIssuer y la cuota común, con SignInCode o VerifyPhoneCode. Manual no interviene en acceso. El núcleo conserva el canje de enlaces pendientes.

## Al modificar

- ChatService coordina permisos HTTP, lectura propia y salida durable; DispatchService abre un límite separado para reservar/completar/recuperar. La llamada a Meta queda afuera de las transacciones. Las reglas están en «Chat de atención» de la fuente funcional.
- `WhatsAppPermissionCatalogSource` aporta códigos y textos del módulo al puerto genérico; el núcleo no los nombra.

- El procesamiento no crea cuentas, confirma teléfonos, vincula contactos ni emite enlaces secretos. No inferir acceso por frases. Policies y helpers no guardan.
- Los códigos usan el emisor común y una cuota propia por número; conservá propósito y límites aunque el pedido provenga de otro flujo.
- Los adaptadores de `Channels` implementan puertos del núcleo sin mover la implementación al núcleo.
- El módulo se registra en su bloque; no introducir referencias del núcleo a sus clases.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp).
- [Independencia del núcleo](../../../../docs/guides/quitar-whatsapp.md).
