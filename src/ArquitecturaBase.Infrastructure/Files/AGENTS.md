# Files: contenido privado en Azure Blob

Implementa el puerto neutral `IPrivateFileStore`; el consumidor autoriza y guarda los metadatos en su transacción.
- SDK de Azure sólo acá. Credencial explícita: AzureCli, VisualStudio, ManagedIdentity o ConnectionString (Azurite local). Sin fallback ni credenciales en logs.
- No crear contenedores al arrancar; se provisionan privados fuera de la API. Nunca devolver URLs o SAS al navegador.
- Claves determinísticas del servidor: sobreescribir y borrar son idempotentes; reintentos del SDK, sin staging ni estados durables.
- Pruebas de configuración sin red; contenido/reescritura/borrado contra Azurite al cierre. Azure real se prueba cuando exista la cuenta (ADR 0012).

SkiaSharp crea miniaturas JPEG de JPEG/PNG y respeta orientación sin reescribir el original; el paquete Linux acompaña el despliegue. OpenMcdf inspecciona los streams CFB de Word/Excel antiguos. OOXML se verifica por su manifiesto y parte principal con ZIP/XML de la BCL, sin renderizar ni ejecutar documentos. Hash y límites se verifican antes de almacenar.
