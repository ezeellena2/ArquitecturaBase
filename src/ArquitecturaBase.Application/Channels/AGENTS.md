# Channels: adaptadores que trae el núcleo

Implementa el canal de invitaciones por correo y la versión deshabilitada del canal de teléfono. Son implementaciones de puertos; las de un módulo viven con él.

SystemQueries aporta usuarios agregados, roles y ayuda curada versionada; son fuentes neutrales autorizadas, sin escrituras ni llamadas LLM. Consultar lectores materializados, no una página parcial como total. Las fuentes de módulos se agregan con TryAddEnumerable.

## Al modificar

- `PermissionCatalog` compone el catálogo estático y fuentes opcionales; no conoce nombres de módulos. Sus textos siguen la cultura actual y falla por códigos duplicados. [ADR 0010](../../../docs/decisions/0010-catalogo-de-permisos-extensible.md).

- El canal verifica sus precondiciones y encola según el contrato. No abre una transacción ni guarda por su cuenta.
- El correo se registra como un canal entre varios; una implementación deshabilitada conserva el comportamiento del núcleo cuando no hay proveedor.
- Una falla al encolar marca el estado previsto de la invitación; no registra destinos ni enlaces sensibles.

## Referencias

- [Registro de canales opcionales](../../../docs/architecture/backend.md).
- [Invitaciones y estado de envío](../../../docs/features/administracion.md).
- [Contrato de entrega](../Interfaces/Channels/IInvitationChannel.cs).

DisabledStructuredFormChannelStateSource devuelve evidencia vacía cuando no hay canal instalado y respeta cancelación; no fabrica estados de deprecación. El núcleo registra el fallback con TryAddSingleton y el canal instalado lo reemplaza.

DisabledStructuredFormRetirementSource conserva vacío el conjunto de versiones confirmadas como no usadas cuando falta el dueño. Su lock no tiene efectos y ambas operaciones respetan cancelación; no deduce retiro por ausencia del módulo.

DisabledAssistantOperationCompletionSignal conserva el cierre del motor cuando falta un consumidor opcional: Notify es inmediato y no tiene efectos. El canal instalado reemplaza ese singleton con su adaptador; el núcleo nunca interpreta identificadores de módulos.
