# Users: administración y perfil

Coordina listados, alta, acceso, roles, invitaciones y perfil propio. Guards y linkers concentran reglas compartidas de cuentas y medios de ingreso.

## Lectura obligatoria

Antes de tocar esto, leé [`docs/features/administracion.md`](../../../../docs/features/administracion.md).
El perfil sigue además [`docs/features/identidad.md`](../../../../docs/features/identidad.md) (los códigos por destino y propósito: el del correo lo pide `DestinationCodeIssuer`, el de un número lo pide el módulo del canal, y los dos los verifica `DestinationCodeVerifier`, en `Services/Auth`). Desvincular el número desde el perfil es de `ProfilePhoneService` (`IProfilePhoneService`), del núcleo: exige otro medio de ingreso, conserva las sesiones y anula los enlaces pendientes, con el módulo prendido, apagado o quitado. Desde el perfil, cada medio verificado confirma los cambios del otro (N11 y decisión del 2026-11-16, identidad.md), con un código de `LoginCodePurpose.ConfirmChange`: agregar un correo con número verificado pide también el código mandado a ese número (`ProfileEmailLinker`), vincular un número con correo verificado pide el mandado a ese correo (`PhoneNumberLinker.ConfirmOwnPhoneAsync`), y desvincularlo también (`ProfilePhoneService`); el del correo lo manda `ProfileService.RequestEmailConfirmationCodeAsync`. Un correo o un número verificados no se reemplazan desde el perfil. Los dos códigos se revisan antes de gastar alguno, y los locks van en el orden único de `UserContactLinker.LockAsync`: correo, número, participantes y cuenta. La administración no pide esos códigos. El número de una cuenta lo vincula, lo cambia o lo suelta `PhoneNumberLinker`, que avisa a los participantes del número (`IPhoneLinkParticipant`; administracion.md); con WhatsApp, su participante suelta o vincula el contacto del chat ([`docs/features/whatsapp.md`](../../../../docs/features/whatsapp.md)). Las invitaciones de un administrador las arma `UserInvitationIssuer` y las encola el canal (`IInvitationChannel`): el correo, en `Application/Channels`, y WhatsApp, en `Modules/WhatsApp/Channels` (administracion.md, "Las invitaciones salen por canales").

## Al modificar

- La invitación WhatsApp conserva consentimiento y WANT_TO_ENTER (con su idioma, `WANT_TO_ENTER:es` o `:en`); orienta al login público y después al código web. Los mensajes comunes no crean cuentas ni vinculan/confirman teléfonos.

- Conservá último administrador y último medio de ingreso; las carreras necesitan lock antes de leer la cuenta.
- Nadie da o quita un rol con permisos que no tiene ni actúa sobre una cuenta con permisos que no tiene (`UserGuard` con `PermissionDelegationGuard`, de Roles), y la cuenta activa de `Seed:AdminEmail` no pierde Admin ni el correo, ni desde la administración ni desde el perfil. Restaurar por el número la cuenta borrada que tenía ese correo la deja sin él, a propósito (N4).
- Alta y edición tienen efectos distintos: cargar un destino no equivale a verificarlo. El perfil propio reemplaza los campos previstos del contrato.
- El alta que restaura una cuenta borrada la deja sólo con el correo y el número que trajo (lo distinto se reemplaza), sin verificar, sin logins externos, con las preferencias de una cuenta nueva y con todo acceso cortado otra vez (N4, `UserContactLinker.RestoreAsync`). La respuesta dice que se restauró. Restaurando por el correo, entra quien controla el buzón, con el código: Google no se vincula a un correo sin confirmar (decisión del 2026-11-15, `ExternalLoginService`; administracion.md e identidad.md).
- Los participantes del número se avisan desde `PhoneNumberLinker` en el límite existente, sin guardar por su cuenta.
- Roles, sesiones y datos del perfil invalidan o revocan solo los efectos definidos por el caso; probá también invitaciones y fallas de entrega.

## Verificación y ejemplos

- [Pruebas de casos de uso](../../../../tests/ArquitecturaBase.Application.UnitTests/Services/Users).
- [Rutas, perfil y carreras](../../../../tests/ArquitecturaBase.Api.IntegrationTests/Users).
