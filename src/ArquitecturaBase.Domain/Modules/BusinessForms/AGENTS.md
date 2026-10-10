# BusinessForms: modelo e identidades

Leer [formularios](../../../../docs/features/formularios.md) y su contrato antes de modificar.
- Domain usa BCL y Result. Entidades Definition/DraftChange heredan Entity e IAuditable; auditoría exclusivamente por interceptores.
- UUID v7 canónicos creados por servidor. Mismo tipo/renombre/orden conserva identidad; cambio de tipo/copia crea otra. Ledger retirado no se reinicia al reconstituir.
- Las fábricas de identidad usadas por FormDraftPolicy permanecen en Domain. El guardado real resuelve IDs y revisiones contra la definición en Application; sus garantías se prueban por HTTP y persistencia. No mantener otro modelo de borrador sin consumidor productivo.
- Un comprobante protege el resultado por 30 días. Expirar limpia payload, conserva metadata contra reutilización del UUID. Sin dependencias IA/WhatsApp ni respuestas implementadas.

Contenido puro localizado copiado/Frozen; original válido y secundarios pendientes, sin compartir mapas mutables.
