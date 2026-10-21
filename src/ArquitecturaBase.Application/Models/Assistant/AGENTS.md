# Assistant: contratos neutrales del motor

Contexto de servidor con Actor/Subject explícitos, requisitos separados sin herencia, capacidades Read/Proposal versionadas y codec/payload tipado del dueño. Los requisitos efectivos se sellan y se revalidan para cada identidad; el modelo no elige identidad, permisos, cultura, SQL o URL. Sin efectos/UoW. No referencia módulos opcionales. Todo contenido privado redacta ToString.

Operations lleva el vencimiento del contenido original y devuelve procedencia readonly reconstruida del sello (perfil/instrucciones/versiones/instante original). El motor acota contenido a min(retenciónIA, vencimiento del dueño); recibos metadata conservan su retención independiente. Nunca reconstruir procedencia desde configuración vigente ni exponer prompts, snapshots o IDs de continuación por HTTP.

Provenance incluye ChannelId requerido y opaco, derivado del contexto capturado del turno; los consumidores no interpretan IDs privados de otro canal. InteractionMode distingue Assistant y Manual como hecho explícito del servidor: el canal lo fija y revalida, y el motor lo conserva en el sello, alcance y huella. Unspecified=0 conserva los contratos existentes sin ese concepto, como Inicio; se omite del JSON y no cambia su huella ni concede autoridad automática. Los modos explícitos añaden una extensión versionada a la huella. No inferir modo de ChannelId, del operador ni de la configuración actual.

Audio es un descriptor privado del servidor, alternativo al texto, con hash/tamaño/nombre/MIME y vencimiento propio. Submit/Get conservan el mismo RequestId y autorización del canal; NormalizedAudio es una salida técnica tipada, sin propuesta ni autoridad para ejecutar acciones. ToString redacta descriptor y transcripción; no se acepta desde contratos HTTP o argumentos del modelo.

SourceRecordId es una referencia opcional fijada por el servidor para el origen exacto de una captura. Se omite cuando es null para preservar los contextos y huellas que no tienen origen. Submit sella su valor y Get/Cancel lo comparan sin reconstruirlo desde el historial ni desde argumentos del modelo.

ChannelConversationId es un Guid opcional y opaco del dueño del canal, distinto de la conversación del motor. Se sella y participa en SameScope y en una extensión de huella sólo cuando existe; null conserva el formato anterior y el grupo de reserva sin ámbito. No es un argumento del modelo ni fragmenta cuotas por actor/canal/propósito.
