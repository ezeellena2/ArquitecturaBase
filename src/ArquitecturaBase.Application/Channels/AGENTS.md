# Channels: adaptadores que trae el núcleo

Implementa el canal de invitaciones por correo y la versión deshabilitada del canal de teléfono. Son implementaciones de puertos; las de un módulo viven con él.

## Al modificar

- El canal verifica sus precondiciones y encola según el contrato. No abre una transacción ni guarda por su cuenta.
- El correo se registra como un canal entre varios; una implementación deshabilitada conserva el comportamiento del núcleo cuando no hay proveedor.
- Una falla al encolar marca el estado previsto de la invitación; no registra destinos ni enlaces sensibles.

## Referencias

- [Registro de canales opcionales](../../../docs/architecture/backend.md).
- [Invitaciones y estado de envío](../../../docs/features/administracion.md).
- [Contrato de entrega](../Interfaces/Channels/IInvitationChannel.cs).
