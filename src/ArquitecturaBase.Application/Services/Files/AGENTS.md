# Files: limpieza de contenido privado

El almacenamiento es neutral; no nombra consumidores. Purga objetos de más de un día sin metadatos confirmados ni referencia durable, incluidos thumbnails. Solo lecturas DB y borrado idempotente externo; sin transaccion ni protocolo durable. Tiempo de TimeProvider y sin claves/nombres en logs.

Consultá referencias por lotes de hasta 128 claves. La miniatura actual se deriva del original con `.thumb.jpg`; el lector usa el índice de StorageKey, sin OR sobre ThumbnailKey ni una consulta por blob.

Antes de borrar un lote, unir metadata confirmada y referencias de todas las `IPrivateFileRetentionSource`. Un efecto durable pendiente puede conservar su blob más de un día; no hay un plazo máximo asumido. Si una fuente falla, no borrar ningún objeto de ese lote.
