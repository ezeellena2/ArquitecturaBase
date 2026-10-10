# BusinessForms: persistencia y protección

Leer [formularios](../../../../docs/features/formularios.md).
- EF de negocio en Repository/Reader; sólo el lector usa AsNoTracking. Configuraciones y registro propios del módulo.
- Locks: actor/UUID primero y luego definiciones/prefix. Claves sólo en FormAdvisoryLockKeys. Por ahora serializa escrituras de borrador globalmente; medir antes de afinar.
- DataProtection con propósito propio v1 vinculado a fila/actor/UUID/definición/kind. Codec tipado cerrado; huella SHA256 sólo dentro del resultado protegido, jamás en columnas/logs.
- Purga seguida en lotes de 100: IUnitOfWork, sin ExecuteUpdate. Worker cada 15 minutos y al guardar; vencimiento bloquea lectura aunque falte purga física.
- La inicial agrega Definition/DraftChange; la publicación pertenece al siguiente incremento. Anillo externo protegido compartido de la plantilla; ninguna credencial IA o Meta.

NameJson autoritativo; Name derivado original. DisplayNameEs/En calculados por PostgreSQL sostienen búsqueda/orden/facetas coherentes con el nombre visible de UI, con referencia original sólo si la traducción está pendiente. No edición independiente ni orden/paginación en memoria. D5 regenera únicamente la inicial no commiteada; no variantes de recibo legadas ficticias ni update de base local.
