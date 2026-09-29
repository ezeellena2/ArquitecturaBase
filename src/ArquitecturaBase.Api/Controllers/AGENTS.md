Antes de tocar un controller, leé el [borde HTTP](../../../docs/architecture/backend.md#borde-http) y el documento de su área:
- `LoginCode`, `LoginLink`, `LoginMethods`, `ExternalLogin` y `Connect` (`/account` y `/connect`): [`docs/features/identidad.md`](../../../docs/features/identidad.md).
- `Me` (el perfil): [`identidad.md`](../../../docs/features/identidad.md) para el correo y sus códigos, y [`whatsapp.md`](../../../docs/features/whatsapp.md) para el número.
- El webhook de WhatsApp está en `Api/Modules/WhatsApp/Controllers`, con lo demás del módulo: [`docs/features/whatsapp.md`](../../../docs/features/whatsapp.md).
- `Users`, `Roles`, `Settings` y `Permissions`: [`docs/features/administracion.md`](../../../docs/features/administracion.md); el número de una cuenta sigue además `whatsapp.md`.
