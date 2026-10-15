# Files: metadatos privados

StoredFile hereda Entity e IAuditable. Dueño, nombre original, tipo verificado, tamaño, SHA256 y claves del proveedor se guardan junto con la referencia del consumidor en una única transacción.
Sin contenido binario en PostgreSQL, URLs públicas, versiones, staging ni referencias a módulos opcionales. ToString nunca muestra nombres, hashes o claves. La vida del archivo la determina su consumidor.
