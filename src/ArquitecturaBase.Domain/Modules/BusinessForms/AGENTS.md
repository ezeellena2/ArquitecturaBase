# BusinessForms: modelo e identidades

Leer [formularios](../../../../docs/features/formularios.md) y su contrato antes de modificar.
- Domain usa BCL y Result. Entidades Definition/DraftChange heredan Entity e IAuditable; auditoría exclusivamente por interceptores.
- UUID v7 canónicos creados por servidor. Mismo tipo/renombre/orden conserva identidad; cambio de tipo/copia crea otra. Ledger retirado no se reinicia al reconstituir.
- Snapshot puro conserva colecciones readOnly/Frozen. El escritor resuelve IDs contra el borrador de la definición, nunca por etiqueta o UUID nuevo del cliente.
- Un comprobante protege el resultado por 30 días. Expirar limpia payload, conserva metadata contra reutilización del UUID. Sin dependencias IA/WhatsApp ni respuestas implementadas.

Contenido puro localizado copiado/Frozen; original válido y secundarios pendientes, sin compartir mapas mutables.
