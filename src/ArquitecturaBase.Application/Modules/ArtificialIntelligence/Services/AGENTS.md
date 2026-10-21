# Services: coordinación del motor y configuración de IA

Contiene los casos de uso y sus políticas de conexión, negocio y asistente. Hereda las reglas del [módulo](../AGENTS.md) y la fuente de [inteligencia artificial](../../../../../docs/features/inteligencia-artificial.md). Channels adapta operaciones neutrales; Infrastructure coordina el proveedor y sus scopes.

## Al modificar

- Dispatch conserva un único límite por escritura y verifica el fence, la vigencia y el acceso antes de publicar. Complete cierra texto, propuestas y NormalizeAudio por el mismo camino; Fail conserva los fallos conocidos o inciertos. Claim, BeginProviderStep y RecordProgress también pueden cerrar por vigencia, cancelación o acceso y aplican el mismo aviso al quedar terminales.
- El aviso neutral de cierre sale después de confirmar la UoW. Reenvía el ChannelId duradero sin interpretarlo y sin contenido. Un rollback o un Result fallido no avisa. Un cierre conocido puede reiterar el aviso coalescente. No cambia aceptación, cuotas, leases, herramientas ni efectos externos.
- El aviso es una optimización en memoria: la recuperación periódica sigue siendo la autoridad del trabajo pendiente. No agregar una cola, persistencia o bus para el aviso.

## Referencias

- [Pruebas de cierre y orden postcommit](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/ArtificialIntelligence/AiAssistantDispatchServiceTests.CompletionSignal.cs).
- [Normalización y aviso del mismo motor](../../../../../tests/ArquitecturaBase.Application.UnitTests/Modules/ArtificialIntelligence/AiAssistantAudioCompletionSignalTests.cs).
- [Robustez proporcional](../../../../../docs/decisions/0018-robustez-proporcional-al-efecto.md).

La reserva activa de interpretación compara OwnerUserId, ChannelId y ChannelConversationId nullable. Replay por UUID se resuelve antes de unicidad/cuotas; Get y Cancel comparan el ámbito sellado. Las cuotas y recibos conservan actor/canal/propósito, sin repartirlas por conversación. NormalizeAudio conserva su exclusión de la reserva.
