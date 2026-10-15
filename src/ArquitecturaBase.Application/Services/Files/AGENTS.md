# Files: limpieza de contenido privado

El almacenamiento es neutral; no nombra consumidores. Purga objetos de mas de un dia que no tienen metadatos confirmados, incluidos thumbnails. Solo lecturas DB y borrado idempotente externo; sin transaccion ni protocolo durable. Tiempo de TimeProvider y sin claves/nombres en logs.

Consultá referencias por lotes de hasta 128 claves. La miniatura actual se deriva del original con `.thumb.jpg`; el lector usa el índice de StorageKey, sin OR sobre ThumbnailKey ni una consulta por blob.
