# Auth: casos de uso de acceso

Coordina ingreso, registro explícito, acceso externo, códigos y enlaces. Los helpers reparten política de cuenta, emisión, verificación y auditoría.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/identidad.md`](../../../../docs/features/identidad.md).
El enlace de un solo uso (`LoginLinkIssuer`, `LoginLinkVerifier`, `LoginLinkService`) es del núcleo y su regla está en `identidad.md`. Acá se emite y se verifica cualquier código (`LoginCodeIssuer`, `LoginCodeVerifier`, `DestinationCodeVerifier`), pero solo se piden los del correo: los pedidos por un número y su tope diario son del módulo (`Modules/WhatsApp/Services/WhatsAppCodeIssuer`) y siguen además [`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md).

## Al modificar

- Conservá la intención de ingreso/registro y comprobá la política vigente al verificar, no solo al ofrecer un método.
- Consumos, intentos y auditoría que deben persistir incluso al fallar requieren `OnAnyResult`; emisión y verificación se serializan con sus locks.
- Los helpers no abren límites. Los efectos de sesión que exigen datos confirmados van después del commit.
- Antes de reutilizar un emisor/verificador, revisá propósito, destino, plazo, reenvío y anonimato de la respuesta.

## Verificación y ejemplos

- [Pruebas de coordinación](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Auth).
- [Flujos HTTP y seguridad](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Auth).
