# Files: contenido privado en Azure Blob

Implementa el puerto neutral `IPrivateFileStore`; el consumidor autoriza y guarda los metadatos en su transacción.
- SDK de Azure sólo acá. Credencial explícita: AzureCli, VisualStudio, ManagedIdentity o ConnectionString (Azurite local). Sin fallback ni credenciales en logs.
- No crear contenedores al arrancar; se provisionan privados fuera de la API. Nunca devolver URLs o SAS al navegador.
- Claves determinísticas del servidor: sobreescribir y borrar son idempotentes; reintentos del SDK, sin staging ni estados durables.
- Pruebas de configuración sin red; contenido/reescritura/borrado contra Azurite al cierre. Azure real se prueba cuando exista la cuenta (ADR 0012).
