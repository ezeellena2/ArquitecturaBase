# Channels: adaptadores de WhatsApp para el núcleo

Implementa puertos neutrales de `Application/Interfaces/Channels` con lo que sabe el módulo. Cada adaptador traduce y delega en servicios o lectores de WhatsApp; la regla vive en `Services` y el núcleo solo ve el puerto. Se registran en `WhatsAppApplicationRegistration`.

| Adaptador | Puerto | Aporta |
|---|---|---|
| `WhatsAppPhoneChannel` | `IPhoneChannel` (`Replace`) | países y número del bot con WhatsApp prendido; el país de un número nuevo se valida siempre |
| `WhatsAppPhoneLinkParticipant` | `IPhoneLinkParticipant` | locks, vínculo y desvínculo del contacto por `WhatsAppContactLinker` |
| `WhatsAppInvitationChannel` | `IInvitationChannel` | chequea disponibilidad, número, consentimiento y nombre, en ese orden; encola la plantilla o marca la invitación no enviada |
| `WhatsAppInvitationDeliveryStatusSource` | `IInvitationDeliveryStatusSource` | último estado de Meta del saliente; Pending sin id |
| `WhatsAppPermissionCatalogSource` | `IPermissionCatalogSource` | códigos y textos del módulo |
| `WhatsAppStructuredFormChannel` | `IStructuredFormChannel` (`phone`) | chequeo local; Accept, Status y Preview por los servicios de Flows |
| `WhatsAppStructuredRecordActionChannel` | `IStructuredRecordActionChannel` (`phone`) | avisos de reglas por `IWhatsAppFormActionService` |
| `WhatsAppStructuredRecordOriginSource` | `IStructuredRecordOriginSource` | canal, remitente e instante de respuestas, por lote |
| `WhatsAppAssistantCapabilitySource` | `IAssistantCapabilitySource` | `whatsapp.offered-forms`, `whatsapp.no-reply` y `whatsapp.decline-form` |
| `WhatsAppAudioRetentionSource` | `IPrivateFileRetentionSource` | claves de audio todavía referenciadas |

La evidencia local de retiro (`IStructuredFormChannelStateSource`) la implementa un lector de Infrastructure.

## Al modificar

- Un adaptador no abre UoW ni guarda: encola o lee en el límite de quien llama, o delega en un servicio con el suyo. La implementación no se mueve al núcleo.
- Sin tipos ni errores de BusinessForms o IA. El canal de formularios solo traduce `UnsupportedConfiguration` y `PayloadMismatch` de su propio Accept a `Failed`, sin reconciliar; no clasifiques errores ajenos por su código.
- `CheckAsync` es puro: sin transporte, cuenta ni publicación. Funciona apagado y no acredita la cuenta ni habilita aceptar.
- El origen entrega `ConversationId` solo si el actor tiene `whatsapp.read` antes y después de leer; enriquece respuestas ya autorizadas y no envía nada.
- Capacidades: JSON estricto; hasta 100 `definitionIds` sin repetir para leer rechazos duraderos, aparte de hasta 20 ofertas recientes que caben en 16 KiB con nombres completos de hasta 120 caracteres. Los rechazos no conceden elegibilidad ni prueban ausencia. Un rechazo solo cita una oferta leída en el mismo turno.
- El esquema de entrada sigue el subconjunto estricto de OpenAI (sin `uniqueItems`) y el adaptador rechaza duplicados; la salida sí puede declararlo. Sin sanitizadores genéricos ni validación relajada.
- La retención de audio también protege la copia recuperable escrita antes del commit (clave determinista por mensaje).

## Referencias

- [Puertos del núcleo](../../../Interfaces/Channels/AGENTS.md) y [versiones apagadas](../../../Channels/AGENTS.md).
- [Pruebas de adaptadores](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/Channels) y [del canal de formularios](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/WhatsApp/WhatsAppStructuredFormChannelTests.cs).
- [Catálogo de permisos extensible](../../../../../docs/decisions/0010-catalogo-de-permisos-extensible.md) y [puertos de módulos](../../../../../docs/architecture/backend.md#módulos-opcionales).

WhatsAppAssistantOperationCompletionSignal reemplaza el aviso neutral de cierre del motor. Sólo whatsapp-assistant y whatsapp-manual despiertan IWhatsAppInboundSignal; otros identificadores no tienen efecto. No lee contenido, no guarda ni crea trabajo durable.
