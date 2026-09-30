# Users: administración y perfil

Coordina listados, alta, acceso, roles, invitaciones y perfil propio. Guards y linkers concentran reglas compartidas de cuentas y medios de ingreso.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).
El perfil sigue además [`docs/features/identidad.md`](../../../../docs/features/identidad.md) (los códigos por destino y propósito: el del correo lo pide `DestinationCodeIssuer`, el de un número lo pide el módulo del canal, y los dos los verifica `DestinationCodeVerifier`, en `Services/Auth`). Desvincular el número desde el perfil es de `ProfilePhoneService` (`IProfilePhoneService`), del núcleo: exige otro medio de ingreso, conserva las sesiones y anula los enlaces pendientes, con el módulo prendido, apagado o quitado. El número de una cuenta lo vincula, lo cambia o lo suelta `PhoneNumberLinker`, que avisa a los participantes del número (`IPhoneLinkParticipant`; administracion.md); con WhatsApp, su participante suelta o vincula el contacto del chat ([`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md)). Las invitaciones de un administrador las arma `UserInvitationIssuer` y las encola el canal (`IInvitationChannel`): el correo, en `Application/Channels`, y WhatsApp, en `Modules/WhatsApp/Channels` (administracion.md, "Las invitaciones salen por canales").

## Al modificar

- Conservá último administrador y último medio de ingreso; las carreras necesitan lock antes de leer la cuenta.
- Alta y edición tienen efectos distintos: cargar un destino no equivale a verificarlo. El perfil propio reemplaza los campos previstos del contrato.
- Los participantes del número se avisan desde `PhoneNumberLinker` en el límite existente, sin guardar por su cuenta.
- Roles, sesiones y datos del perfil invalidan o revocan solo los efectos definidos por el caso; probá también invitaciones y fallas de entrega.

## Verificación y ejemplos

- [Pruebas de casos de uso](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Users).
- [Rutas, perfil y carreras](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Users).
