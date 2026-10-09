# 0012: Azure Blob Storage como primer proveedor de archivos

## Estado

Aceptada, 2026-10-02, para elegir el proveedor y probar desde local contra Azure real. Implementación pendiente; el modelo funcional y el alcance del fondo siguen en elaboración.

## Contexto

El fondo del chat requiere guardar imágenes y se quiere preparar una capacidad reutilizable para otros archivos. El usuario conoce Azure Blob Storage y lo eligió como primer proveedor, aunque la aplicación todavía no está publicada y falta crear la cuenta de desarrollo.

## Decisión

El primer proveedor será Azure Blob Storage. Se verificará desde la PC de desarrollo contra una cuenta real de Azure, conservando la separación entre contratos de Application y el adaptador del SDK en Infrastructure.

## Consecuencias

- No hace falta publicar la API para conectarse al storage por HTTPS. Front, API, PostgreSQL y Redis pueden seguir corriendo localmente.
- La primera implementación usa `Azure.Storage.Blobs` y `Azure.Identity` dentro de Infrastructure; las credenciales nunca llegan al navegador.
- Una cuenta de desarrollo con contenedores privados permite preparar y verificar el acceso antes del flujo completo. La [guía local](../guides/azure-blob-en-local.md) separa esa prueba de la integración de la aplicación.
- Azurite queda como complemento para pruebas repetibles, sin reemplazar la verificación pedida en Azure real. No se implementan adaptadores S3/AWS en esta etapa.
- La estructura, autenticación propuesta, catálogo, procesamiento e historial se detallan en el [plan](../plans/2026-10-02-almacenamiento-de-archivos.md); esta elección no da por aprobados ni implementados sus puntos pendientes.

## Alternativas descartadas

- Elegir primero AWS/S3 o un servidor propio: el usuario prefirió Azure por su experiencia previa.
- Probar solamente con un emulador: no verifica acceso ni almacenamiento en la cuenta real solicitada.
- Esperar al despliegue de la aplicación: la conexión al storage no depende de dónde corre la API.
