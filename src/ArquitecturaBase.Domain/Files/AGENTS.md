# Files: metadatos privados

StoredFile hereda Entity e IAuditable. Dueño, nombre saneado de descarga, tipo verificado, tamaño, SHA256 y claves del proveedor se guardan junto con la referencia del consumidor en una única transacción.
Sin contenido binario en PostgreSQL, URLs públicas, versiones, staging ni referencias a módulos opcionales. ToString nunca muestra nombres, hashes o claves. La vida del archivo la determina su consumidor.

`FileErrors` es neutral (N22): `Files.Storage.Unavailable` cuando no hay almacenamiento privado o no quedó la copia, `Files.Content.Missing` cuando falta el objeto guardado y `Files.Content.NotFound` cuando el archivo no pertenece a lo pedido. Un módulo que baja archivos de un proveedor declara su falla en su propio dominio.
